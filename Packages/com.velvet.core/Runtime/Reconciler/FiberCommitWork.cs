#nullable enable
namespace Velvet
{
    // The commit phase for a function-component fiber: applies the rendered
    // VNode tree to the host (UI-Toolkit) tree and reconciles the prior committed tree's lifecycle. For
    // wrapper-less (inline-mounted) fibers it also owns the inline-slot geometry: several fibers share one
    // MountPoint, so each commits only its own [MountSlotStart, MountSlotStart + MountSlotCount) sub-range
    // and propagates its child-count delta to following siblings. Pure functions of the fiber + the trees
    // the orchestrator (FiberRenderer.RenderAndReconcile) hands in after FiberBeginWork produced them.
    internal static class FiberCommitWork
    {
        // The slot index at which this inline fiber's range ENDS within its shared MountPoint — the
        // nearest co-located tenant's MountSlotStart beyond this fiber's own, or int.MaxValue when
        // this fiber is the last/only tenant. Used to bound the keyed desync-recovery rebuild so it
        // cannot reach past this fiber's rows into a sibling's. The sibling chain is fiber-CREATION
        // order and a keyed reorder does not resync it, so the first co-located entry can sit
        // visually BEFORE this fiber; bounding by it would make slotLimit < slotStart — every slot
        // in this fiber's own range would look missing, and an independent re-render of the
        // displaced fiber would insert a permanent duplicate instead of patching in place. Walk the
        // parent's whole co-located chain instead and take the minimum start strictly beyond ours.
        private static int NextInlineSiblingSlotStart(ComponentFiber fiber)
        {
            var limit = int.MaxValue;
            var first = fiber.Parent?.Child ?? fiber.Sibling;
            for (var sibling = first; sibling != null; sibling = sibling.Sibling)
            {
                if (!ReferenceEquals(sibling, fiber)
                    && sibling.IsInlineMounted
                    && sibling.MountPoint == fiber.MountPoint
                    && sibling.MountSlotStart > fiber.MountSlotStart
                    && sibling.MountSlotStart < limit)
                {
                    limit = sibling.MountSlotStart;
                }
            }
            return limit;
        }

        // Propagates an inline-mount fiber's committed child-count change to the siblings that share its
        // MountPoint. Updates the fiber's own slot count, then shifts every following sibling's
        // ComponentFiber.MountSlotStart by the same delta and re-bases the captured slotStart of
        // any following sibling whose own reconcile is parked. Called both from the initial
        // RenderAndReconcile pass and from each ContinueReconcile
        // resume slice, since the delta is committed incrementally across slices. No-op when
        // actualDelta is zero.
        internal static void PropagateInlineSlotShift(ComponentFiber fiber, int actualDelta)
        {
            if (actualDelta == 0) return;

            var baseline = fiber.MountSlotCount < 0 ? 0 : fiber.MountSlotCount;
            fiber.MountSlotCount = baseline + actualDelta;
            for (var sibling = fiber.Sibling; sibling != null; sibling = sibling.Sibling)
            {
                sibling.MountSlotStart += actualDelta;
                // A following sibling whose own time-sliced reconcile is parked captured its slotStart as an
                // absolute offset into the shared parent. This shift moved its already-committed rows within
                // that parent, so re-base the suspended state by the same delta; otherwise its resume writes
                // the remaining rows at stale absolute indices. Wrapper-mounted siblings reconcile their own VE
                // at slotStart 0 and are unaffected.
                if (sibling.IsInlineMounted && sibling.Reconciler?.HasPendingWork == true)
                {
                    sibling.Reconciler.RebasePendingSlotStart(actualDelta);
                }
            }
        }

        // Drains parked time-sliced work before the new reconcile measures childCount. Force-draining
        // commits the remaining child-count delta (e.g. the atomic keyed reorder inserting created
        // elements). For an inline-mount fiber that delta must propagate to following siblings exactly as
        // the scheduled resume (ContinueReconcile) and the initial pass do — the new reconcile measures
        // childCount AFTER this drain, so it would otherwise absorb the drain's delta and leave following
        // siblings' MountSlotStart stale.
        internal static void DrainPendingWork(ComponentFiber fiber)
        {
            if (fiber.IsInlineMounted)
            {
                var beforeDrain = fiber.MountPoint?.childCount ?? 0;
                fiber.Reconciler!.ContinueReconcile(frameBudgetMs: 0);
                var afterDrain = fiber.MountPoint?.childCount ?? 0;
                PropagateInlineSlotShift(fiber, afterDrain - beforeDrain);
            }
            else
            {
                fiber.Reconciler!.ContinueReconcile(frameBudgetMs: 0);
            }
            fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
            // Detach the parked baseline BEFORE retiring it: the sweep's own mark treats
            // owner.PendingOldTree as live (other fibers' retirements must spare a parked diff's
            // baseline), and a still-attached reference would spare this very sweep's target.
            var parkedTree = fiber.PendingOldTree;
            fiber.PendingOldTree = null;
            FiberTreeReturn.ReturnRetiredTree(parkedTree, fiber);
        }

