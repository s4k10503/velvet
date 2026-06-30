namespace Velvet
{
    // Aggregates per-hook-kind call-position cursors for a single render cycle.
    internal struct HookIndexTable
    {
        public int HookIndex;
        public int BlockerHookIndex;
        public int LayoutEffectHookIndex;
        public int InsertionEffectHookIndex;
        public int EffectHookIndex;
        public int StateHookIndex;
        public int StoreHookIndex;
        public int ImperativeHandleHookIndex;
        public int RefHookIndex;
        public int MemoHookIndex;
        public int MemoValueHookIndex;
        public int IdHookIndex;
        public int DeferredValueHookIndex;
        public int OptimisticHookIndex;
        public int MutationHookIndex;
        public int TransitionHookIndex;

        public void Reset() => this = default;
    }
}
