namespace Velvet
{
    // MotionContext: the active variant label propagated down the tree so a descendant Motion
    // that supplies variants but no explicit animate inherits the nearest ancestor Motion's label
    // and resolves it against its OWN variants. Carried on the same ComponentContextStack that
    // Router/Outlet use for their ambient values — Velvet's ambient-context mechanism — so propagation flows
    // through intervening components, including memoized ones.
    // Context propagation across a memo boundary holds WITHOUT a UseContext-style subscription, because the
    // label is consumed at the ELEMENT level (a Motion reads it during its own patch), not in a component body.
    // A memoized component that bails skips re-running its body, but Velvet's inline expansion still re-patches
    // its committed output against the live tree — and the ancestor Motion's label is on the cursor throughout
    // that walk — so a descendant Motion is re-resolved with the new label even though the memoized body did
    // not re-run (guarded by MotionVariantPropagationTests' memoized-boundary case). A descendant's OWN isolated
    // re-render reconstructs the ancestor label via FiberContextSpine.
    internal static class MotionContext
    {
        public static readonly ComponentContext<string> ActiveLabel = ComponentContext<string>.Create(null);
    }
}