        // Reconciles this render's output into the fiber's slot range and commits the sibling-shift delta.
        // For wrapper-less (inline-mounted) fibers, reconcile only the sub-range
        // [MountSlotStart, MountSlotStart + MountSlotCount) of MountPoint.children so
        // sibling fibers' slots are preserved. MountSlotCount may grow or shrink as the
        // body's top-level output count changes; the new count is committed here and
        // the delta propagated to subsequent sibling fibers' MountSlotStart.
        //
        // When deferReconcile is true (initial inline mount), the commit is performed by the caller's
        // parent expansion rather than this fiber's own Reconciler: the newTree is captured on
        // PreviousTree by the caller and consumed by ExpandInlineRecursive, which inserts the output VEs
        // into the parent at the fiber's slot range — so the FiberRenderer-side bookkeeping is skipped
        // here to avoid using the unexpanded VNode count.
        internal static void ReconcileIntoSlotRange(
            ComponentFiber fiber, VNode?[] oldTree, VNode?[] newTree, double frameBudgetMs, bool deferReconcile)
        {
            var slotStart = fiber.IsInlineMounted ? fiber.MountSlotStart : 0;
            if (!deferReconcile)
            {
                // For inline-mounted fibers, the slot footprint is the *expanded* DOM count —
                // <c>newTree</c> may include a top-level Fragment / ContextProvider whose
                // expansion produces a different number of leaves, or descendant ComponentNodes
                // whose own subtrees contribute additional VEs. Measure parent.childCount
                // before / after Reconcile to capture the actual mutation within this fiber's
                // slot range, then shift subsequent siblings by that delta. Wrapper-mounted
                // fibers own their entire MountPoint and don't participate in sibling shifts.
                if (fiber.IsInlineMounted)
                {
                    var beforeChildCount = fiber.MountPoint?.childCount ?? 0;
                    // Bound the reconcile to this fiber's slot range so a desync rebuild cannot delete a
                    // following sibling's committed rows — the next inline-mount sibling's MountSlotStart is
                    // where this fiber's rows end.
                    var slotLimit = NextInlineSiblingSlotStart(fiber);
                    fiber.Reconciler!.Reconcile(fiber.MountPoint, oldTree, newTree, frameBudgetMs, slotStart, slotLimit);
                    var afterChildCount = fiber.MountPoint?.childCount ?? 0;
                    PropagateInlineSlotShift(fiber, afterChildCount - beforeChildCount);
                }
                else
                {
                    fiber.Reconciler!.Reconcile(fiber.MountPoint, oldTree, newTree, frameBudgetMs, slotStart);
                }
            }
        }

        // Returns the prior committed tree to the VNode pool after the reconcile, or parks / defers it.
        // The caller must have committed the new tree to fiber.PreviousTree already: the recycle sweep
        // marks the committed tree live so nodes a memo hit shares across the two renders are spared.
        internal static void ReturnOldTreeAfterReconcile(
            ComponentFiber fiber, Reconciler? reconciler, VNode?[] oldTree, VNode?[]? prevPendingOldTree, bool deferReconcile)
        {
            if (deferReconcile)
            {
                // deferReconcile means the caller's parent expansion (ChildReconciler) reconciles and
                // commits this fiber's leaves, and on an UPDATE it still holds references to this fiber's
                // OLD tree as the patch baseline (captured during its old-side expansion of
                // fiber.PreviousTree, before this render overwrote it). Returning that old tree to the
                // VNode pool now would let the SAME parent pass rent and mutate these very nodes while
                // rendering later siblings — a use-after-return that empties the baseline's children, so
                // PatchNode re-inserts the child's whole subtree instead of patching it (the subtree
                // visibly duplicates). Defer the return to the top-level reconcile boundary, where the
                // pass is complete and no renter can alias the nodes — pooling is preserved (no extra GC),
                // correctness restored. Skip empty trees (initial inline mount) so the queue holds only
                // real baselines. A deferred render never schedules its own reconcile, so there is no
                // pending-work / PendingOldTree case here.
                if (oldTree is { Length: > 0 })
                {
                    if (reconciler != null)
                    {
                        reconciler.Context.DeferredInlineOldTreeReturns.Add((oldTree, fiber));
                    }
                    else
                    {
                        // The fiber was disposed mid-render, so there is no context queue to defer
                        // into — and no later drain that would ever reach this baseline. Retiring it
                        // immediately is safe: the pass-scoped release staging keeps its objects
                        // un-rentable until the enclosing pass (which may still read old-side
                        // captures of these nodes) has fully ended.
                        FiberTreeReturn.ReturnRetiredTree(oldTree, fiber);
                    }
                }
            }
            else if (reconciler != null && reconciler.HasPendingWork)
            {
                fiber.PendingOldTree = oldTree;
                // Registered so retirement sweeps elsewhere treat this parked baseline as live: the
                // paused pass keeps diffing against it across frames.
                reconciler.Context.ParkedBaselineFibers.Add(fiber);
                fiber.MountPoint?.schedule.Execute(() => FiberWorkLoop.ContinueReconcile(fiber));
            }
            else
            {
                FiberTreeReturn.ReturnRetiredTree(oldTree, fiber);
            }

            ReturnSupersededParkedBaseline(fiber, prevPendingOldTree, oldTree);
        }

        // Retires a superseded parked baseline. The identity guard encodes an aliasing rule shared
        // by every caller (the normal path here, plus the abort and exception paths in
        // FiberRenderer): PendingOldTree may be the very array a new render adopted as oldTree, and
        // retiring it then would recycle the baseline that render still owns.
        internal static void ReturnSupersededParkedBaseline(
            ComponentFiber fiber, VNode?[]? prevPendingOldTree, VNode?[]? oldTree)
        {
            if (prevPendingOldTree == null || prevPendingOldTree == oldTree) return;
            FiberTreeReturn.ReturnRetiredTree(prevPendingOldTree, fiber);
        }
    }
}
