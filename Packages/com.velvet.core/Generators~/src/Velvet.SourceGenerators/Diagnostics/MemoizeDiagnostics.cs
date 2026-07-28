using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic Descriptor definitions reported by the Velvet Memoize family of Source Generators / Analyzers.
    /// </summary>
    internal static class MemoizeDiagnostics
    {
        private const string Category = "Velvet.Memoize";
        // Per-analyzer category split: VEL100 / VEL101 are hook-rule diagnostics and must not be
        // silenced by a blanket Velvet.Memoize category suppression (e.g.
        // `dotnet_analyzer_diagnostic.category-Velvet.Memoize.severity = none`).
        private const string HookCategory = "Velvet.Hooks";

        private static DiagnosticDescriptor Warn(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description);

        private static DiagnosticDescriptor HookWarn(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, HookCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true, description);

        private static DiagnosticDescriptor Info(string id, string title, string messageFormat, string description) =>
            new(id, title, messageFormat, Category, DiagnosticSeverity.Info, isEnabledByDefault: true, description);

        public static readonly DiagnosticDescriptor Vel001ArityZeroCannotProvePurity = Warn(
            "VEL001",
            "[MemoizeMethod] arity 0 cannot prove purity",
            "Method '{0}' has no parameters and the corresponding _Impl method is not provably Pure; the deps-less cache may serve a stale VNode forever",
            "[MemoizeMethod] with no parameters (arity 0) caches a deps-less value — generation proceeds, but PurityAnalyzer could not statically prove the _Impl method has no side effects. Either annotate the _Impl with [Pure], remove the [MemoizeMethod] attribute, or accept the warning if you trust the body is deterministic.");

        public static readonly DiagnosticDescriptor Vel002ArityExceedsLimit = Warn(
            "VEL002",
            "[MemoizeMethod] supports only 1-8 parameters",
            "Method '{0}' has {1} parameters; [MemoizeMethod] supports 1-8 parameters",
            "Arity 9+ is not supported. If needed, future expansion to params object[] is planned.");

        public static readonly DiagnosticDescriptor Vel003GenericMethodNotSupported = Warn(
            "VEL003",
            "[MemoizeMethod] does not support generic methods",
            "Method '{0}' is generic; [MemoizeMethod] does not support generic methods",
            "[MemoizeMethod] on generic methods is not supported. Future support is planned.");

        public static readonly DiagnosticDescriptor Vel004AsyncMethodNotSupported = Warn(
            "VEL004",
            "[MemoizeMethod] does not support async methods",
            "Method '{0}' is async or returns Task; [MemoizeMethod] does not support async methods",
            "[MemoizeMethod] on async methods is not supported. Future support is planned.");

        public static readonly DiagnosticDescriptor Vel005RefOutParameterNotSupported = Warn(
            "VEL005",
            "[MemoizeMethod] does not support ref/out/in parameters",
            "Method '{0}' has a ref/out/in parameter; [MemoizeMethod] does not support by-reference parameters",
            "ref/out/in parameters cannot be safely used as deps and are not supported.");

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
            "[MemoizeMethod] method must return Velvet.VNode or a derived type",
            "Method '{0}' return type '{1}' is not Velvet.VNode or a derived type",
            "V.Memo returns a MemoNode (which derives from VNode), so the target method's return type must derive from VNode.");

        public static readonly DiagnosticDescriptor Vel009PartialMethodAlreadyHasBody = Warn(
            "VEL009",
            "[MemoizeMethod] partial method declaration must not have a body",
            "Method '{0}' already has a body; write implementation in '{0}_Impl' instead",
            "[MemoizeMethod] partial methods are declarations only; the implementation must be written in a separate method with the '_Impl' suffix by convention.");

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
