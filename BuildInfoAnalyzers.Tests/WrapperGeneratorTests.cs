using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BuildInfoAnalyzers.Tests
{
    [TestFixture]
    public class WrapperGeneratorTests
    {
        private const string TestWrapperCode = @"public static class TestWrapper
{
    public static void OnEnter(string method, object[] args) {}
    public static void OnExit(string method, object? result, long ms) {}
    public static void OnError(string method, System.Exception ex, long ms) {}
}
";

        [Test]
        public void Generator_HandlesVoidMethod()
        {
            var source = CreateSource("public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial void DoWork();",
                "private void DoWork_Implementation() {}");
            var expected = CreateExpectedSource("public partial class MyService", "public partial void DoWork()",
                "new object[0]",
                "DoWork_Implementation();");
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesSyncMethodWithParamAndReturn()
        {
            var source = CreateSource("public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial string DoWork(string input);",
                "private string DoWork_Implementation(string input) => input;");
            var expectedTryBlock =
                "var methodResult = DoWork_Implementation(input);\n            __wrapper_log_result = methodResult;\n            return methodResult;";
            var expected = CreateExpectedSource("public partial class MyService",
                "public partial string DoWork(string input)",
                "new object[] { input }", expectedTryBlock);
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesAsyncTask()
        {
            var source = CreateSource("public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial Task DoWork();",
                "private Task DoWork_Implementation() => Task.CompletedTask;");
            var expected = CreateExpectedSource("public partial class MyService",
                "public async partial System.Threading.Tasks.Task DoWork()", "new object[0]",
                "await DoWork_Implementation();");
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesGenericAsyncTask()
        {
            var source = CreateSource("public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial Task<string> DoWork(string s);",
                "private Task<string> DoWork_Implementation(string s) => Task.FromResult(s);");
            var expectedTryBlock =
                "var methodResult = await DoWork_Implementation(s);\n            __wrapper_log_result = methodResult;\n            return methodResult;";
            var expected = CreateExpectedSource("public partial class MyService",
                "public async partial System.Threading.Tasks.Task<string> DoWork(string s)", "new object[] { s }",
                expectedTryBlock);
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        [TestCase("ref int val", "ref val", "new object[] { val }")]
        [TestCase("out string result", "out result", "new object[0]")]
        [TestCase("in Guid id", "in id", "new object[] { id }")]
        public void Generator_WithRefOutInParameters_PassesThroughCorrectly(string paramDef, string callParam,
            string onEnter)
        {
            var source = CreateSource(
                "public partial class MyService",
                $"[Wrapper(typeof(TestWrapper))] public partial void Process({paramDef});",
                $"private void Process_Implementation({paramDef}) {{ {(paramDef.Contains("out") ? "result = \"ok\";" : "")} }}"
            );
            var generated = GetGeneratedSource(source, "MyService.Process.g.cs");
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace($"Process_Implementation({callParam});")));
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace($"TestWrapper.OnEnter(\"Process\", {onEnter})")));

            if (paramDef.Contains("out"))
            {
                Assert.That(NormalizeWhitespace(generated)
                    .Contains(NormalizeWhitespace($"__wrapper_log_result = {callParam.Replace("out ", "")};")));
            }

            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        public void Generator_HandlesGenericMethodWithConstraints_GeneratesCorrectly()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial T Echo<T>(T value) where T : class;",
                "private T Echo_Implementation<T>(T value) where T : class => value;"
            );
            var expected = "public partial T Echo<T>(T value) where T : class";
            var generated = GetGeneratedSource(source, "MyService.Echo.g.cs");
            Assert.That(NormalizeWhitespace(generated).Contains(NormalizeWhitespace(expected)));
            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        [TestCase("public")]
        [TestCase("internal")]
        [TestCase("private")]
        [TestCase("")]
        public void Generator_HandlesAccessibilityModifiers(string accessibility)
        {
            var source = CreateSource(
                "public partial class MyService",
                $"[Wrapper(typeof(TestWrapper))] {accessibility} partial void DoWork();",
                $"{accessibility} void DoWork_Implementation() {{}}"
            );
            var expectedSignature = string.IsNullOrWhiteSpace(accessibility)
                ? "partial void DoWork()"
                : $"{accessibility} partial void DoWork()";
            var expected = CreateExpectedSource(
                "public partial class MyService",
                expectedSignature,
                "new object[0]",
                "DoWork_Implementation();"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesStaticMethod()
        {
            var source = CreateSource("public static partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public static partial void StaticMethod();",
                "private static void StaticMethod_Implementation() {}");
            var expected = CreateExpectedSource("public static partial class MyService",
                "public static partial void StaticMethod()",
                "new object[0]",
                "StaticMethod_Implementation();",
                methodName: "StaticMethod");
            var generatedSource = GetGeneratedSource(source, "MyService.StaticMethod.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesAsyncVoidMethod()
        {
            var source = CreateSource("public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial void AsyncVoidMethod();",
                "private async void AsyncVoidMethod_Implementation() {}"
            );
            // The wrapper should be async, but the call should NOT be awaited for async void
            var expectedTryBlock = "AsyncVoidMethod_Implementation();";
            var expected = CreateExpectedSource("public partial class MyService",
                "public async partial void AsyncVoidMethod()", "new object[0]",
                expectedTryBlock,
                methodName: "AsyncVoidMethod");
            var generatedSource = GetGeneratedSource(source, "MyService.AsyncVoidMethod.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesMethodInStruct()
        {
            var source = CreateSource("public partial struct MyStruct",
                "[Wrapper(typeof(TestWrapper))] private partial int StructMethod(int value);",
                "private int StructMethod_Implementation(int value) => value;");
            var expectedTryBlock =
                "var methodResult = StructMethod_Implementation(value);\n            __wrapper_log_result = methodResult;\n            return methodResult;";
            var expected = CreateExpectedSource("public partial struct MyStruct",
                "private partial int StructMethod(int value)",
                "new object[] { value }", expectedTryBlock,
                methodName: "StructMethod");
            var generatedSource = GetGeneratedSource(source, "MyStruct.StructMethod.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        [TestCase("protected internal")]
        [TestCase("private protected")]
        public void Generator_HandlesAdvancedAccessibilityModifiers(string accessibility)
        {
            var source = CreateSource(
                "public partial class MyService",
                $"[Wrapper(typeof(TestWrapper))] {accessibility} partial void DoWork();",
                $"{accessibility} void DoWork_Implementation() {{}}"
            );
            var expected = CreateExpectedSource(
                "public partial class MyService",
                $"{accessibility} partial void DoWork()",
                "new object[0]",
                "DoWork_Implementation();"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesMethodInRecord()
        {
            var source = CreateSource(
                "public partial record MyRecord",
                "[Wrapper(typeof(TestWrapper))] public partial int RecordMethod(int x);",
                "private int RecordMethod_Implementation(int x) => x;"
            );
            var expectedTryBlock =
                "var methodResult = RecordMethod_Implementation(x);\n            __wrapper_log_result = methodResult;\n            return methodResult;";
            var expected = CreateExpectedSource("public partial record MyRecord",
                "public partial int RecordMethod(int x)",
                "new object[] { x }", expectedTryBlock,
                methodName: "RecordMethod");
            var generatedSource = GetGeneratedSource(source, "MyRecord.RecordMethod.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(
                NormalizeWhitespace(generatedSource),
                Is.EqualTo(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesMethodInGenericClass()
        {
            var source = CreateSource(
                "public partial class MyGeneric<T>",
                "[Wrapper(typeof(TestWrapper))] public partial T Echo(T value);",
                "private T Echo_Implementation(T value) => value;"
            );
            var expected = "public partial T Echo(T value)";
            var generated = GetGeneratedSource(source, "MyGeneric.Echo.g.cs");
            Assert.That(NormalizeWhitespace(generated).Contains(NormalizeWhitespace(expected)));
            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        public void Generator_HandlesMultipleRefOutInParameters()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial void Process(ref int a, out string b, in Guid c);",
                "private void Process_Implementation(ref int a, out string b, in Guid c) { b = \"ok\"; }"
            );
            var generated = GetGeneratedSource(source, "MyService.Process.g.cs");
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace("Process_Implementation(ref a, out b, in c);")));
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace("TestWrapper.OnEnter(\"Process\", new object[] { a, c })")));
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace("__wrapper_log_result = b;")));
            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        public void Generator_HandlesNoAccessModifierDefaultsToPrivate()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] partial void DoWork();",
                "private void DoWork_Implementation() {}"
            );
            var generated = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace("partial void DoWork()")));
            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        public void Generator_HandlesMethodInNestedType()
        {
            var source =
                "using BuildInfoAnalyzers;\n" +
                TestWrapperCode +
                @"namespace TestNamespace {
    public partial class Outer {
        public partial class Inner {
            [Wrapper(typeof(TestWrapper))] public partial void DoWork();
            public void DoWork_Implementation() {}
        }
    }
}";
            var generated = GetGeneratedSource(source, "Inner.DoWork.g.cs");
            Assert.That(generated.Contains("namespace TestNamespace"));
            Assert.That(NormalizeWhitespace(generated)
                .Contains(NormalizeWhitespace("public partial void DoWork()")));
            AssertNoCompilationErrors(generated, source);
        }

        [Test]
        public void Generator_HandlesMultipleGenericConstraints()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial T Echo<T, U>(T value, U other) where T : class, new() where U : struct;",
                "private T Echo_Implementation<T, U>(T value, U other) where T : class, new() where U : struct => value;",
                "public struct MyStruct {}"
            );
            var expected = "public partial T Echo<T, U>(T value, U other) where T : class, new() where U : struct";
            var generatedSource = GetGeneratedSource(source, "MyService.Echo.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace(expected)));
        }

        [Test]
        public void Generator_HandlesMethodWithOtherAttributes()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Obsolete][Wrapper(typeof(TestWrapper))] public partial void DoWork();",
                "private void DoWork_Implementation() {}"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource)
                .Contains(NormalizeWhitespace("public partial void DoWork()")));
        }

        [Test]
        public void Generator_HandlesMethodInNamespace()
        {
            var source =
                "using BuildInfoAnalyzers;\n" +
                TestWrapperCode +
                @"namespace MyNamespace { public partial class MyService { [Wrapper(typeof(TestWrapper))] public partial void DoWork(); private void DoWork_Implementation() {} } }";
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(generatedSource.Contains("namespace MyNamespace"));
            Assert.That(NormalizeWhitespace(generatedSource)
                .Contains(NormalizeWhitespace("public partial void DoWork()")));
        }


        [Test]
        public void Generator_HandlesMultipleClassesAndMethods()
        {
            var source = CreateMultiSource(
                ("public partial class A", ["[Wrapper(typeof(TestWrapper))] public partial void M1();"],
                    ["private void M1_Implementation() {}"]),
                ("public partial class B", ["[Wrapper(typeof(TestWrapper))] public partial int M2(int x);"],
                    ["private int M2_Implementation(int x) => x;"])
            );
            var generated = GetAllGeneratedSources(source);
            Assert.That(generated.Keys.Any(k => k.Contains("A.M1.g.cs")));
            Assert.That(generated.Keys.Any(k => k.Contains("B.M2.g.cs")));
            Assert.That(NormalizeWhitespace(generated.Values.First(v => v.Contains("M1")))
                .Contains(NormalizeWhitespace("public partial void M1()")));
            Assert.That(NormalizeWhitespace(generated.Values.First(v => v.Contains("M2")))
                .Contains(NormalizeWhitespace("public partial int M2(int x)")));
            AssertNoCompilationErrors(generated.Values.Concat([source]).ToArray());
        }


        [Test]
        public void Generator_HandlesMultipleMethodsInOneClass()
        {
            var source = CreateSource(
                "public partial class MultiMethodClass",
                "[Wrapper(typeof(TestWrapper))] public partial void M1();\n[Wrapper(typeof(TestWrapper))] internal partial int M2(int x);\n[Wrapper(typeof(TestWrapper))] private partial string M3(string s);",
                "private void M1_Implementation() {}\nprivate int M2_Implementation(int x) => x;\nprivate string M3_Implementation(string s) => s;"
            );
            var generated = GetAllGeneratedSources(source);
            Assert.That(generated.Keys.Any(k => k.Contains("M1.g.cs")));
            Assert.That(generated.Keys.Any(k => k.Contains("M2.g.cs")));
            Assert.That(generated.Keys.Any(k => k.Contains("M3.g.cs")));
            Assert.That(NormalizeWhitespace(generated["MultiMethodClass.M1.g.cs"])
                .Contains(NormalizeWhitespace("public partial void M1()")));
            Assert.That(NormalizeWhitespace(generated["MultiMethodClass.M2.g.cs"])
                .Contains(NormalizeWhitespace("internal partial int M2(int x)")));
            Assert.That(NormalizeWhitespace(generated["MultiMethodClass.M3.g.cs"])
                .Contains(NormalizeWhitespace("private partial string M3(string s)")));
            AssertNoCompilationErrors(generated.Values.Concat([source]).ToArray());
        }

        [Test]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial int[] GetIntArray();",
            "private int[] GetIntArray_Implementation() => new int[] { 1, 2, 3 };",
            "public partial int[] GetIntArray()", "GetIntArray.g.cs")]
        [TestCase(
            "[Wrapper(typeof(TestWrapper))] public partial System.Collections.Generic.List<string> GetStringList();",
            "private System.Collections.Generic.List<string> GetStringList_Implementation() => new System.Collections.Generic.List<string> { \"a\", \"b\" };",
            "public partial System.Collections.Generic.List<string> GetStringList()", "GetStringList.g.cs")]
        [TestCase(
            "[Wrapper(typeof(TestWrapper))] public partial System.Collections.Generic.Dictionary<int, string> GetDictionary();",
            "private System.Collections.Generic.Dictionary<int, string> GetDictionary_Implementation() => new System.Collections.Generic.Dictionary<int, string> { { 1, \"one\" } };",
            "public partial System.Collections.Generic.Dictionary<int, string> GetDictionary()", "GetDictionary.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial MyStruct GetStruct();",
            "private MyStruct GetStruct_Implementation() => new MyStruct { X = 42 };",
            "public partial MyStruct GetStruct()", "GetStruct.g.cs")]
        public void Generator_HandlesVariousReturnTypes(string methodDecl, string methodImpl, string expectedSignature,
            string hintName)
        {
            var source = CreateSource(
                [
                    ("public partial class ReturnTypeTest",
                        [methodDecl],
                        [methodImpl]
                    )
                ],
                additionalCode: "public struct MyStruct { public int X; }"
            );
            var generated = GetAllGeneratedSources(source);
            Assert.That(generated.Keys.Any(k => k.Contains(hintName)));
            Assert.That(NormalizeWhitespace(generated[$"ReturnTypeTest.{hintName}"])
                .Contains(NormalizeWhitespace(expectedSignature)));
            AssertNoCompilationErrors(generated[$"ReturnTypeTest.{hintName}"], source);
        }

        [Test]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial int IntMethod(int x);",
            "private int IntMethod_Implementation(int x) => x;", "public partial int IntMethod(int x)",
            "IntMethod.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial string StringMethod(string s);",
            "private string StringMethod_Implementation(string s) => s;",
            "public partial string StringMethod(string s)", "StringMethod.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial double? NullableMethod(double? d);",
            "private double? NullableMethod_Implementation(double? d) => d;",
            "public partial double? NullableMethod(double? d)", "NullableMethod.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial int[] ArrayMethod(int[] arr);",
            "private int[] ArrayMethod_Implementation(int[] arr) => arr;",
            "public partial int[] ArrayMethod(int[] arr)", "ArrayMethod.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial System.Guid StructMethod(System.Guid g);",
            "private System.Guid StructMethod_Implementation(System.Guid g) => g;",
            "public partial System.Guid StructMethod(System.Guid g)", "StructMethod.g.cs")]
        [TestCase("[Wrapper(typeof(TestWrapper))] public partial object RefMethod(object o);",
            "private object RefMethod_Implementation(object o) => o;", "public partial object RefMethod(object o)",
            "RefMethod.g.cs")]
        public void Generator_HandlesVariousArgumentAndReturnTypes(string methodDecl, string methodImpl,
            string expectedSignature, string hintName)
        {
            var source = CreateMultiSource(
                ("public partial class TypeTest",
                    [methodDecl],
                    [methodImpl])
            );
            var generated = GetAllGeneratedSources(source);
            Assert.That(generated.Keys.Any(k => k.Contains(hintName)));
            Assert.That(NormalizeWhitespace(generated[$"TypeTest.{hintName}"])
                .Contains(NormalizeWhitespace(expectedSignature)));
            AssertNoCompilationErrors(generated[$"TypeTest.{hintName}"], source);
        }

        [Test]
        public void Generator_UsesCustomImplementationMethodName()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper), \"CustomImpl\")] public partial void DoWork();",
                "private void CustomImpl() {}"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace("CustomImpl();")));
        }

        [Test]
        public void Generator_UsesCustomImplementationMethodName_GenericMethod()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper), \"CustomImpl\")] public partial T Echo<T>(T value) where T : class;",
                "private T CustomImpl<T>(T value) where T : class => value;"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.Echo.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace("CustomImpl(value);")));
        }

        [Test]
        public void Generator_UsesCustomImplementationMethodName_NestedClass()
        {
            var source = @"
public partial class Outer {
    public partial class Inner {
        [Wrapper(typeof(TestWrapper), ""CustomImpl"")] public partial void DoWork();
        private void CustomImpl() {}
    }
}";
            var fullSource = "using BuildInfoAnalyzers;\n" + TestWrapperCode + source;
            var generatedSource = GetGeneratedSource(fullSource, "Inner.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, fullSource);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace("CustomImpl();")));
        }

        [Test]
        public void Generator_HandlesInternalClassWithCustomImplName()
        {
            var source = CreateSource(
                "internal partial class MyInternalService",
                "[Wrapper(typeof(TestWrapper), \"CustomImpl\")] internal partial void DoWork();",
                "private void CustomImpl() {}"
            );
            var generatedSource = GetGeneratedSource(source, "MyInternalService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace("CustomImpl();")));
        }

        [Test]
        public void Generator_HandlesPrivateNestedClassWithGenericMethod()
        {
            var source = @"
public partial class Outer {
    private partial class Inner {
        [Wrapper(typeof(TestWrapper))] private partial T Echo<T>(T value) where T : new();
        private T Echo_Implementation<T>(T value) where T : new() => value;
    }
}";
            var fullSource = "using BuildInfoAnalyzers;\n" + TestWrapperCode + source;
            var generatedSource = GetGeneratedSource(fullSource, "Inner.Echo.g.cs");
            AssertNoCompilationErrors(generatedSource, fullSource);
            Assert.That(NormalizeWhitespace(generatedSource)
                .Contains(NormalizeWhitespace("private partial T Echo<T>(T value) where T : new()")));
        }

        [Test]
        public void Generator_HandlesAllParameterModifiersWithCustomImplName()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper), \"CustomImpl\")] public partial void Process(ref int a, out string b, in Guid c);",
                "private void CustomImpl(ref int a, out string b, in Guid c) { b = \"ok\"; }"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.Process.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource)
                .Contains(NormalizeWhitespace("CustomImpl(ref a, out b, in c);")));
        }

        [Test]
        public void Generator_HandlesMethodWithMultipleGenericParametersAndConstraints()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial TResult Combine<T1, T2, TResult>(T1 a, T2 b) where T1 : class where T2 : struct where TResult : new();",
                "private TResult Combine_Implementation<T1, T2, TResult>(T1 a, T2 b) where T1 : class where T2 : struct where TResult : new() => new TResult();"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.Combine.g.cs");
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace(
                "public partial TResult Combine<T1, T2, TResult>(T1 a, T2 b) where T1 : class where T2 : struct where TResult : new()")));
            AssertNoCompilationErrors(generatedSource, source);
        }

        [Test]
        public void Generator_HandlesMethodWithOtherAttributesAndCustomImplName()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Obsolete][Wrapper(typeof(TestWrapper), \"CustomImpl\")] public partial void DoWork();",
                "private void CustomImpl() {}"
            );
            var generatedSource = GetGeneratedSource(source, "MyService.DoWork.g.cs");
            AssertNoCompilationErrors(generatedSource, source);
            Assert.That(NormalizeWhitespace(generatedSource).Contains(NormalizeWhitespace("CustomImpl();")));
        }

        [Test]
        public void Generator_ReportsConflictingAccessabilitiesWithCustomImplName()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper), \"CustomImpl\")] public partial void DoWork();",
                "protected partial void CustomImpl() {}"
            );
            RunGeneratorExpectingErrors(source, "CS0759");
        }

        [Test]
        public void Generator_ReportsMissingImplementationForNonVoid()
        {
            var source = CreateSource(
                "public partial class MyService",
                "[Wrapper(typeof(TestWrapper))] public partial int DoWork();",
                "// missing implementation"
            );
            RunGeneratorExpectingErrors(source,
                "CS0103"); // CS0103: The name 'DoWork_Implementation' does not exist in the current context
        }

        private string GetGeneratedSource(string source, string hintName)
        {
            var (_, runResult) = RunGenerator(source);
            var generatedSource = runResult.Results
                .SelectMany(res => res.GeneratedSources)
                .FirstOrDefault(s => s.HintName.EndsWith(hintName));

            return generatedSource.SourceText?.ToString() ?? string.Empty;
        }

        private string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return string.Join(" ",
                text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        }

        // --- Helper Methods ---

        private (Compilation, GeneratorDriverRunResult) RunGenerator(string source)
        {
            var compilation = CSharpCompilation.Create("TestCompilation",
                [CSharpSyntaxTree.ParseText(source)],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new WrapperGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            var runResult = driver.GetRunResult();

            var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.That(errors.Where(d => d.Id != "CS0411" && d.Id != "CS0029"), Is.Empty,
                $"Generated code has compilation errors: {string.Join("\n", errors.Select(e => e.ToString()))}");

            return (outputCompilation, runResult);
        }

        // Flexible source generator for test scenarios.
        private string CreateSource(
            (string classDef, string[] methodDecls, string[] methodImpls)[] classes,
            string[]? usings = null,
            string? namespaceBlock = null,
            string? wrapperClass = null,
            string additionalCode = "")
        {
            var sb = new System.Text.StringBuilder();
            if (usings != null)
            {
                foreach (var u in usings) sb.AppendLine($"using {u};");
            }
            else
            {
                sb.AppendLine(
                    "using System;\nusing System.Threading.Tasks;\nusing System.Collections.Generic;\nusing BuildInfoAnalyzers;\n");
            }

            // Do NOT inject WrapperAttributeCode here!
            sb.AppendLine(wrapperClass ?? TestWrapperCode);
            if (!string.IsNullOrWhiteSpace(additionalCode))
                sb.AppendLine(additionalCode);
            if (!string.IsNullOrWhiteSpace(namespaceBlock))
            {
                sb.AppendLine($"namespace {namespaceBlock} {{");
            }

            foreach (var (classDef, methodDecls, methodImpls) in classes)
            {
                sb.AppendLine(classDef);
                sb.AppendLine("{");
                foreach (var decl in methodDecls) sb.AppendLine(decl);
                foreach (var impl in methodImpls) sb.AppendLine(impl);
                sb.AppendLine("}");
            }

            if (!string.IsNullOrWhiteSpace(namespaceBlock))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        // Backward compatible overload for simple cases
        private string CreateSource(string classImplementation, string methodDeclaration, string methodImplementation,
            string additionalCode = "")
        {
            return CreateSource(
                [(classImplementation, [methodDeclaration], [methodImplementation])],
                null!, null, null, additionalCode);
        }

        // New helper: allows multiple classes/methods in one test
        private string CreateMultiSource(params (string classDef, string[] methodDecls, string[] methodImpls)[] classes)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(
                @"using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using BuildInfoAnalyzers;

public static class TestWrapper
{
    public static void OnEnter(string method, object[] args) {}
    public static void OnExit(string method, object? result, long ms) {}
    public static void OnError(string method, System.Exception ex, long ms) {}
}
");
            foreach (var (classDef, methodDecls, methodImpls) in classes)
            {
                sb.AppendLine(classDef);
                sb.AppendLine("{");
                foreach (var decl in methodDecls) sb.AppendLine(decl);
                foreach (var impl in methodImpls) sb.AppendLine(impl);
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        // New helper: get all generated sources for a test
        private Dictionary<string, string> GetAllGeneratedSources(string source)
        {
            var runResult = RunGenerator(source).Item2;
            return runResult.Results
                .SelectMany(res => res.GeneratedSources)
                .ToDictionary(s => s.HintName, s => s.SourceText.ToString());
        }

        private string CreateExpectedSource(string classDef, string methodSignature, string onEnterArgs,
            string tryBlock, string? ns = null, string methodName = "DoWork")
        {
            // Determine if System.Threading.Tasks is needed
            bool needsTasks = methodSignature.Contains("Task") || tryBlock.Contains("Task") ||
                              methodSignature.Contains("async");
            var usings = "using System;\n" + (needsTasks ? "using System.Threading.Tasks;\n" : "") +
                         "using System.Diagnostics;\n";
            var body =
                $"// <auto-generated/>\n#nullable enable\n{usings}\n{classDef}\n{{\n    {methodSignature}\n    {{\n        TestWrapper.OnEnter(\"{methodName}\", {onEnterArgs});\n        var stopwatch = System.Diagnostics.Stopwatch.StartNew();\n        object? __wrapper_log_result = null;\n        try\n        {{\n            {tryBlock}\n        }}\n        catch (System.Exception ex)\n        {{\n            stopwatch.Stop();\n            TestWrapper.OnError(\"{methodName}\", ex, stopwatch.ElapsedMilliseconds);\n            throw;\n        }}\n        finally\n        {{\n            stopwatch.Stop();\n            TestWrapper.OnExit(\"{methodName}\", __wrapper_log_result, stopwatch.ElapsedMilliseconds);\n        }}\n    }}\n}}";
            if (ns != null)
            {
                return $"namespace {ns}\n{{\n{body}\n}}";
            }

            return body;
        }

        private void RunGeneratorExpectingErrors(string source, params string[] expectedErrorCodes)
        {
            var compilation = CSharpCompilation.Create("TestCompilation",
                [CSharpSyntaxTree.ParseText(source)],
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new WrapperGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.That(errors, Is.Not.Empty, "Expected errors but found none.");
            foreach (var code in expectedErrorCodes)
            {
                Assert.That(errors.Any(e => e.Id == code),
                    $"Expected error code {code} but not found. Errors: {string.Join(", ", errors.Select(e => e.ToString()))}");
            }
        }

        // Helper: Asserts that the generated code compiles without errors
        private void AssertNoCompilationErrors(params string[] sources)
        {
            bool hasWrapperAttribute = sources.Any(s => s.Contains("class WrapperAttribute"));
            var allSources = hasWrapperAttribute
                ? sources
                : new[] { WrapperGenerator.WrapperAttributeText }.Concat(sources).ToArray();

            var syntaxTrees = allSources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();
            var generatorAssembly = typeof(WrapperGenerator).Assembly.Location;
            var compilation = CSharpCompilation.Create("GeneratedTest",
                syntaxTrees,
                [
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                    MetadataReference.CreateFromFile(generatorAssembly)
                ],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var errors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Where(d => d.Id != "CS0411" && d.Id != "CS0029")
                .ToList();
            Assert.That(errors, Is.Empty,
                $"Generated code has compilation errors: {string.Join("\n", errors.Select(e => e.ToString()))}");
        }
    }
}