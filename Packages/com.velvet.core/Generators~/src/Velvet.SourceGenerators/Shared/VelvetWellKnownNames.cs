namespace Velvet.SourceGenerators.Shared
{
    /// <summary>
    /// Aggregates the fully-qualified names and MetadataNames of Velvet-specific types and attributes.
    /// Single source of truth that prevents drift between Analyzer / CodeFix / Generator.
    /// </summary>
    /// <remarks>
    /// These are plain strings because this solution cannot reference the Unity runtime assembly, so the
    /// compiler cannot check them against the real surface. The Generators~ hook-surface drift guard pins
    /// them against the runtime source instead, resolving each <c>…FullName</c> to a declared type and each
    /// <c>…MethodName</c> to a declared method. Those two suffixes are what makes a constant pinned, so a new
    /// constant is guarded without editing that guard only when it is named with one — and the guard fails on
    /// a constant carrying neither suffix unless it is recorded there as naming no declaration, which is why
    /// <see cref="Namespace"/> does not silently sit unchecked. A constant declared outside this type is not
    /// covered at all.
    /// </remarks>
    internal static class VelvetWellKnownNames
    {
        public const string Namespace = "Velvet";
        public const string ComponentAttributeFullName = "Velvet.ComponentAttribute";
        public const string MemoizeMethodAttributeFullName = "Velvet.MemoizeMethodAttribute";
        public const string VNodeFullName = "global::Velvet.VNode";
        public const string HooksTypeFullName = "Velvet.Hooks";
        public const string VTypeFullName = "Velvet.V";
        public const string UseEffectMethodName = "UseEffect";
        public const string UseLayoutEffectMethodName = "UseLayoutEffect";
        public const string UseInsertionEffectMethodName = "UseInsertionEffect";
        public const string UseCallbackMethodName = "UseCallback";
        public const string UseMemoMethodName = "UseMemo";
        public const string UseImperativeHandleMethodName = "UseImperativeHandle";
        public const string UseBlockerMethodName = "UseBlocker";
        public const string VMemoizedMethodName = "Memoized";
        public const string VMemoizedWithKeyMethodName = "MemoizedWithKey";

        public const string UseStateMethodName = "UseState";
        public const string UseReducerMethodName = "UseReducer";
        public const string UseRefMethodName = "UseRef";
        public const string UseMutableRefMethodName = "UseMutableRef";
        public const string UseTransitionMethodName = "UseTransition";
        public const string UseSearchParamsMethodName = "UseSearchParams";
        public const string UseMutationMethodName = "UseMutation";
    }
}
