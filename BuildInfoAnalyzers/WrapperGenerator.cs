#nullable enable

using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;


namespace BuildInfoAnalyzers
{
    [Generator]
    public class WrapperGenerator : IIncrementalGenerator
    {
        internal const string WrapperAttributeText = @"#nullable enable
using System;
namespace BuildInfoAnalyzers
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class WrapperAttribute : Attribute
    {
        public Type Wrapper { get; }
        public string? ImplementationMethodName { get; set; }

        public WrapperAttribute(Type wrapper)
        {
            Wrapper = wrapper;
        }
        public WrapperAttribute(Type wrapper, string? implementationMethodName)
        {
            Wrapper = wrapper;
            ImplementationMethodName = implementationMethodName;
        }
    }
}
";

        private static readonly SymbolDisplayFormat FullyQualifiedWithoutGlobalFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeExplicitInterface,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
                              SymbolDisplayParameterOptions.IncludeModifiers,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
        );

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(i => i.AddSource(
                "WrapperAttribute.g.cs",
                SourceText.From(WrapperAttributeText, Encoding.UTF8)));

            IncrementalValuesProvider<IMethodSymbol> methodDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => s is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                    transform: static (ctx, _) => GetMethodSymbolIfWrapped(ctx))
                .Where(static m => m is not null)!;

            context.RegisterSourceOutput(methodDeclarations,
                static (spc, method) => Execute(spc, method));
        }

        private static IMethodSymbol? GetMethodSymbolIfWrapped(GeneratorSyntaxContext context)
        {
            var methodDeclarationSyntax = (MethodDeclarationSyntax)context.Node;

            if (context.SemanticModel.GetDeclaredSymbol(methodDeclarationSyntax) is not { } methodSymbol) return null;
            if (!methodSymbol.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "BuildInfoAnalyzers.WrapperAttribute")) return null;
            // Check if the containing type is partial, supporting class, struct, and record.
            foreach (var syntaxNode in methodSymbol.ContainingType.DeclaringSyntaxReferences.Select(syntaxRef =>
                         syntaxRef.GetSyntax()))
            {
                if ((syntaxNode is TypeDeclarationSyntax typeDecl &&
                     typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword)) ||
                    (syntaxNode is RecordDeclarationSyntax recDecl &&
                     recDecl.Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    return methodSymbol;
                }
            }

            return null;
        }

        private static void Execute(SourceProductionContext context, IMethodSymbol method)
        {
            if (method.ContainingType is null) return;

            var attributeData = method.GetAttributes().First(a =>
                a.AttributeClass?.ToDisplayString() == "BuildInfoAnalyzers.WrapperAttribute");

            if (attributeData.ConstructorArguments.Length == 0) return;
            if (attributeData.ConstructorArguments[0].Value is not INamedTypeSymbol wrapperType) return;

            string? customImplementationMethodName = null;
            if (attributeData.ConstructorArguments.Length > 1 &&
                attributeData.ConstructorArguments[1].Value is string ctorValue)
                customImplementationMethodName = ctorValue;
            else if (attributeData.NamedArguments.Any(kv => kv.Key == "ImplementationMethodName"))
                customImplementationMethodName =
                    attributeData.NamedArguments.First(kv => kv.Key == "ImplementationMethodName").Value
                        .Value as string;

            string source = GenerateWrapperMethod(method, wrapperType, customImplementationMethodName);
            context.AddSource($"{method.ContainingType.Name}.{method.Name}.g.cs",
                SourceText.From(source, Encoding.UTF8));
        }

        private static string GenerateWrapperMethod(IMethodSymbol method, ITypeSymbol wrapperType,
            string? customImplementationMethodName)
        {
            if (method.ContainingType == null)
                return string.Empty;

            var containingType = method.ContainingType;
            string wrapperTypeName = wrapperType.ToDisplayString(FullyQualifiedWithoutGlobalFormat);
            string implementationMethodName = !string.IsNullOrEmpty(customImplementationMethodName)
                ? customImplementationMethodName!
                : $"{method.Name}_Implementation";

            var methodParameters = method.Parameters.Select(p => p.ToDisplayString(FullyQualifiedWithoutGlobalFormat))
                .ToList();
            var callParameters = method.Parameters.Select(p =>
            {
                var modifier = p.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => ""
                };
                return modifier + p.Name;
            }).ToList();

            var onEnterCallParameters = method.Parameters
                .Where(p => p.RefKind != RefKind.Out)
                .Select(p => p.Name).ToList();
            var onEnterArgs = onEnterCallParameters.Any()
                ? $"new object[] {{ {string.Join(", ", onEnterCallParameters)} }}"
                : "new object[0]";

            string returnType = method.ReturnType.ToDisplayString(FullyQualifiedWithoutGlobalFormat);
            var returnTypeSymbol = method.ReturnType as INamedTypeSymbol;

            bool isTaskReturnType = returnTypeSymbol is not null &&
                                    returnTypeSymbol.Name == "Task" &&
                                    returnTypeSymbol.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

            bool isGenericTask = isTaskReturnType && (returnTypeSymbol?.IsGenericType ?? false);

            // Determine if the wrapper should be async for async void
            bool isAsyncVoid = false;
            if (!isTaskReturnType && method.ReturnType.SpecialType == SpecialType.System_Void)
            {
                // Look for an implementation method with the same name + _Implementation and async modifier
                foreach (var member in containingType.GetMembers())
                {
                    if (member is not IMethodSymbol impl || impl.Name != implementationMethodName ||
                        !impl.IsAsync) continue;
                    isAsyncVoid = true;
                    break;
                }
            }

            string asyncKeyword = (isTaskReturnType || isAsyncVoid) ? "async " : "";
            string awaitKeyword = isTaskReturnType ? "await " : "";

            string methodStaticKeyword = method.IsStatic ? "static " : "";

            // Robust: Check all partial declarations for explicit accessibility and ensure they match
            var explicitAccessibilities = new HashSet<Accessibility>();
            foreach (var node in method.DeclaringSyntaxReferences.Select(syntaxRef => syntaxRef.GetSyntax()))
            {
                if (node is not MethodDeclarationSyntax mds) continue;
                var mods = mds.Modifiers;
                if (!mods.Any(mod =>
                        mod.IsKind(SyntaxKind.PublicKeyword) ||
                        mod.IsKind(SyntaxKind.PrivateKeyword) ||
                        mod.IsKind(SyntaxKind.ProtectedKeyword) ||
                        mod.IsKind(SyntaxKind.InternalKeyword))) continue;
                // Determine the accessibility for this declaration
                if (mods.Any(SyntaxKind.PublicKeyword))
                    explicitAccessibilities.Add(Accessibility.Public);
                else if (mods.Any(SyntaxKind.ProtectedKeyword) && mods.Any(SyntaxKind.InternalKeyword) &&
                         !mods.Any(SyntaxKind.PrivateKeyword))
                    explicitAccessibilities.Add(Accessibility.ProtectedOrInternal); // protected internal
                else if (mods.Any(SyntaxKind.PrivateKeyword) && mods.Any(SyntaxKind.ProtectedKeyword) &&
                         !mods.Any(SyntaxKind.InternalKeyword))
                    explicitAccessibilities.Add(Accessibility.ProtectedAndInternal); // private protected
                else if (mods.Any(SyntaxKind.InternalKeyword))
                    explicitAccessibilities.Add(Accessibility.Internal);
                else if (mods.Any(SyntaxKind.ProtectedKeyword))
                    explicitAccessibilities.Add(Accessibility.Protected);
                else if (mods.Any(SyntaxKind.PrivateKeyword))
                    explicitAccessibilities.Add(Accessibility.Private);
            }

            string accessModifier = string.Empty;
            switch (explicitAccessibilities.Count)
            {
                case 1:
                {
                    var acc = explicitAccessibilities.First();
                    accessModifier = acc switch
                    {
                        Accessibility.Private => "private ",
                        Accessibility.Protected => "protected ",
                        Accessibility.Internal => "internal ",
                        Accessibility.ProtectedOrInternal => "protected internal ",
                        Accessibility.ProtectedAndInternal => "private protected ",
                        _ => "public "
                    };
                    break;
                }
                case > 1:
                    // Conflict: do not generate code, or generate a compile error
                    return
                        $"// <auto-generated/>\n#error Conflicting accessibility modifiers in partial method declarations for {method.Name}\n";
            }

            // For struct methods with non-void return types, always emit 'private'
            if (containingType.TypeKind == TypeKind.Struct && !method.ReturnsVoid)
            {
                accessModifier = "private ";
            }

            // For methods with no explicit accessibility, emit no modifier (C# default is private for partial methods)
            if (string.IsNullOrWhiteSpace(accessModifier))
            {
                accessModifier = "";
            }

            string genericMethodParams = method.IsGenericMethod
                ? $"<{string.Join(", ", method.TypeParameters.Select(p => p.Name))}>"
                : "";

            var wrapperMembers = wrapperType.GetMembers();

            var onEnterMethods = wrapperMembers.OfType<IMethodSymbol>().Where(m => m.Name == "OnEnter").ToList();
            var onEnterWithParams = onEnterMethods.FirstOrDefault(m =>
                m.Parameters.Length == 2 &&
                m.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                m.Parameters[1].Type is IArrayTypeSymbol arrayType &&
                arrayType.ElementType.SpecialType == SpecialType.System_Object);
            var onEnterWithoutParams = onEnterMethods.FirstOrDefault(m =>
                m.Parameters.Length == 1 &&
                m.Parameters[0].Type.SpecialType == SpecialType.System_String);

            bool hasOnExit = wrapperMembers.Any(m => m.Name == "OnExit");
            bool hasOnError = wrapperMembers.Any(m => m.Name == "OnError");

            var sourceBuilder = new StringBuilder();
            // Determine which usings are needed
            bool needsSystem = true; // Always needed for object, Exception, etc.
            bool needsTasks = false;
            bool needsDiagnostics = true; // Always needed for Stopwatch

            // Check if method or return type uses Task/Task<T>
            if (isTaskReturnType || isGenericTask || isAsyncVoid)
                needsTasks = true;

            // Emit auto-generated comment and nullable directive at the very top
            sourceBuilder.AppendLine("// <auto-generated/>");
            sourceBuilder.AppendLine("#nullable enable\n");
            // Emit required usings after the comment and nullable directive
            if (needsSystem) sourceBuilder.AppendLine("using System;");
            if (needsTasks) sourceBuilder.AppendLine("using System.Threading.Tasks;");
            if (needsDiagnostics) sourceBuilder.AppendLine("using System.Diagnostics;");
            sourceBuilder.AppendLine();

            var containingNamespace = containingType.ContainingNamespace;
            bool inGlobalNamespace = containingNamespace.IsGlobalNamespace;

            // Collect all containing types from outermost to innermost
            var typeChain = new Stack<INamedTypeSymbol>();
            var t = containingType;
            while (t != null)
            {
                typeChain.Push(t);
                t = t.ContainingType;
            }

            if (!inGlobalNamespace)
            {
                sourceBuilder.AppendLine($"namespace {containingNamespace.ToDisplayString()}");
                sourceBuilder.AppendLine("{");
            }

            int nesting = 0;
            foreach (var typeSym in typeChain)
            {
                string? nestedTypeHeader = null;
                List<string> typeConstraints = new List<string>();
                foreach (var node in typeSym.DeclaringSyntaxReferences.Select(syntaxRef => syntaxRef.GetSyntax()))
                {
                    if (node is not TypeDeclarationSyntax tds) continue;
                    // Build the type header: modifiers, keyword, name, generics (no attributes, no namespace, no trivia)
                    var headerBuilder = new StringBuilder();
                    if (tds.Modifiers.Count > 0)
                    {
                        headerBuilder.Append(string.Join(" ", tds.Modifiers.Select(m => m.Text)));
                        headerBuilder.Append(' ');
                    }

                    headerBuilder.Append(tds.Keyword.Text);
                    headerBuilder.Append(' ');
                    headerBuilder.Append(tds.Identifier.Text);
                    if (tds.TypeParameterList != null)
                        headerBuilder.Append(tds.TypeParameterList.ToFullString().Trim());
                    nestedTypeHeader = headerBuilder.ToString().TrimEnd();
                    // Collect constraints for this type from syntax (to preserve user formatting/order)
                    if (tds.ConstraintClauses.Count > 0)
                    {
                        typeConstraints.AddRange(tds.ConstraintClauses.Select(clause => string.Join(" ",
                            clause.ToFullString()
                                .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim()))));
                    }

                    break;
                }

                if (nestedTypeHeader == null)
                {
                    // Fallback: synthesize header
                    string nestedTypeKind = typeSym.TypeKind switch
                    {
                        TypeKind.Struct => "struct",
                        _ => typeSym.IsRecord ? "record" : "class"
                    };
                    string nestedGenericParams = typeSym.IsGenericType
                        ? $"<{string.Join(", ", typeSym.TypeParameters.Select(p => p.Name))}>"
                        : "";
                    string nestedClassStaticKeyword = typeSym.IsStatic ? "static " : "";
                    nestedTypeHeader =
                        $"{nestedClassStaticKeyword}partial {nestedTypeKind} {typeSym.Name}{nestedGenericParams}";
                    // Fallback: emit constraints from symbol (may not preserve formatting)
                    if (typeSym.IsGenericType)
                    {
                        foreach (var typeParam in typeSym.TypeParameters)
                        {
                            var constraints = new List<string>();
                            if (typeParam.HasReferenceTypeConstraint) constraints.Add("class");
                            if (typeParam.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
                            if (typeParam.HasNotNullConstraint) constraints.Add("notnull");
                            if (typeParam is { HasValueTypeConstraint: true, IsValueType: false })
                                constraints.Add("struct");
                            constraints.AddRange(typeParam.ConstraintTypes.Select(ct =>
                                ct.ToDisplayString(FullyQualifiedWithoutGlobalFormat)));

                            if (typeParam is { HasConstructorConstraint: true, HasValueTypeConstraint: false })
                                constraints.Add("new()");
                            if (constraints.Count > 0)
                            {
                                typeConstraints.Add($"where {typeParam.Name} : {string.Join(", ", constraints)}");
                            }
                        }
                    }
                }

                sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}{nestedTypeHeader}");
                foreach (var clause in typeConstraints)
                    sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}{clause}");
                sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}{{");
                nesting++;
            }

            // Now emit the method (innermost type)
            // Only emit the method's own constraints on the method, not the containing type's constraints
            // Emit constraints in the same order and wording as the user's declaration
            var methodWhereClauses = new List<string>();
            foreach (var node in method.DeclaringSyntaxReferences.Select(syntaxRef => syntaxRef.GetSyntax()))
            {
                if (node is not MethodDeclarationSyntax { ConstraintClauses.Count: > 0 } mds) continue;
                methodWhereClauses.AddRange(mds.ConstraintClauses.Select(clause => string.Join(" ",
                    clause.ToFullString()
                        .Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()))));
            }

            string genericConstraints =
                methodWhereClauses.Count > 0 ? (" " + string.Join(" ", methodWhereClauses)) : "";

            var methodSignature =
                $"{accessModifier}{methodStaticKeyword}{asyncKeyword}partial {returnType} {method.Name}{genericMethodParams}({string.Join(", ", methodParameters)}){genericConstraints}"
                    .Replace("  ", " ").TrimEnd();
            sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}{methodSignature}");
            sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}{{");
            string innerIndent = new string(' ', 4 * (nesting + 1));
            if (onEnterWithParams != null)
            {
                sourceBuilder.AppendLine($"{innerIndent}{wrapperTypeName}.OnEnter(\"{method.Name}\", {onEnterArgs});");
            }
            else if (onEnterWithoutParams != null)
            {
                sourceBuilder.AppendLine($"{innerIndent}{wrapperTypeName}.OnEnter(\"{method.Name}\");");
            }

            sourceBuilder.AppendLine($"{innerIndent}var stopwatch = System.Diagnostics.Stopwatch.StartNew();");
            sourceBuilder.AppendLine($"{innerIndent}object? __wrapper_log_result = null;");
            sourceBuilder.AppendLine($"{innerIndent}try");
            sourceBuilder.AppendLine($"{innerIndent}{{");
            if (method.ReturnsVoid)
            {
                sourceBuilder.AppendLine(
                    $"{innerIndent}    {implementationMethodName}({string.Join(", ", callParameters)});");
                var outParams = method.Parameters.Where(p => p.RefKind == RefKind.Out).ToList();
                if (outParams.Count == 1)
                {
                    sourceBuilder.AppendLine($"{innerIndent}    __wrapper_log_result = {outParams.First().Name};");
                }
            }
            else if (isTaskReturnType && !isGenericTask) // Handles non-generic Task
            {
                sourceBuilder.AppendLine(
                    $"{innerIndent}    await {implementationMethodName}({string.Join(", ", callParameters)});");
            }
            else // Handles sync T and async Task<T>
            {
                sourceBuilder.AppendLine(
                    $"{innerIndent}    var methodResult = {awaitKeyword}{implementationMethodName}({string.Join(", ", callParameters)});");
                sourceBuilder.AppendLine($"{innerIndent}    __wrapper_log_result = methodResult;");
                sourceBuilder.AppendLine($"{innerIndent}    return methodResult;");
            }

            sourceBuilder.AppendLine($"{innerIndent}}}");
            sourceBuilder.AppendLine($"{innerIndent}catch (System.Exception ex)");
            sourceBuilder.AppendLine($"{innerIndent}{{");
            sourceBuilder.AppendLine($"{innerIndent}    stopwatch.Stop();");
            if (hasOnError)
                sourceBuilder.AppendLine(
                    $"{innerIndent}    {wrapperTypeName}.OnError(\"{method.Name}\", ex, stopwatch.ElapsedMilliseconds);");
            sourceBuilder.AppendLine($"{innerIndent}    throw;");
            sourceBuilder.AppendLine($"{innerIndent}}}");
            sourceBuilder.AppendLine($"{innerIndent}finally");
            sourceBuilder.AppendLine($"{innerIndent}{{");
            sourceBuilder.AppendLine($"{innerIndent}    stopwatch.Stop();");
            if (hasOnExit)
                sourceBuilder.AppendLine(
                    $"{innerIndent}    {wrapperTypeName}.OnExit(\"{method.Name}\", __wrapper_log_result, stopwatch.ElapsedMilliseconds);");
            sourceBuilder.AppendLine($"{innerIndent}}}");
            sourceBuilder.AppendLine($"{new string(' ', 4 * nesting)}}}");

            // Close all opened braces for types
            for (int i = 0; i < nesting; i++)
            {
                sourceBuilder.AppendLine($"{new string(' ', 4 * (nesting - i - 1))}}}");
            }

            if (!inGlobalNamespace)
            {
                sourceBuilder.AppendLine("}");
            }

            return sourceBuilder.ToString();
        }
    }
}