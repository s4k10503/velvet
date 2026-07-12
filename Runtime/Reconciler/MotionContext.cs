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

        // The active staggerChildren/delayChildren orchestration for the current label-propagation subtree,
        // established by the nearest ancestor Motion whose active label just changed THIS render and whose own
        // Transition declares StaggerChildrenSec / DelayChildrenSec / a non-Together When. Pushed alongside
        // ActiveLabel by FiberNodePatcher.PatchMotion. Null (the default, and the overwhelming common case) means
        // no orchestration is active — a Motion that declares none of those knobs costs nothing beyond the
        // ordinary label push. Flows THROUGH an inheriting descendant (no own Animate) that declares no
        // orchestration of its own, so a grandchild keeps claiming from the SAME ancestor sequence; a descendant
        // with its own explicit Animate breaks the chain (pushes null) since it is no longer driven by this
        // propagation. See MotionOrchestrationFrame for the per-child delay computation.
        public static readonly ComponentContext<MotionOrchestrationFrame> Orchestration =
            ComponentContext<MotionOrchestrationFrame>.Create(null);
    }

    // Mutable per-subtree stagger state pushed onto MotionContext.Orchestration when a Motion's active label
    // just changed and its own Transition declares StaggerChildrenSec / DelayChildrenSec / a non-Together When.
    // A reference type (not a struct) so the child-index counter mutates in place as siblings are visited, in
    // document order, during the same reconcile pass — ComponentContextStack.Get unboxes a COPY of a struct on
    // every read, which would reset the counter for each sibling instead of advancing it. Pushed once per
    // label-changing render and popped when that render's subtree walk unwinds (see FiberNodePatcher.PatchMotion),
    // so an instance never survives past the render that created it.
    internal sealed class MotionOrchestrationFrame
    {
        private readonly float _delayChildrenSec;
        private readonly float _staggerChildrenSec;
        // Extra delay (seconds) folded into EVERY claim from this frame, on top of delayChildren +
        // index*staggerChildren — see FiberNodePatcher.ResolveChildOrchestration for the two contributions:
        // this Motion's own [DelaySec, DelaySec + DurationSec] span when When == BeforeChildren (inheriting
        // descendants wait for the parent's own swap to finish, not just start), PLUS — when this Motion is
        // itself an inheriting descendant claiming a delay from a FURTHER-OUT orchestration — that claimed
        // delay, so a claim from this frame is measured from render-commit time (when the whole chain actually
        // started), not from when this Motion's own already-delayed swap begins.
        private readonly float _baseDelaySec;
        private int _nextChildIndex;

        public MotionOrchestrationFrame(float delayChildrenSec, float staggerChildrenSec, float baseDelaySec)
        {
            _delayChildrenSec = delayChildrenSec;
            _staggerChildrenSec = staggerChildrenSec;
            _baseDelaySec = baseDelaySec;
        }

        // Claims the next sequential slot (document order) and returns the claiming descendant's total extra
        // delay (seconds): delayChildren + index * staggerChildren, plus this frame's base delay (see
        // _baseDelaySec).
        public float ClaimNextChildDelaySec()
        {
            var index = _nextChildIndex++;
            return _delayChildrenSec + index * _staggerChildrenSec + _baseDelaySec;
        }
    }
}
