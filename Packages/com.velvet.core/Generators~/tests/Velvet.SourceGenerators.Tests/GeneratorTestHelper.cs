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
    /// Since the Velvet Runtime cannot build to netstandard2.0, the test input embeds a minimal stub
    /// of the required Velvet types to drive the generator. Helper for retrieving MemoizeMethodGenerator
    /// execution results.
    /// </summary>
    /// <remarks>
    /// The stub is a subset, never a variant: it may leave a runtime type or member out, but whatever it does
    /// declare carries the runtime's exact signature, so a diagnostic verified here is verified for a call
    /// shape a user can actually write. <see cref="StubSurfaceDriftTests"/> re-derives the runtime signatures
    /// from source and fails on any divergence this file introduces, including a runtime overload the stub
    /// declares the name of but not the shape of.
    /// </remarks>
    internal static class GeneratorTestHelper
    {
        internal const string VelvetStubSource = @"
namespace Velvet
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class MemoizeMethodAttribute : global::System.Attribute { }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class PureAttribute : global::System.Attribute { }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class ComponentAttribute : global::System.Attribute
    {
        public bool IsErrorBoundary { get; init; } = false;
        public bool Memoize { get; init; } = false;
        public bool Compiler { get; init; } = true;
        public string DisplayName { get; init; }
    }

    public class VNode
    {
        public string Key { get; internal set; }
    }

    public sealed class MemoNode : VNode
    {
        public required global::System.Func<VNode> Factory { get; init; }
        public object[] Dependencies { get; init; }
    }

    public sealed class MutableRef<T>
    {
        public MutableRef(T initial) { Current = initial; }
        public T Current { get; set; }
    }

    public sealed class Ref<T> where T : class { }

    public sealed class ComponentContext<T> { }

    public readonly struct StateUpdater<T>
    {
        public void Invoke(T next) { }
        public void Invoke(global::System.Func<T, T> updater) { }
    }

    public readonly struct TransitionStarter { }

    public sealed class NavigationAttempt { }
    public sealed class RouteBlockerState { }

    public readonly struct VelvetTask<T> { }
    public readonly struct VelvetTask { }

    public static partial class V
    {
        public static MemoNode Memoized(global::System.Func<VNode> factory) =>
            new MemoNode { Factory = factory, Dependencies = null };
        public static MemoNode Memoized(global::System.Func<VNode> factory, params object[] deps) =>
            new MemoNode { Factory = factory, Dependencies = deps };
        public static MemoNode MemoizedWithKey(string key, global::System.Func<VNode> factory) =>
            new MemoNode { Key = key, Factory = factory, Dependencies = null };
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

        // Static API stubs for the positional hooks a functional-component test fixture may call. Every
        // overload the runtime declares of a name appearing here is modelled, so a fixture cannot pick a
        // shape that only exists because the narrower overloads are missing.
        public static void UseEffect(global::System.Func<global::System.Action> factory, object[] deps = null) { }
        public static void UseEffect(global::System.Func<global::System.IDisposable> factory, object[] deps = null) { }
        public static void UseLayoutEffect(global::System.Func<global::System.Action> factory, object[] deps = null) { }
        public static void UseLayoutEffect(global::System.Func<global::System.IDisposable> factory, object[] deps = null) { }
        public static void UseInsertionEffect(global::System.Func<global::System.Action> factory, object[] deps = null) { }
        public static void UseInsertionEffect(global::System.Func<global::System.IDisposable> factory, object[] deps = null) { }
        public static T UseCallback<T>(T callback) where T : global::System.Delegate => callback;
        public static T UseCallback<T>(T callback, params object[] deps) where T : global::System.Delegate => callback;
        public static T UseMemo<T>(global::System.Func<T> factory) => factory();
        public static T UseMemo<T>(global::System.Func<T> factory, params object[] deps) => factory();
        public static global::Velvet.RouteBlockerState UseBlocker(
            global::System.Func<global::Velvet.NavigationAttempt, bool> shouldBlock) => null;
        public static global::Velvet.RouteBlockerState UseBlocker(
            global::System.Func<global::Velvet.NavigationAttempt, bool> shouldBlock, params object[] deps) => null;
        public static global::Velvet.RouteBlockerState UseBlocker(
            global::System.Func<global::Velvet.NavigationAttempt, global::System.Threading.CancellationToken,
                global::Velvet.VelvetTask<bool>> shouldBlock) => null;
        public static global::Velvet.RouteBlockerState UseBlocker(
            global::System.Func<global::Velvet.NavigationAttempt, global::System.Threading.CancellationToken,
                global::Velvet.VelvetTask<bool>> shouldBlock, params object[] deps) => null;
        public static (T value, global::Velvet.StateUpdater<T> setValue) UseState<T>(T initial) =>
            (initial, default);
        public static (T value, global::Velvet.StateUpdater<T> setValue) UseState<T>(global::System.Func<T> initialFactory) =>
            (default, default);
        public static (TState state, global::System.Action<TAction> dispatch) UseReducer<TState, TAction>(
            global::System.Func<TState, TAction, TState> reducer, TState initial) =>
            (initial, _ => { });
        public static (TState state, global::System.Action<TAction> dispatch) UseReducer<TArg, TState, TAction>(
            global::System.Func<TState, TAction, TState> reducer, TArg initialArg, global::System.Func<TArg, TState> init) =>
            (default, _ => { });
        public static T UseContext<T>(global::Velvet.ComponentContext<T> context) => default;
        public static (bool isPending, global::Velvet.TransitionStarter startTransition) UseTransition() =>
            (false, default);
        public static (global::Velvet.ISearchParams searchParams, global::Velvet.SearchParamsSetter setSearchParams) UseSearchParams() =>
            (null, null);
        public static global::Velvet.MutationResult<TVariables, TData> UseMutation<TVariables, TData>(
            global::Velvet.MutationOptions<TVariables, TData> options) => null;
        public static global::Velvet.MutationResult<TVariables, global::Velvet.Unit> UseMutation<TVariables>(
            global::Velvet.MutationOptions<TVariables> options) => null;
        public static global::Velvet.MutationResult<global::Velvet.Unit, global::Velvet.Unit> UseMutation(
            global::Velvet.MutationOptions options) => null;
        public static global::Velvet.Ref<T> UseRef<T>() where T : class => null;
        public static global::Velvet.Ref<T> UseRef<T>(global::System.Func<T> initialFactory) where T : class => null;
        public static global::Velvet.MutableRef<T> UseMutableRef<T>(T initial) =>
            new global::Velvet.MutableRef<T>(initial);
        public static global::Velvet.MutableRef<T> UseMutableRef<T>(global::System.Func<T> initialFactory) =>
            new global::Velvet.MutableRef<T>(default);
        public static void UseImperativeHandle<THandle>(
            global::Velvet.Ref<THandle> handleRef, global::System.Func<THandle> factory) where THandle : class { }
        public static void UseImperativeHandle<THandle>(
            global::Velvet.Ref<THandle> handleRef, global::System.Func<THandle> factory, params object[] deps) where THandle : class { }
    }

    public interface ISearchParams : global::System.Collections.Generic.IEnumerable<string> { }
    public sealed class SearchParamsSetter { }
    public readonly struct Unit : global::System.IEquatable<global::Velvet.Unit>
    {
        public bool Equals(global::Velvet.Unit other) => true;
        public override bool Equals(object obj) => true;
        public override int GetHashCode() => 0;
    }
    public sealed class MutationResult<TVariables, TData> { }
    public sealed record MutationOptions<TVariables, TData>(
        global::System.Func<TVariables, global::System.Threading.CancellationToken,
            global::Velvet.VelvetTask<TData>> MutationFn,
        global::System.Action<TData, TVariables>? OnSuccess = null,
        global::System.Action<global::System.Exception, TVariables>? OnError = null);
    public sealed record MutationOptions<TVariables>(
        global::System.Func<TVariables, global::System.Threading.CancellationToken,
            global::Velvet.VelvetTask> MutationFn,
        global::System.Action<TVariables>? OnSuccess = null,
        global::System.Action<global::System.Exception, TVariables>? OnError = null);
    public sealed record MutationOptions(
        global::System.Func<global::System.Threading.CancellationToken,
            global::Velvet.VelvetTask> MutationFn,
        global::System.Action? OnSuccess = null,
        global::System.Action<global::System.Exception>? OnError = null);
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

        public static ImmutableArray<Diagnostic> RunAnalyzer(string userSource, DiagnosticAnalyzer analyzer) =>
            Analyze(AnalyzerCompilation(userSource), analyzer);

        /// <summary>
        /// The same run, refusing a source that does not compile. An analyzer asking the semantic model about
        /// an unresolved call is answered <c>null</c> and reports nothing, so a fixture whose sample source
        /// carries a typo can pass its "nothing is reported" cases without the rule having reached the shape
        /// they name.
        /// </summary>
        public static ImmutableArray<Diagnostic> RunAnalyzerOnCompilingSource(
            string userSource, DiagnosticAnalyzer analyzer)
        {
            var compilation = AnalyzerCompilation(userSource);
            var errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            if (errors.Length > 0)
            {
                throw new System.InvalidOperationException(
                    "The analyzer sample source does not compile: "
                    + string.Join("; ", errors.Select(diagnostic => diagnostic.ToString())));
            }

            return Analyze(compilation, analyzer);
        }

        private static CSharpCompilation AnalyzerCompilation(string userSource)
        {
            var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(userSource, parseOptions),
                CSharpSyntaxTree.ParseText(VelvetStubSource, parseOptions),
            };

            return CSharpCompilation.Create(
                assemblyName: "AnalyzerTestAssembly",
                syntaxTrees: syntaxTrees,
                references: ReferenceAssemblies(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static ImmutableArray<Diagnostic> Analyze(CSharpCompilation compilation, DiagnosticAnalyzer analyzer)
        {
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
