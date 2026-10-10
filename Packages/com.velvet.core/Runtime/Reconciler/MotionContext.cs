using System.Collections.Generic;

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

        // The initial label a descendant Motion inheriting ActiveLabel mounts from, pushed and popped with it.
        public static readonly ComponentContext<string> InitialLabel = ComponentContext<string>.Create(null);

        // Whether the presence child being emitted withholds mount enters (GeneralPathReconciler.LiveEntrySite).
        // Pushed around each presence-child emission, so an inner presence answers for its own children, and
        // carried by the snapshots a portal or a virtual list mounts under. Where neither pushed it, it is null
        // and ComponentFiber.BlocksInitialEnters answers instead.
        public static readonly ComponentContext<bool?> EntersBlocked = ComponentContext<bool?>.Create(null);

        // The staggerChildren/delayChildren orchestration a Motion's inheriting children claim their slots from,
        // while that Motion's label changes this render or its mount enter plays. Pushed alongside ActiveLabel by
        // FiberNodePatcher.PatchMotion and FiberNodeFactory.CreateForMotionNode; null (the default) means no
        // orchestration is active. FiberNodePatcher.ResolveChildOrchestration owns which frame each Motion hands
        // its children.
        public static readonly ComponentContext<MotionOrchestrationFrame> Orchestration =
            ComponentContext<MotionOrchestrationFrame>.Create(null);

        // The playback the Motion pushing ActiveLabel hands the descendants that take that label
        // (MotionVariantResolver.PlaybackForChildren). Kept in a snapshot that outlives its pass, unlike the
        // orchestration: a playback is the sequence's own for as long as it plays, not one pass's.
        public static readonly ComponentContext<MotionPlayback> Playback = ComponentContext<MotionPlayback>.Create(null);

        // What a Motion establishes for the subtree it reconciles, pushed together and popped together by
        // FiberNodeFactory.CreateForMotionNode and FiberNodePatcher.PatchMotion. FiberContextSpine rebuilds the
        // two labels and the playback, and not the orchestration, for an isolated render.
        public static void PushForChildren(ComponentContextStack stack, string label, string? initialLabel,
            MotionOrchestrationFrame? orchestration, MotionPlayback? playback)
        {
            stack.Push(ActiveLabel, label);
            stack.Push(InitialLabel, initialLabel);
            stack.Push(Orchestration, orchestration);
            stack.Push(Playback, playback);
        }

        public static void PopForChildren(ComponentContextStack stack)
        {
            stack.Pop(Playback);
            stack.Pop(Orchestration);
            stack.Pop(InitialLabel);
            stack.Pop(ActiveLabel);
        }

        // A context snapshot kept past the pass that took it, less the orchestration: a Motion mounting from it
        // mounts under a parent already mounted, which Framer staggers no further.
        public static List<KeyValuePair<object, object>>? OutlivingPass(List<KeyValuePair<object, object>>? snapshot)
        {
            snapshot?.RemoveAll(IsOrchestration);
            return snapshot;
        }

        private static bool IsOrchestration(KeyValuePair<object, object> entry)
            => ReferenceEquals(entry.Key, Orchestration);
    }

    // Mutable per-subtree stagger state pushed onto MotionContext.Orchestration (see its doc).
    // A reference type (not a struct) so the child-index counter mutates in place as siblings are visited, in
    // document order, during the same reconcile pass — ComponentContextStack.Get unboxes a COPY of a struct on
    // every read, which would reset the counter for each sibling instead of advancing it. A snapshot taken
    // for a deferred portal mount carries it to that pass's drain, and MotionContext.OutlivingPass takes it out
    // of every snapshot kept longer.
    internal sealed class MotionOrchestrationFrame
    {
        private readonly float _delayChildrenSec;
        private readonly float _staggerChildrenSec;
        // Extra delay (seconds) folded into EVERY claim from this frame, on top of delayChildren +
        // index*staggerChildren — see FiberNodePatcher.ResolveChildOrchestration for its contributions, which
        // measure a claim from render-commit time rather than from when the Motion's own delayed swap begins.
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
