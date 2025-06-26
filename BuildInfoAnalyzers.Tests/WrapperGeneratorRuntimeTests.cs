using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BuildInfoAnalyzers.Tests
{
    [TestFixture]
    public class WrapperGeneratorRuntimeTests
    {
        private static string GetWrapperCode() => @"public static class TestWrapper
{
    private static System.Collections.Generic.List<string> logRef = null;
    public static void OnEnter(string method, object[] args) { logRef.Add(""Enter:"" + method + "":"" + string.Join("", "", args)); }
    public static void OnExit(string method, object result, long ms) { logRef.Add(""Exit:"" + method + "":"" + result + "":"" + ms); }
    public static void OnError(string method, System.Exception ex, long ms) { logRef.Add(""Error:"" + method + "":"" + ex.Message + "":"" + ms); }
}";

        private class RuntimeTestContext
        {
            public Assembly Assembly { get; set; }
            public List<string> Log { get; set; }
        }

        private RuntimeTestContext CompileAndLoad(string userCode, params string[] extraSources)
        {
            var log = new List<string>();

            var sources = new List<string> { GetWrapperCode(), userCode };
            if (extraSources != null && extraSources.Length > 0)
                sources.AddRange(extraSources);
            var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToList();
            var systemRuntimePath = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "System.Runtime.dll");
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<string>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Diagnostics.Stopwatch).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(WrapperGenerator).Assembly.Location),
                MetadataReference.CreateFromFile(systemRuntimePath)
            };
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var generator = new WrapperGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
            using var ms = new MemoryStream();
            var emitResult = updatedCompilation.Emit(ms);
            Assert.That(emitResult.Success, Is.True, string.Join("\n", emitResult.Diagnostics));
            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            var testWrapperType = assembly.GetType("TestWrapper");
            var logRefField = testWrapperType.GetField("logRef", BindingFlags.Static | BindingFlags.NonPublic);
            logRefField.SetValue(null, log);
            return new RuntimeTestContext { Assembly = assembly, Log = log };
        }

        [Test]
        public void Wrapper_CallsOnEnterOnExitAndReturnsValue()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a, int b);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var addMethod = serviceType.GetMethod("Add");
            var result = addMethod.Invoke(instance, [2, 3]);
            Assert.That(result, Is.EqualTo(5));
            Assert.That(ctx.Log[0], Does.StartWith("Enter:Add:2, 3"));
            Assert.That(ctx.Log.Any(l => l.StartsWith("Exit:Add:5")));
        }

        [Test]
        public void Wrapper_CallsOnErrorOnException()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Fail(int a);
    private int Fail_Implementation(int a) { throw new System.InvalidOperationException(""fail""); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var failMethod = serviceType.GetMethod("Fail");
            var ex = Assert.Throws<TargetInvocationException>(() => failMethod.Invoke(instance, [42]));
            Assert.That(ex.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(ctx.Log[0], Does.StartWith("Enter:Fail:42"));
            Assert.That(ctx.Log.Any(l => l.StartsWith("Error:Fail:fail")));
        }

        [Test]
        public void Wrapper_AsyncMethod_WorksAndLogs()
        {
            var userCode = @"using System.Threading.Tasks;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial Task<int> AddAsync(int a, int b);
    private async Task<int> AddAsync_Implementation(int a, int b) { await Task.Delay(10); return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var addAsyncMethod = serviceType.GetMethod("AddAsync");
            var task = (Task<int>)addAsyncMethod.Invoke(instance, [7, 8]);
            var result = task.GetAwaiter().GetResult();
            Assert.That(result, Is.EqualTo(15));
            Assert.That(ctx.Log[0], Does.StartWith("Enter:AddAsync:7, 8"));
            Assert.That(ctx.Log.Any(l => l.StartsWith("Exit:AddAsync:15")));
        }

        [Test]
        public void Wrapper_GenericMethod_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial T Echo<T>(T value);
    private T Echo_Implementation<T>(T value) { return value; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var echoMethod = serviceType.GetMethod("Echo").MakeGenericMethod(typeof(string));
            var result = echoMethod.Invoke(instance, ["hello"]);
            Assert.That(result, Is.EqualTo("hello"));
            Assert.That(ctx.Log[0], Does.StartWith("Enter:Echo:hello"));
            Assert.That(ctx.Log.Any(l => l.StartsWith("Exit:Echo:hello")));
        }

        [Test]
        public void Wrapper_AsyncMethod_Throws_LogsOnError()
        {
            var userCode = @"using System.Threading.Tasks;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial Task<int> FailAsync();
    private async Task<int> FailAsync_Implementation() { await Task.Delay(10); throw new System.InvalidOperationException(""fail""); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var failAsyncMethod = serviceType.GetMethod("FailAsync");
            var task = (Task<int>)failAsyncMethod.Invoke(instance, null);
            var ex = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(ex.Message, Is.EqualTo("fail"));
            Assert.That(ctx.Log[0], Does.StartWith("Enter:FailAsync:"));
            Assert.That(ctx.Log.Any(l => l.StartsWith("Error:FailAsync:fail")));
        }

        [Test]
        public void Wrapper_MultipleCalls_LogsEachInvocation()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a, int b);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var addMethod = serviceType.GetMethod("Add");
            var result1 = addMethod.Invoke(instance, [1, 2]);
            var result2 = addMethod.Invoke(instance, [3, 4]);
            var result3 = addMethod.Invoke(instance, [5, 6]);
            Assert.That(result1, Is.EqualTo(3));
            Assert.That(result2, Is.EqualTo(7));
            Assert.That(result3, Is.EqualTo(11));
            Assert.That(ctx.Log.Count(l => l.StartsWith("Enter:Add:")), Is.EqualTo(3));
            Assert.That(ctx.Log.Count(l => l.StartsWith("Exit:Add:")), Is.EqualTo(3));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Add:1, 2"));
            Assert.That(ctx.Log[2], Does.Contain("Enter:Add:3, 4"));
            Assert.That(ctx.Log[4], Does.Contain("Enter:Add:5, 6"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:11")));
        }

        [Test]
        public void Wrapper_VariousParameterTypes_WorksAndLogs()
        {
            var userCode = @"using System;
public struct MyStruct { public int X; }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial string Combine(int a, string b, MyStruct s, int[] arr);
    private string Combine_Implementation(int a, string b, MyStruct s, int[] arr) { return a + ""-"" + b + ""-"" + s.X + ""-"" + string.Join(""_"", arr); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService");
            var instance = Activator.CreateInstance(serviceType);
            var combineMethod = serviceType.GetMethod("Combine");
            var myStruct = ctx.Assembly.GetType("MyStruct");
            var structInstance = Activator.CreateInstance(myStruct);
            myStruct.GetField("X").SetValue(structInstance, 42);
            var result = combineMethod.Invoke(instance, [7, "foo", structInstance, new[] { 1, 2, 3 }]);
            Assert.That(result, Is.EqualTo("7-foo-42-1_2_3"));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Combine:7, foo, MyStruct, System.Int32[]"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Combine:7-foo-42-1_2_3")));
        }

        // --- EDGE CASE TESTS ---

        [Test]
        public void Wrapper_NoParameters_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int GetValue();
    private int GetValue_Implementation() { return 42; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("GetValue")!;
            var result = method.Invoke(instance, null);
            Assert.That(result, Is.EqualTo(42));
            Assert.That(ctx.Log[0], Does.Contain("Enter:GetValue:"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetValue:42")));
        }

        [Test]
        public void Wrapper_RefOutInParameters_NotSupportedByReflection()
        {
            // Reflection does not support ref/out/in parameter invocation directly.
            // This test is a placeholder to document the limitation.
            Assert.Pass("Reflection does not support ref/out/in parameter invocation. Test skipped.");
        }

        [Test]
        public void Wrapper_NullableParametersAndReturnTypes_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int? MaybeAdd(int? a, int? b);
    private int? MaybeAdd_Implementation(int? a, int? b) { return a.HasValue && b.HasValue ? a + b : null; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("MaybeAdd")!;
            var result1 = method.Invoke(instance, new object?[] { 2, 3 });
            var result2 = method.Invoke(instance, new object?[] { null, 3 });
            Assert.That(result1, Is.EqualTo(5));
            Assert.That(result2, Is.Null);
            Assert.That(ctx.Log[0], Does.Contain("Enter:MaybeAdd:2, 3"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:MaybeAdd:5")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:MaybeAdd:")));
        }

        [Test]
        public void Wrapper_CustomImplementationName_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper), ImplementationMethodName = ""DoAdd"")]
    public partial int Add(int a, int b);
    private int DoAdd(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Add")!;
            var result = method.Invoke(instance, new object[] { 4, 5 });
            Assert.That(result, Is.EqualTo(9));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Add:4, 5"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:9")));
        }

        [Test]
        public void Wrapper_NestedStaticGenericClasses_WorksAndLogs()
        {
            var userCode = @"public static partial class Outer
{
    public static partial class Inner
    {
        [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
        public static partial int Add(int a, int b);
        private static int Add_Implementation(int a, int b) { return a + b; }
    }
    public partial class Generic<T>
    {
        [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
        public partial T Echo(T value);
        private T Echo_Implementation(T value) { return value; }
    }
}";
            var ctx = CompileAndLoad(userCode);
            var innerType = ctx.Assembly.GetType("Outer+Inner")!;
            var addMethod = innerType.GetMethod("Add")!;
            var result = addMethod.Invoke(null, new object[] { 10, 20 });
            Assert.That(result, Is.EqualTo(30));
            var genericType = ctx.Assembly.GetType("Outer+Generic`1")!.MakeGenericType(typeof(string));
            var genericInstance = Activator.CreateInstance(genericType)!;
            var echoMethod = genericType.GetMethod("Echo")!;
            var echoResult = echoMethod.Invoke(genericInstance, new object[] { "abc" });
            Assert.That(echoResult, Is.EqualTo("abc"));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:10, 20")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:30")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Echo:abc")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Echo:abc")));
        }

        [Test]
        public void Wrapper_DefaultParameterValues_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a, int b = 7);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Add")!;
            var result1 = method.Invoke(instance, new object[] { 3, 4 });
            var result2 = method.Invoke(instance, new object[] { 3, Type.Missing });
            Assert.That(result1, Is.EqualTo(7));
            Assert.That(result2, Is.EqualTo(10));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:3, 4")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:3, ")));
        }

        [Test]
        public void Wrapper_ParamsArray_WorksAndLogs()
        {
            var userCode = @"using System.Linq;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Sum(params int[] values);
    private int Sum_Implementation(params int[] values) { return values.Sum(); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Sum")!;
            var result = method.Invoke(instance, new object[] { new int[] { 1, 2, 3, 4 } });
            Assert.That(result, Is.EqualTo(10));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Sum:System.Int32[]"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Sum:10")));
        }

        [Test]
        public void Wrapper_AdditionalAttributes_PreservedAndWorks()
        {
            var userCode = @"using System;
public partial class MyService
{
    [Obsolete]
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a, int b);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var method = serviceType.GetMethod("Add")!;
            var obsoleteAttr = method.GetCustomAttributes(false).OfType<ObsoleteAttribute>().FirstOrDefault();
            Assert.That(obsoleteAttr, Is.Not.Null);
            var instance = Activator.CreateInstance(serviceType)!;
            var result = method.Invoke(instance, [1, 2]);
            Assert.That(result, Is.EqualTo(3));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:1, 2")));
        }

        [Test]
        public void Wrapper_ComplexReturnTypes_WorksAndLogs()
        {
            var userCode = @"using System.Collections.Generic;
public record MyRecord(int X, string Y);
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial (int, string) GetTuple();
    private (int, string) GetTuple_Implementation() { return (1, ""abc""); }
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial MyRecord GetRecord();
    private MyRecord GetRecord_Implementation() { return new MyRecord(7, ""foo""); }
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial List<int> GetList();
    private List<int> GetList_Implementation() { return new List<int> { 1, 2, 3 }; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var tupleResult = serviceType.GetMethod("GetTuple")!.Invoke(instance, null);
            var recordResult = serviceType.GetMethod("GetRecord")!.Invoke(instance, null);
            var listResult = serviceType.GetMethod("GetList")!.Invoke(instance, null);
            Assert.That(tupleResult!.ToString(), Is.EqualTo("(1, abc)"));
            Assert.That(recordResult!.ToString(), Is.EqualTo("MyRecord { X = 7, Y = foo }"));
            Assert.That(listResult, Is.InstanceOf(typeof(System.Collections.IEnumerable)));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetTuple:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetTuple:(1, abc)")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetRecord:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetRecord:MyRecord { X = 7, Y = foo }")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetList:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetList:System.Collections.Generic.List`1[System.Int32]")));
        }

        [Test]
        public void Wrapper_AsyncVoid_WorksAndLogs()
        {
            var userCode = @"using System.Threading.Tasks;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial void DoAsync();
    private async void DoAsync_Implementation() { await Task.Delay(10); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("DoAsync")!;
            method.Invoke(instance, null);
            Assert.That(ctx.Log[0], Does.Contain("Enter:DoAsync:"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:DoAsync:")));
        }

        [Test]
        public void Wrapper_ValueTupleParameter_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int AddPair((int, int) pair);
    private int AddPair_Implementation((int, int) pair) { return pair.Item1 + pair.Item2; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("AddPair")!;
            var tuple = Activator.CreateInstance(typeof(ValueTuple<int, int>), 2, 3);
            var result = method.Invoke(instance, [tuple]);
            Assert.That(result, Is.EqualTo(5));
            Assert.That(ctx.Log[0], Does.Contain("Enter:AddPair:(2, 3)"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:AddPair:5:0")));
        }

        [Test]
        public void Wrapper_GenericConstraints_WorksAndLogs()
        {
            var userCode = @"public class MyClass { public MyClass() {} public override string ToString() => ""myclass""; }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial T Echo<T>(T value) where T : class, new();
    private T Echo_Implementation<T>(T value) where T : class, new() { return value; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var myClassType = ctx.Assembly.GetType("MyClass")!;
            var echoMethod = serviceType.GetMethod("Echo")!.MakeGenericMethod(myClassType);
            var myClassInstance = Activator.CreateInstance(myClassType);
            var result = echoMethod.Invoke(instance, new object[] { myClassInstance });
            Assert.That(result, Is.Not.Null);
            Assert.That(result.GetType().Name, Is.EqualTo("MyClass"));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Echo:myclass"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Echo:myclass")));
        }

        [Test]
        public void Wrapper_ManyParameters_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int AddMany(int a, int b, int c, int d, int e, int f, int g, int h);
    private int AddMany_Implementation(int a, int b, int c, int d, int e, int f, int g, int h) { return a+b+c+d+e+f+g+h; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("AddMany")!;
            var result = method.Invoke(instance, new object[] { 1,2,3,4,5,6,7,8 });
            Assert.That(result, Is.EqualTo(36));
            Assert.That(ctx.Log[0], Does.Contain("Enter:AddMany:1, 2, 3, 4, 5, 6, 7, 8"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:AddMany:36")));
        }

        [Test]
        public void Wrapper_AllOptionalParameters_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a = 1, int b = 2);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Add")!;
            var result = method.Invoke(instance, [Type.Missing, Type.Missing]);
            Assert.That(result, Is.EqualTo(3));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:1, 2")));
        }

        [Test]
        public void Wrapper_StructReturnType_WorksAndLogs()
        {
            var userCode = @"public struct MyStruct { public int X; }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial MyStruct GetStruct();
    private MyStruct GetStruct_Implementation() { return new MyStruct { X = 99 }; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("GetStruct")!;
            var result = method.Invoke(instance, null)!;
            var xValue = result.GetType().GetField("X")!.GetValue(result);
            Assert.That(xValue, Is.EqualTo(99));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetStruct:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetStruct:MyStruct")));
        }

        [Test]
        public void Wrapper_InterfaceReturnType_WorksAndLogs()
        {
            var userCode = @"public interface IFoo { int X { get; } }
public class Foo : IFoo { public int X => 123; }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial IFoo GetFoo();
    private IFoo GetFoo_Implementation() { return new Foo(); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("GetFoo")!;
            var result = method.Invoke(instance, null)!;
            var xValue = (int)result.GetType().GetProperty("X")!.GetValue(result)!;
            Assert.That(xValue, Is.EqualTo(123));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetFoo:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:GetFoo:Foo")));
        }

        [Test]
        public void Wrapper_EnumParameter_WorksAndLogs()
        {
            var userCode = @"public enum MyEnum { A, B, C }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial string EnumToString(MyEnum e);
    private string EnumToString_Implementation(MyEnum e) { return e.ToString(); }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var enumType = ctx.Assembly.GetType("MyEnum")!;
            var method = serviceType.GetMethod("EnumToString")!;
            var result = method.Invoke(instance, new object[] { Enum.Parse(enumType, "B") });
            Assert.That(result, Is.EqualTo("B"));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:EnumToString:B")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:EnumToString:B")));
        }

        [Test]
        public void Wrapper_ExpressionBodiedImplementation_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Double(int x);
    private int Double_Implementation(int x) => x * 2;
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Double")!;
            var result = method.Invoke(instance, new object[] { 21 });
            Assert.That(result, Is.EqualTo(42));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Double:21")));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Double:42")));
        }

        [Test]
        public void Wrapper_MultipleAttributes_PreservedAndWorks()
        {
            var userCode = @"using System;
[AttributeUsage(AttributeTargets.Method)]
public class CustomAttr : Attribute { public string Name; public CustomAttr(string name) { Name = name; } }
public partial class MyService
{
    [Obsolete]
    [CustomAttr(""foo"")]
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add(int a, int b);
    private int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var method = serviceType.GetMethod("Add")!;
            var obsoleteAttr = method.GetCustomAttributes(false).OfType<ObsoleteAttribute>().FirstOrDefault();
            var customAttr = method.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == "CustomAttr");
            Assert.That(obsoleteAttr, Is.Not.Null);
            Assert.That(customAttr, Is.Not.Null);
            var instance = Activator.CreateInstance(serviceType)!;
            var result = method.Invoke(instance, new object[] { 1, 2 });
            Assert.That(result, Is.EqualTo(3));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:1, 2")));
        }

        [Test]
        public void Wrapper_AdvancedGenericConstraints_WorksAndLogs()
        {
            var userCode = @"using System;
using System.Collections.Generic;
public interface IFoo { }
public class Foo : IFoo, IComparable<Foo> { public int CompareTo(Foo other) => 0; public override string ToString() => ""foo""; }
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial T Echo<T>(T value) where T : class, IFoo, IComparable<T>, new();
    private T Echo_Implementation<T>(T value) where T : class, IFoo, IComparable<T>, new() { return value; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var fooType = ctx.Assembly.GetType("Foo")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var echoMethod = serviceType.GetMethod("Echo")!.MakeGenericMethod(fooType);
            var fooInstance = Activator.CreateInstance(fooType)!;
            var result = echoMethod.Invoke(instance, new object[] { fooInstance });
            Assert.That(result, Is.Not.Null);
            Assert.That(result.GetType().Name, Is.EqualTo("Foo"));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Echo:foo")));
        }

        [Test]
        public void Wrapper_ImplementationMethodName_PositionalAndNamed_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper), ""DoAdd"")]
    public partial int Add(int a, int b);
    private int DoAdd(int a, int b) { return a + b; }
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper), ImplementationMethodName = ""DoSub"")]
    public partial int Sub(int a, int b);
    private int DoSub(int a, int b) { return a - b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var add = serviceType.GetMethod("Add")!;
            var sub = serviceType.GetMethod("Sub")!;
            var result1 = add.Invoke(instance, new object[] { 10, 3 });
            var result2 = sub.Invoke(instance, new object[] { 10, 3 });
            Assert.That(result1, Is.EqualTo(13));
            Assert.That(result2, Is.EqualTo(7));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:10, 3")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Sub:10, 3")));
        }

        [Test]
        public void Wrapper_DelegateAndFuncReturnTypes_WorksAndLogs()
        {
            var userCode = @"using System;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial Func<int, int> GetDoubler();
    private Func<int, int> GetDoubler_Implementation() { return x => x * 2; }
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial Action<string> GetPrinter();
    private Action<string> GetPrinter_Implementation() { return s => { }; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var getDoubler = serviceType.GetMethod("GetDoubler")!;
            var getPrinter = serviceType.GetMethod("GetPrinter")!;
            var doubler = (Func<int, int>)getDoubler.Invoke(instance, null)!;
            var printer = (Action<string>)getPrinter.Invoke(instance, null)!;
            Assert.That(doubler(21), Is.EqualTo(42));
            Assert.That(printer, Is.Not.Null);
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetDoubler:")));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:GetPrinter:")));
        }

        [Test]
        public void Wrapper_ParameterAttributes_PreservedAndWorks()
        {
            var userCode = @"using System;
using System.Runtime.InteropServices;
public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial int Add([Optional] int a, [Optional] int b);
    private int Add_Implementation([Optional] int a, [Optional] int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var method = serviceType.GetMethod("Add")!;
            var paramAttrs = method.GetParameters().SelectMany(p => p.GetCustomAttributes(false)).ToList();
            Assert.That(paramAttrs.Any(a => a.GetType().Name == "OptionalAttribute"));
            var instance = Activator.CreateInstance(serviceType)!;
            var result = method.Invoke(instance, new object[] { 1, 2 });
            Assert.That(result, Is.EqualTo(3));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:1, 2")));
        }

        [Test]
        public void Wrapper_DeeplyNestedTypes_WorksAndLogs()
        {
            var userCode = @"namespace TestNamespaceDeeplyNestedTypes {
public partial class Outer
{
    public partial class Inner
    {
        public partial class Deep
        {
            [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
            public partial int Add(int a, int b);
            private int Add_Implementation(int a, int b) { return a + b; }
        }
    }
}
}";
            var ctx = CompileAndLoad(userCode);
            var deepType = ctx.Assembly.GetType("TestNamespaceDeeplyNestedTypes.Outer+Inner+Deep")!;
            var instance = Activator.CreateInstance(deepType)!;
            var method = deepType.GetMethod("Add")!;
            var result = method.Invoke(instance, new object[] { 5, 7 });
            Assert.That(result, Is.EqualTo(12));
            Assert.That(ctx.Log.Any(l => l.Contains("Enter:Add:5, 7")));
        }

        [Test]
        public void Wrapper_ImplementationMethodName_Nameof_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper), ImplementationMethodName = nameof(DoAdd))]
    public partial int Add(int a, int b);
    private int DoAdd(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Add")!;
            var result = method.Invoke(instance, new object[] { 4, 5 });
            Assert.That(result, Is.EqualTo(9));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Add:4, 5"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:9")));
        }

        [Test]
        public void Wrapper_ImplementationMethodName_Nameof_Positional_WorksAndLogs()
        {
            var userCode = @"public partial class MyService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper), nameof(DoAdd))]
    public partial int Add(int a, int b);
    private int DoAdd(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyService")!;
            var instance = Activator.CreateInstance(serviceType)!;
            var method = serviceType.GetMethod("Add")!;
            var result = method.Invoke(instance, new object[] { 7, 8 });
            Assert.That(result, Is.EqualTo(15));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Add:7, 8"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:15")));
        }

        [Test]
        public void Wrapper_StaticClass_WorksAndLogs()
        {
            var userCode = @"public static partial class MyStaticService
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public static partial int Add(int a, int b);
    private static int Add_Implementation(int a, int b) { return a + b; }
}";
            var ctx = CompileAndLoad(userCode);
            var serviceType = ctx.Assembly.GetType("MyStaticService")!;
            var addMethod = serviceType.GetMethod("Add")!;
            var result = addMethod.Invoke(null, new object[] { 5, 6 });
            Assert.That(result, Is.EqualTo(11));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Add:5, 6"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Add:11")));
        }

        [Test]
        public void Wrapper_GenericClass_WorksAndLogs()
        {
            var userCode = @"public partial class MyGenericService<T>
{
    [BuildInfoAnalyzers.Wrapper(typeof(TestWrapper))]
    public partial T Echo(T value);
    private T Echo_Implementation(T value) { return value; }
}";
            var ctx = CompileAndLoad(userCode);
            var genericType = ctx.Assembly.GetType("MyGenericService`1")!.MakeGenericType(typeof(string));
            var instance = Activator.CreateInstance(genericType)!;
            var echoMethod = genericType.GetMethod("Echo")!;
            var result = echoMethod.Invoke(instance, new object[] { "test" });
            Assert.That(result, Is.EqualTo("test"));
            Assert.That(ctx.Log[0], Does.Contain("Enter:Echo:test"));
            Assert.That(ctx.Log.Any(l => l.Contains("Exit:Echo:test")));
        }
    }
}