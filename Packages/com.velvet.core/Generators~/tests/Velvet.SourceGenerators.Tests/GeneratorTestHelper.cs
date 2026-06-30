using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Helper for retrieving MemoizeMethodGenerator execution results.
    /// Since the Velvet Runtime cannot build to netstandard2.0, the test input embeds a minimal stub
    /// of the required Velvet types to drive the generator.
    /// </summary>
    internal static class GeneratorTestHelper
    {
        internal const string VelvetStubSource = @"
namespace Velvet
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class MemoizeAttribute : global::System.Attribute { }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method | global::System.AttributeTargets.Constructor, Inherited = false)]
    public sealed class PureAttribute : global::System.Attribute { }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class ComponentAttribute : global::System.Attribute
    {
        public bool IsErrorBoundary { get; set; } = false;
        public bool Memoize { get; set; } = false;
        public string DisplayName { get; set; }
    }

    public class VNode
    {
        [global::Velvet.PureAttribute] public VNode() { }
    }

    public sealed class MutableRef<T>
    {
        public MutableRef(T initial) { Current = initial; }
        public T Current { get; set; }
    }
    public sealed class MemoNode : VNode
    {
        public string Key;
        public global::System.Func<VNode> Factory;
        public object[] Dependencies;
    }

    public static partial class V
    {
        public static MemoNode Memoized(global::System.Func<VNode> factory, params object[] deps) =>
            new MemoNode { Factory = factory, Dependencies = deps };
        public static MemoNode MemoizedWithKey(string key, global::System.Func<VNode> factory, params object[] deps) =>
            new MemoNode { Key = key, Factory = factory, Dependencies = deps };
    }

    public static class ComponentMethodRegistry
    {
        public static void RegisterErrorBoundary(string declaringTypeFullName, string methodName) { }
        public static void RegisterMemoize(string declaringTypeFullName, string methodName) { }
        public static void RegisterComponentDisplayName(string declaringTypeFullName, string methodName, string displayName) { }
    }

    public static class Hooks
    {
        // Memoization API stub invoked by the [Component(Memoize=true)] generator. No-op is fine
        // (these tests only assert on generated source text; runtime behavior is verified on the Unity side via RunEditModeTests).
        public static bool TryGetMemoizedVNode(object[] deps, out int slotIndex, out VNode cached)
        {
            slotIndex = 0;
            cached = null;
            return false;
        }
        public static void StoreMemoizedVNode(int slotIndex, object[] deps, VNode result) { }

        // Static API stubs for the positional hooks a functional-component test fixture may call.
        // params on the deps argument mirrors the runtime Hooks signatures so loose-arg deps typecheck.
        public static void UseEffect(global::System.Func<global::System.Action> factory, object[] deps) { }
        public static void UseLayoutEffect(global::System.Func<global::System.Action> factory, object[] deps) { }
        public static T UseCallback<T>(T callback, params object[] deps) where T : class => callback;
        public static T UseMemo<T>(global::System.Func<T> factory, params object[] deps) => factory();
        public static T UseBlocker<T>(T initial) => initial;
        public static (T value, global::System.Action<T> setValue) UseState<T>(T initial) =>
            (initial, _ => { });
        public static (TState state, global::System.Action<TAction> dispatch) UseReducer<TState, TAction>(global::System.Func<TState, TAction, TState> reducer, TState initial) =>
            (initial, _ => { });
        public static T UseContext<T>(object contextRef) where T : class => null;
        public static (bool isPending, global::System.Action<global::System.Action> startTransition) UseTransition() =>
            (false, _ => { });
        public static T UseRef<T>(T initial) where T : class => initial;
        public static global::Velvet.MutableRef<T> UseMutableRef<T>(T initial) =>
            new global::Velvet.MutableRef<T>(initial);
        public static void UseImperativeHandle<T>(object refTarget, global::System.Func<T> factory, params object[] deps) { }
    }
}
";

        public static GeneratorRunResult Run(string userSource) =>
            Run(userSource, new Microsoft.CodeAnalysis.IIncrementalGenerator[] { new MemoizeMethodGenerator() });

        public static GeneratorRunResult Run(string userSource, Microsoft.CodeAnalysis.IIncrementalGenerator[] generators)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(userSource, parseOptions),
                CSharpSyntaxTree.ParseText(VelvetStubSource, parseOptions),
            };

            var references = ReferenceAssemblies();

            var compilation = CSharpCompilation.Create(
                assemblyName: "TestAssembly",
                syntaxTrees: syntaxTrees,
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators.Select(g => g.AsSourceGenerator()).ToArray());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var _);

            var runResult = driver.GetRunResult();

            var compilationDiagnostics = updatedCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();

            return new GeneratorRunResult(
                GeneratedSources: runResult.Results
                    .SelectMany(r => r.GeneratedSources)
                    .Select(s => new GeneratedSource(s.HintName, s.SourceText.ToString()))
                    .ToImmutableArray(),
                Diagnostics: runResult.Results
                    .SelectMany(r => r.Diagnostics)
                    .ToImmutableArray(),
                CompilationErrors: compilationDiagnostics);
        }

        public static ImmutableArray<Diagnostic> RunAnalyzer(string userSource, DiagnosticAnalyzer analyzer)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(userSource, parseOptions),
                CSharpSyntaxTree.ParseText(VelvetStubSource, parseOptions),
            };

            var compilation = CSharpCompilation.Create(
                assemblyName: "AnalyzerTestAssembly",
                syntaxTrees: syntaxTrees,
                references: ReferenceAssemblies(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
            return withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public static IReadOnlyList<MetadataReference> ReferenceAssemblies()
        {
            var list = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
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

    internal sealed record GeneratorRunResult(
        ImmutableArray<GeneratedSource> GeneratedSources,
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<Diagnostic> CompilationErrors);

    internal sealed record GeneratedSource(string HintName, string Source);
}
