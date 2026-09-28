using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic Descriptor definitions reported by the Velvet Memoize family of Source Generators / Analyzers.
    /// </summary>
    internal static class MemoizeDiagnostics
    {
        private const string Category = DiagnosticCategories.Memoize;
        // Per-analyzer category split: VEL100 / VEL101 are hook-rule diagnostics and must not be
        // silenced by a blanket Velvet.Memoize category suppression (e.g.
        // `dotnet_analyzer_diagnostic.category-Velvet.Memoize.severity = none`).
        private const string HookCategory = DiagnosticCategories.Hooks;

        private static DiagnosticDescriptor Warn(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description);

        private static DiagnosticDescriptor HookWarn(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, HookCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true, description);

        private static DiagnosticDescriptor Info(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description);

        public static readonly DiagnosticDescriptor Vel004AsyncMethodNotSupported = Warn(
            "VEL004",
            "[MemoizeMethod] does not support async methods",
            "Method '{0}' is async or returns Task; [MemoizeMethod] does not support async methods",
            "V.Memoized places a node synchronously, so there is nothing to place until a task completes. To memoize the task itself, call UseMemo.");

        public static readonly DiagnosticDescriptor Vel005RefOutParameterNotSupported = Warn(
            "VEL005",
            "[MemoizeMethod] does not support ref/out parameters",
            "Method '{0}' has a ref or out parameter; a cached render would skip the write through it",
            "A render served from the cache does not run the _Impl method, so a write it makes through a ref or out parameter would happen on some renders and not others. An in parameter is supported.");

        public static readonly DiagnosticDescriptor Vel006MissingAccessibilityModifier = Warn(
            "VEL006",
            "[MemoizeMethod] partial method declaration requires an accessibility modifier",
            "Method '{0}' requires an accessibility modifier (private, internal, etc.) for [MemoizeMethod] (C# 9.0 extended partial methods spec)",
            "In the implementation-providing form, C# 9.0 extended partial methods require an accessibility modifier.");

        public static readonly DiagnosticDescriptor Vel007ContainingTypeNotPartial = Warn(
            "VEL007",
            "[MemoizeMethod] containing type must be declared partial",
            "Type '{0}' containing [MemoizeMethod] method must be declared 'partial'",
            "The Source Generator adds generated code to the existing class, so the containing class must also be declared partial.");

        public static readonly DiagnosticDescriptor Vel008NonVNodeReturnType = Warn(
            "VEL008",
            "[MemoizeMethod] method must return Velvet.VNode",
            "Method '{0}' return type '{1}' is not Velvet.VNode or Velvet.MemoNode",
            "The generated body returns the MemoNode V.Memoized builds, so the declared return type must be one MemoNode converts to: VNode or MemoNode itself. To memoize any other value, call UseMemo.");

        public static readonly DiagnosticDescriptor Vel009PartialMethodAlreadyHasBody = Warn(
            "VEL009",
            "[MemoizeMethod] partial method declaration must not have a body",
            "Method '{0}' already has a body; write implementation in '{0}_Impl' instead",
            "[MemoizeMethod] partial methods are declarations only; the implementation must be written in a separate method with the '_Impl' suffix by convention.");

        public static readonly DiagnosticDescriptor Vel010UnboxableParameterNotSupported = Warn(
            "VEL010",
            "[MemoizeMethod] does not support ref struct or pointer parameters",
            "Method '{0}' has parameter '{1}' of type '{2}', which cannot be a dependency; [MemoizeMethod] keys on every parameter",
            "Each parameter is a dependency: it is boxed into the object?[] V.Memoized compares and read by the factory lambda. A ref struct such as Span<T> can be neither boxed nor captured, and a pointer cannot be boxed. Pass an array or a value the span or pointer was read from instead.");

        public static readonly DiagnosticDescriptor Vel100UseEffectMissingDep = HookWarn(
            "VEL100",
            "Hook lambda captures a local that is not in the deps array",
            "Hook lambda captures '{0}' but it is not present in the deps array; the closure may run with a stale value",
            "Compares closure-captured locals inside a deps-comparing hook's factory lambda (UseEffect / UseLayoutEffect / UseInsertionEffect / UseCallback / UseMemo / UseImperativeHandle / UseBlocker, plus V.Memoized / V.MemoizedWithKey) against the elements listed in the deps argument and warns on mismatches. Conservative: only flags simple `new[]` / `new T[] { ... }` deps initializers and loose params deps, and never treats the factory lambda's own parameters as dependencies.");

        public static readonly DiagnosticDescriptor Vel101HookInConditional = HookWarn(
            "VEL101",
            "Hook call inside conditional control flow",
            "'{0}' must not be called inside {1}; hooks must be called unconditionally at the top level so the per-fiber hook index aligns across renders",
            "Flags `Hooks.UseXxx` calls inside if/else, loops, short-circuit operators (&&/||/??), conditional expressions (?:), switch sections, or nested lambdas/anonymous methods. The runtime guards against silent corruption via the positional HookIndexTable (throws when hook counts differ across renders), but the static check surfaces the violation at edit time.");
    }
}
