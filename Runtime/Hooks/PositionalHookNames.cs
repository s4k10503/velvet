namespace Velvet
{
    // Canonical list of hook method names whose calls the ILPP weaver (CompilerWeaver) must keep in the
    // hook section so the position-based slot order is preserved. Includes every hook that allocates a positional
    // slot via its own HookIndexTable index, plus custom hooks that consume one transitively
    // (e.g. UseService via UseContext).
    // CompilerWeaver reads this list directly (same assembly), so there is no second copy to keep in
    // sync. PositionalHookNamesLockstepTests pins the set against accidental edits.
    internal static class PositionalHookNames
    {
        internal static readonly string[] All =
        {
            nameof(Hooks.UseEffect),
            nameof(Hooks.UseLayoutEffect),
            nameof(Hooks.UseInsertionEffect),
            nameof(Hooks.UseCallback),
            nameof(Hooks.UseMemo),
            nameof(Hooks.UseBlocker),
            nameof(Hooks.UseState),
            nameof(Hooks.UseReducer),
            nameof(Hooks.UseOptimistic),
            nameof(Hooks.UseStore),
            nameof(Hooks.UseContext),
            nameof(Hooks.UseRef),
            nameof(Hooks.UseMutableRef),
            nameof(Hooks.UseImperativeHandle),
            nameof(Hooks.UseTransition),
            nameof(Hooks.UseId),
            nameof(Hooks.UseDeferredValue),
            nameof(Hooks.UseMutation),
            nameof(Hooks.UseService),
            nameof(Hooks.UseFallback),
            nameof(Hooks.Use),
            nameof(Hooks.UseFrame),
        };
    }
}
