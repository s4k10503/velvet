namespace Velvet.SourceGenerators.Shared
{
    /// <summary>
    /// Aggregates the fully-qualified names and MetadataNames of Velvet-specific types and attributes.
    /// Single source of truth that prevents drift between Analyzer / CodeFix / Generator.
    /// </summary>
    internal static class VelvetWellKnownNames
    {
        public const string Namespace = "Velvet";
        public const string ComponentAttributeFullName = "Velvet.ComponentAttribute";
        public const string VNodeFullName = "global::Velvet.VNode";
        public const string HooksTypeFullName = "Velvet.Hooks";
        public const string VTypeFullName = "Velvet.V";
        public const string UseEffectMethodName = "UseEffect";
        public const string UseLayoutEffectMethodName = "UseLayoutEffect";
        public const string UseCallbackMethodName = "UseCallback";
        public const string UseMemoMethodName = "UseMemo";
        public const string UseImperativeHandleMethodName = "UseImperativeHandle";
        public const string VMemoizedMethodName = "Memoized";
        public const string VMemoizedWithKeyMethodName = "MemoizedWithKey";

        public const string UseStateMethodName = "UseState";
        public const string UseReducerMethodName = "UseReducer";
        public const string UseRefMethodName = "UseRef";
        public const string UseMutableRefMethodName = "UseMutableRef";
    }
}
