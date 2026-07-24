using Velvet.SourceGenerators.Shared;

namespace Velvet.SourceGenerators.AutoDeps
{
    /// <summary>
    /// Describes how the deps-comparison analyzer locates the closure factory and the deps argument for a
    /// single deps-comparing Velvet hook. Velvet's hooks place these arguments at different positions
    /// (e.g. <c>UseImperativeHandle(refTarget, factory, deps)</c> vs <c>UseEffect(factory, deps)</c>), and
    /// <c>UseCallback</c> / <c>UseImperativeHandle</c> pass deps as <c>params</c> (loose trailing arguments).
    /// </summary>
    internal readonly struct DepsHookDescriptor
    {
        private DepsHookDescriptor(string containingTypeFullName, int factoryArgIndex, int depsArgIndex, bool depsAreParams)
        {
            ContainingTypeFullName = containingTypeFullName;
            FactoryArgIndex = factoryArgIndex;
            DepsArgIndex = depsArgIndex;
            DepsAreParams = depsAreParams;
        }

        /// <summary>
        /// Fully-qualified containing type that this method is bound to (e.g. <c>Velvet.Hooks</c> or
        /// <c>Velvet.V</c>). The analyzer compares the call's resolved <c>ContainingType.ToDisplayString()</c>
        /// to this so a same-named method on an unrelated type is not mistakenly checked.
        /// </summary>
        public string ContainingTypeFullName { get; }

        /// <summary>Zero-based position of the lambda factory argument.</summary>
        public int FactoryArgIndex { get; }

        /// <summary>Zero-based position of the first deps argument.</summary>
        public int DepsArgIndex { get; }

        /// <summary>
        /// True when deps are declared as <c>params object[]</c>, so they may appear either as a single
        /// array literal at <see cref="DepsArgIndex"/> or as multiple loose trailing arguments.
        /// </summary>
        public bool DepsAreParams { get; }

        /// <summary>
        /// Maps a Velvet deps-comparing method name to its descriptor, or returns false for non-deps methods.
        /// Covers the deps-comparison surface across both Velvet.Hooks (UseEffect / UseLayoutEffect /
        /// UseCallback / UseMemo / UseImperativeHandle) and the V DSL's memoized-subtree-node primitives
        /// (V.Memoized / V.MemoizedWithKey).
        /// </summary>
        public static bool TryGet(string methodName, out DepsHookDescriptor descriptor)
        {
            switch (methodName)
            {
                case VelvetWellKnownNames.UseEffectMethodName:
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.HooksTypeFullName, factoryArgIndex: 0, depsArgIndex: 1, depsAreParams: false);
                    return true;
                case VelvetWellKnownNames.UseLayoutEffectMethodName:
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.HooksTypeFullName, factoryArgIndex: 0, depsArgIndex: 1, depsAreParams: false);
                    return true;
                case VelvetWellKnownNames.UseCallbackMethodName:
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.HooksTypeFullName, factoryArgIndex: 0, depsArgIndex: 1, depsAreParams: true);
                    return true;
                case VelvetWellKnownNames.UseMemoMethodName:
                    // UseMemo's factory is parameterless, so it flows through the deps-comparison
                    // pipeline the same way UseCallback's does.
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.HooksTypeFullName, factoryArgIndex: 0, depsArgIndex: 1, depsAreParams: true);
                    return true;
                case VelvetWellKnownNames.UseImperativeHandleMethodName:
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.HooksTypeFullName, factoryArgIndex: 1, depsArgIndex: 2, depsAreParams: true);
                    return true;
                case VelvetWellKnownNames.VMemoizedMethodName:
                    // V.Memoized(Func<VNode> factory, params object[] deps) — the DSL's memoized-subtree-node
                    // primitive. Note: V also exposes generic Memo<TProps>(...) for props-equality Component
                    // memoization, which has no deps parameter and so does not match this descriptor at
                    // the analyzer's deps-arg lookup site (it returns early on shape mismatch).
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.VTypeFullName, factoryArgIndex: 0, depsArgIndex: 1, depsAreParams: true);
                    return true;
                case VelvetWellKnownNames.VMemoizedWithKeyMethodName:
                    // V.MemoizedWithKey(string key, Func<VNode> factory, params object[] deps).
                    descriptor = new DepsHookDescriptor(VelvetWellKnownNames.VTypeFullName, factoryArgIndex: 1, depsArgIndex: 2, depsAreParams: true);
                    return true;
                default:
                    descriptor = default;
                    return false;
            }
        }
    }
}
