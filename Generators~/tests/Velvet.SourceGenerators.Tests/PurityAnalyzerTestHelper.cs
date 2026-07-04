using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Velvet.SourceGenerators.PurityAnalysis;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Test harness that invokes <see cref="PurityAnalyzer"/> directly against a Compilation.
    /// Reuses the same reference resolution and Velvet stub as <see cref="GeneratorTestHelper"/>.
    /// </summary>
    internal static class PurityAnalyzerTestHelper
    {
        private const string PureAttributeStubSource = @"
namespace System.Diagnostics.Contracts
{
    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public sealed class PureAttribute : global::System.Attribute { }
}
namespace JetBrains.Annotations
{
    [global::System.AttributeUsage(global::System.AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    public sealed class PureAttribute : global::System.Attribute { }
}
";

        public static PurityResult AnalyzeMethod(string source, string methodName, CancellationToken ct = default)
        {
            var compilation = BuildCompilation(source);
            var method = FindMethod(compilation, methodName);
            return PurityAnalyzer.Analyze(method, compilation, ct);
        }

        public static CSharpCompilation BuildCompilation(string source)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var trees = new[]
            {
                CSharpSyntaxTree.ParseText(source, parseOptions),
                CSharpSyntaxTree.ParseText(GeneratorTestHelper.VelvetStubSource, parseOptions),
                CSharpSyntaxTree.ParseText(PureAttributeStubSource, parseOptions),
            };

            return CSharpCompilation.Create(
                assemblyName: "PurityTestAssembly",
                syntaxTrees: trees,
                references: ReferenceAssemblies(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        public static IMethodSymbol FindMethod(Compilation compilation, string methodName)
        {
            // Accepts both "ClassName.MethodName" (type-qualified) and bare method-name forms.
            string? typeFilter = null;
            var simpleName = methodName;
            var dotIndex = methodName.LastIndexOf('.');
            if (dotIndex > 0)
            {
                typeFilter = methodName.Substring(0, dotIndex);
                simpleName = methodName.Substring(dotIndex + 1);
            }

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                foreach (var decl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>())
                {
                    if (decl.Identifier.Text != simpleName)
                    {
                        continue;
                    }
                    if (typeFilter is not null &&
                        decl.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax type &&
                        type.Identifier.Text != typeFilter)
                    {
                        continue;
                    }
                    if (model.GetDeclaredSymbol(decl) is IMethodSymbol symbol)
                    {
                        return symbol;
                    }
                }
            }

            throw new System.InvalidOperationException($"Method '{methodName}' not found in test source.");
        }

        private static IReadOnlyList<MetadataReference> ReferenceAssemblies()
        {
            var list = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.StringBuilder).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
            };
            var trustedAssemblies = (string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (trustedAssemblies is not null)
            {
                foreach (var path in trustedAssemblies.Split(System.IO.Path.PathSeparator))
                {
                    if (path.EndsWith("netstandard.dll") || path.EndsWith("System.Runtime.dll"))
                    {
                        list.Add(MetadataReference.CreateFromFile(path));
                    }
                }
            }
            return list;
        }
    }
}
