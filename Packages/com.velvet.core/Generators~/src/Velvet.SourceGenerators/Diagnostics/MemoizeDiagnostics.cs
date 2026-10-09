using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.Diagnostics
{
    /// <summary>
    /// Diagnostic Descriptor definitions reported by the Velvet Memoize family of Source Generators / Analyzers.
    /// </summary>
    internal static class MemoizeDiagnostics
    {
        private const string Category = DiagnosticCategories.Memoize;
        // Per-analyzer category split: VEL100 to VEL103 are hook-rule diagnostics and must not be
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
            "Method '{0}' is async or returns a task; [MemoizeMethod] does not support async methods",
            "V.Memoized places a node synchronously, so there is nothing to place until a task completes. To memoize the task itself, call UseMemo.");

        public static readonly DiagnosticDescriptor Vel005RefOutParameterNotSupported = Warn(
            "VEL005",
            "[MemoizeMethod] does not support ref/out parameters",
            "Method '{0}' has a ref or out parameter; the factory that calls its _Impl runs later, during reconcile, and cannot capture the parameter",
            "The generated wrapper hands V.Memoized a factory that calls the _Impl method, and the reconciler runs that factory after the wrapper has returned, so no write through a ref or out parameter could reach the caller; a lambda cannot capture one either (CS1628). An in parameter is supported: it is copied, and the memo keys on the copy.");

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
            "Method '{0}' return type '{1}' is not Velvet.VNode or Velvet.MemoNode returned by value",
            "The generated body returns the MemoNode V.Memoized builds, by value, so the declared return type must be one MemoNode converts to: VNode or MemoNode itself, without ref. To memoize any other value, call UseMemo.");

        public static readonly DiagnosticDescriptor Vel009PartialMethodAlreadyHasBody = Warn(
            "VEL009",
            "[MemoizeMethod] partial method declaration must not have a body",
            "Method '{0}' already has a body; write implementation in '{0}_Impl' instead",
            "[MemoizeMethod] partial methods are declarations only; the implementation must be written in a separate method with the '_Impl' suffix by convention.");

        public static readonly DiagnosticDescriptor Vel010UnboxableParameterNotSupported = Warn(
            "VEL010",
            "[MemoizeMethod] does not support ref struct or pointer parameters",
            "Method '{0}' has parameter '{1}' of type '{2}', which cannot be a dependency; [MemoizeMethod] keys on every parameter",
            "Each parameter is a dependency: it is stored in the object?[] V.Memoized compares and read by the factory lambda. A ref struct such as Span<T> can be neither stored nor captured, a pointer cannot be stored, and a type holding one, such as int*[], needs an unsafe context the generated code does not open. Pass an array or a value the span or pointer was read from instead.");

        public static readonly DiagnosticDescriptor Vel011StructReceiverNotSupported = Warn(
            "VEL011",
            "[MemoizeMethod] instance member of a struct must be readonly and not of a ref struct",
            "Method '{0}' is an instance member of '{1}'; [MemoizeMethod] supports a struct instance member only when it is readonly and the struct is not a ref struct",
            "A lambda in a struct cannot capture this, so the generated factory calls _Impl on a copy of it: a write _Impl makes to the struct would reach the copy and be lost, so the member must be readonly. A ref struct cannot be copied into anything the factory can capture at all.");

        public static readonly DiagnosticDescriptor Vel012ImplReadsUnkeyedInstanceMember = Warn(
            "VEL012",
            "[MemoizeMethod] _Impl reads an instance member the memo does not key on",
            "'{0}' reads '{1}', which the memo generated for '{2}' does not key on; pass it to '{2}' as a parameter",
            "The generated wrapper keys the memo on its parameters, its type arguments and the instance it is called on, so a field or property that can change while the instance stays the same leaves the cached node stale. A static, const or readonly field and a static, init-only or get-only auto-property are fixed for the instance and are not reported, and nor is a read in a lambda or local function that runs after _Impl returns.");

        public static readonly DiagnosticDescriptor Vel100UseEffectMissingDep = HookWarn(
            "VEL100",
            "Hook lambda captures a local that is not in the deps array",
            "Hook lambda captures '{0}' but it is not present in the deps array; the closure may run with a stale value",
            "Compares closure-captured locals inside a deps-comparing hook's factory lambda (UseEffect / UseLayoutEffect / UseInsertionEffect / UseCallback / UseMemo / UseImperativeHandle / UseBlocker, plus V.Memoized / V.MemoizedWithKey) against the elements listed in the deps argument and warns on mismatches. Conservative: only flags simple `new[]` / `new T[] { ... }` deps initializers and loose params deps, and never treats the factory lambda's own parameters as dependencies.");

        public static readonly DiagnosticDescriptor Vel101HookInConditional = HookWarn(
            "VEL101",
            "Hook call inside conditional control flow",
            "'{0}' must not be called inside {1}; hooks must be called unconditionally at the top level so the per-fiber hook index aligns across renders",
            "Flags `Hooks.UseXxx` calls inside if/else, loops, short-circuit operators (&&/||/??), conditional expressions (?:), switch sections, or nested lambdas/anonymous methods. The runtime guards against silent corruption via the positional HookIndexTable (throws when hook counts differ across renders), but the static check surfaces the violation at edit time. As in eslint-plugin-react-hooks, a lambda counts only where it sits inside a component or a custom hook, a lambda that is a component's render body is the function the hook belongs to, and a member call is a hook call only on a receiver that is a single name starting with an uppercase letter, or a qualified name that binds to a namespace or a type. A hook in an if's condition, a conditional expression's condition or the left operand of &&, || or ?? is evaluated before the branch is chosen and is not reported.");

        public static readonly DiagnosticDescriptor Vel102HookOutsideComponentOrHook = HookWarn(
            "VEL102",
            "Hook call in a method that is neither a component nor a custom hook",
            "'{0}' is called in '{1}', which is neither a [Component] method nor a custom hook; mark it [Component], or rename it Use followed by an uppercase letter so VEL101 checks where it is called",
            "A hook called from a plain helper belongs to whichever component calls the helper, so a helper called on some renders only changes which hooks that component calls from one render to the next, and VEL101 reports nothing at that call because the helper is not named like a hook. Hooks belong in a component — a [Component] method, or a method handed by name to V.Component or V.Memo in the same compilation — or in a method or local function named Use followed by an uppercase letter, the shape VEL101 checks at each call site. A hook in a field or property initializer is reported naming that member. A hook whose nearest enclosing function is a lambda is not reported here, and nor is VEL101 asked of a hook this reports.");

        public static readonly DiagnosticDescriptor Vel103ComponentCalledDirectly = HookWarn(
            "VEL103",
            "Component called directly",
            "Component '{0}' calls hooks and is called here as a plain method, so its hooks run as part of the caller; mount it with V.Component instead",
            "A component's hooks belong to the fiber that renders it. Called as a plain method, it renders no fiber of its own: its hooks run against the calling component's fiber, so calling it on some renders only changes the hooks that component calls, and its state is the caller's. Reported where the call names a component this compilation declares, by a bare name from within its declaring type or qualified by that type's simple name, and the component's declaration contains a hook call. A call that names it otherwise (through using static, an alias, a base class or an instance), a component declared in another assembly, one calling no hook, and a call that is the whole render body of a lambda handed to V.Component are not reported.");

    }
}
