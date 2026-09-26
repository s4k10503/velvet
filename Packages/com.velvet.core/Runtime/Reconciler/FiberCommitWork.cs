#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The commit phase for a function-component fiber: applies the rendered
    // VNode tree to the host (UI-Toolkit) tree and reconciles the prior committed tree's lifecycle. For
    // wrapper-less (inline-mounted) fibers it also owns the inline-slot geometry: several fibers share one
    // MountPoint, so each commits only its own [MountSlotStart, MountSlotStart + MountSlotCount) sub-range
    // and moves the recorded starts behind it by its child-count delta. Pure functions of the fiber + the
    // trees the orchestrator (FiberRenderer.RenderAndReconcile) hands in after FiberBeginWork produced them.
    internal static class FiberCommitWork
    {
        // The slotLimit of this fiber's own reconcile: the nearest start beyond its own among the co-located
        // fibers of its parent's chain, or int.MaxValue. The chain is the parent's alone, so a fiber that is
        // its holder's last child gets int.MaxValue even where the holder's next sibling's rows follow.
        // A fiber no walk has placed yet (MountSlotCount still -1) is passed over: its start is where a walk
        // still in progress means to put it, and a boundary catching in that walk would be bounded short of
        // its own rows.
        private static int NextInlineSiblingSlotStart(ComponentFiber fiber)
        {
            var limit = int.MaxValue;
            var first = fiber.Parent?.Child ?? fiber.Sibling;
            for (var sibling = first; sibling != null; sibling = sibling.Sibling)
            {
                if (!ReferenceEquals(sibling, fiber)
                    && sibling.IsInlineMounted
                    && sibling.MountSlotCount >= 0
                    && sibling.MountPoint == fiber.MountPoint
                    && sibling.MountSlotStart > fiber.MountSlotStart
                    && sibling.MountSlotStart < limit)
                {
                    limit = sibling.MountSlotStart;
                }
            }
            return limit;
        }

        // The mount point's rendered child count, in LOGICAL slots — the basis every stored slotStart
        // shares, so an invisible child appearing or disappearing anywhere in the list cancels out of a
        // before/after delta instead of being read as a rendered child arriving or leaving.
        internal static int LogicalMountPointChildCount(VisualElement? mountPoint)
            => mountPoint != null ? LogicalChildSlots.Count(mountPoint) : 0;

        // Commits an inline-mount fiber's child-count change: its own count and the counts of the fibers
        // whose rows hold its rows, then the recorded starts the change moves — each fiber on the same
        // MountPoint whose rows come after this fiber's, and, where this fiber's rows are a Portal's, that
        // Portal's range and the ranges after it. Called from ReconcileOwnRows, from each ContinueReconcile
        // resume slice and from the force-drain, since a delta can be committed incrementally across slices.
        // No-op when actualDelta is zero.
        //
        // The fibers moved are read off the registry's index of the MountPoint rather than off the sibling
        // chain: a fiber's rows come after this one's whether it is a sibling, a sibling's own inline child,
        // a sibling of a fiber whose rows hold this one's, or a child a neighbouring Portal mounted onto the
        // same target, and the chain reaches only the first.
        internal static void PropagateInlineSlotShift(ComponentFiber fiber, int actualDelta)
        {
            if (actualDelta == 0) return;
            var mountPoint = fiber.MountPoint;

            var heldNoRows = fiber.MountSlotCount <= 0;
            for (var holder = fiber; IsTenantOf(holder, mountPoint); holder = holder!.Parent)
            {
                holder!.MountSlotCount = Math.Max(holder.MountSlotCount, 0) + actualDelta;
            }

            var tenancy = TenancyOf(fiber);
            if (tenancy != null)
            {
                tenancy.ShiftedRows += actualDelta;
                foreach (var tenant in tenancy.Fibers)
                {
                    if (IsPlacedAfter(tenant, fiber, heldNoRows)) MoveTenant(tenant, actualDelta);
                }
            }
            ShiftPortalRangesAround(fiber, mountPoint, actualDelta);
        }

        // A Portal's range on target, changed before this, changed length by delta. The fibers whose rows lie
        // behind it move with it (PortalSlotTracker.IsBehind decides, as it does for the other Portals' ranges);
        // the range's own fibers were placed by the reconcile that changed it, or are leaving with it. Called
        // beside PortalSlotTracker.ShiftRangesBehind.
        internal static void ShiftTenantsAfterPortalRange(
            ComponentRegistry registry, VisualElement target, VisualElement placeholder, PortalSlotInfo changed, int delta)
        {
            // MUTANT_SURVIVES(equivalent): a zero delta moves no start and adds nothing to the total, so the
            // loop it skips changes nothing.
            if (delta == 0) return;
            var tenancy = registry.TenancyOf(target);
            if (tenancy == null) return;
            tenancy.ShiftedRows += delta;
            foreach (var tenant in tenancy.Fibers)
            {
                var owning = OwningPortalOf(tenant, target);
                if (ReferenceEquals(owning, placeholder)) continue;
                if (!PortalSlotTracker.IsBehind(tenant.MountSlotStart, tenant.MountSlotCount > 0, owning, placeholder, changed))
                {
                    continue;
                }
                MoveTenant(tenant, delta);
            }
        }

        private static void MoveTenant(ComponentFiber tenant, int delta)
        {
            tenant.MountSlotStart += delta;
            // A tenant whose own time-sliced reconcile is parked captured its slotStart as an absolute offset
            // into the shared parent. This shift moved its already-committed rows within that parent, so
            // re-base the suspended state by the same delta; otherwise its resume writes the remaining rows at
            // stale absolute indices.
            if (tenant.Reconciler?.HasPendingWork == true)
            {
                tenant.Reconciler.RebasePendingSlotStart(delta);
            }
        }

        private static InlineTenancy? TenancyOf(ComponentFiber fiber)
            => fiber.MountPoint == null ? null : fiber.Reconciler?.Context.ComponentRegistry.TenancyOf(fiber.MountPoint);

        // A MountPoint's child-count change across a span of work, less the rows a reconcile nested inside
        // the span already moved the recorded starts for. The span's before/after count includes what a
        // boundary's fallback swap inside the fiber's own walk, or a parked fiber drained inside it, changed,
        // and each of those has propagated its own change: counted again here, the starts behind it would
        // move twice.
        internal readonly struct RowCountWindow
        {
            private readonly VisualElement? _mountPoint;
            private readonly InlineTenancy? _tenancy;
            private readonly int _rowsBefore;
            private readonly int _shiftedBefore;

            internal RowCountWindow(ComponentFiber fiber)
            {
                _mountPoint = fiber.MountPoint;
                _tenancy = TenancyOf(fiber);
                _rowsBefore = LogicalMountPointChildCount(_mountPoint);
                _shiftedBefore = _tenancy?.ShiftedRows ?? 0;
            }

            internal int UnshiftedChange
                => LogicalMountPointChildCount(_mountPoint) - _rowsBefore
                   - ((_tenancy?.ShiftedRows ?? 0) - _shiftedBefore);
        }

        private static bool IsTenantOf(ComponentFiber? fiber, VisualElement? mountPoint)
        {
            // MUTANT_SURVIVES(equivalent, clause removed): a non-inline fiber on a tenant's MountPoint is a root or wrapper.
            // Such a fiber never propagates, is in no tenancy, is never the holder HoldsRowsOf looks for, and
            // carries no Portal placeholder, so walking through it changes nothing any of those read.
            return fiber != null && fiber.IsInlineMounted && ReferenceEquals(fiber.MountPoint, mountPoint);
        }

        private static bool HoldsRowsOf(ComponentFiber outer, ComponentFiber inner)
        {
            for (var holder = inner.Parent; IsTenantOf(holder, inner.MountPoint!); holder = holder!.Parent)
            {
                if (ReferenceEquals(holder, outer)) return true;
            }
            return false;
        }

        // Ranges are nested or disjoint, so a start beyond fiber's own is after it unless fiber holds it. At
        // fiber's own start the answer turns on emptiness. A fiber that held rows starts at its first row, and
        // a tenant starting there either holds that row or holds none and sits before it. One that held none
        // sits before any tenant's first row there; between two that hold none this takes the committed
        // sibling order, or the order of their Portals where they belong to two.
        private static bool IsPlacedAfter(ComponentFiber tenant, ComponentFiber fiber, bool fiberHeldNoRows)
        {
            if (ReferenceEquals(tenant, fiber)) return false;
            if (tenant.MountSlotStart < fiber.MountSlotStart || HoldsRowsOf(fiber, tenant)) return false;
            if (tenant.MountSlotStart > fiber.MountSlotStart) return true;
            if (!fiberHeldNoRows || HoldsRowsOf(tenant, fiber)) return false;
            if (tenant.MountSlotCount > 0) return true;
            // Two components of different Portals take the order those Portals' ranges take, which
            // PortalSlotTracker.IsBehind reads off the placeholders: the fiber chain need not agree with it once
            // the two were placed by different walks.
            var tenantPortal = OwningPortalOf(tenant, fiber.MountPoint);
            var fiberPortal = OwningPortalOf(fiber, fiber.MountPoint);
            if (tenantPortal != null && fiberPortal != null && !ReferenceEquals(tenantPortal, fiberPortal))
            {
                return PortalSlotTracker.PrecedesInTree(fiberPortal, tenantPortal);
            }
            return FollowsInSiblingChain(tenant, fiber);
        }

        private static bool FollowsInSiblingChain(ComponentFiber tenant, ComponentFiber fiber)
        {
            var later = tenant;
            var earlier = fiber;
            var laterDepth = Depth(later);
            var earlierDepth = Depth(earlier);
            for (; laterDepth > earlierDepth; laterDepth--) later = later.Parent!;
            for (; earlierDepth > laterDepth; earlierDepth--) earlier = earlier.Parent!;
            while (!ReferenceEquals(later.Parent, earlier.Parent))
            {
                later = later.Parent!;
                earlier = earlier.Parent!;
            }
            for (var sibling = earlier.Sibling; sibling != null; sibling = sibling.Sibling)
            {
                if (ReferenceEquals(sibling, later)) return true;
            }
            return false;
        }

        private static int Depth(ComponentFiber fiber)
        {
            var depth = 0;
            for (var ancestor = fiber.Parent; ancestor != null; ancestor = ancestor.Parent) depth++;
            return depth;
        }

        // Read off the nearest fiber of the chain that carries a placeholder, since a fiber below a Portal's
        // own child can have lost its own — ComponentFiber.OwningPortalPlaceholder owns when.
        private static VisualElement? OwningPortalOf(ComponentFiber fiber, VisualElement? mountPoint)
        {
            for (var holder = fiber; IsTenantOf(holder, mountPoint); holder = holder!.Parent)
            {
                if (holder!.OwningPortalPlaceholder != null) return holder.OwningPortalPlaceholder;
            }
            return null;
        }

        // A Portal's recorded range on mountPoint is a start of its own, read by that Portal's patch and its
        // teardown: the range holding fiber takes the change as length, and the ranges after it as start. A
        // fiber below a Portal's child but inside an element of its own carries that Portal's placeholder
        // too, and its rows are not the range's, which is what the target comparison separates.
        private static void ShiftPortalRangesAround(ComponentFiber fiber, VisualElement? mountPoint, int delta)
        {
            var portalState = fiber.Reconciler?.Context.PortalState;
            var owning = OwningPortalOf(fiber, mountPoint);
            if (portalState == null
                || owning == null
                || !portalState.TryGetValue(owning, out var range)
                || !ReferenceEquals(range.Target, mountPoint))
            {
                return;
            }
            portalState[owning] = range with { SlotLength = range.SlotLength + delta };
            PortalSlotTracker.ShiftRangesBehind(portalState, mountPoint, owning, range, delta);
        }

        // Drains parked time-sliced work before the new reconcile measures childCount. Force-draining
        // commits the remaining child-count delta (e.g. the atomic keyed reorder inserting created
        // elements). For an inline-mount fiber that delta must propagate exactly as the scheduled resume
        // (ContinueReconcile) and the initial pass do — the new reconcile measures childCount AFTER this
        // drain, so it would otherwise absorb the drain's delta and leave the starts behind it stale.
        internal static void DrainPendingWork(ComponentFiber fiber)
        {
            if (fiber.IsInlineMounted)
            {
                var drain = new RowCountWindow(fiber);
                fiber.Reconciler!.ContinueReconcile(frameBudgetMs: 0);
                PropagateInlineSlotShift(fiber, drain.UnshiftedChange);
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

        // A fiber's own render, a boundary's fallback swap and an unmount all rewrite the fiber's rows through
        // here, so the three are bounded and propagate alike. For inline-mounted fibers the slot footprint is
        // the *expanded* DOM count — newTree may include a top-level Fragment / ContextProvider whose expansion
        // produces a different number of leaves, or descendant ComponentNodes whose own subtrees contribute
        // additional VEs — so the delta is measured as the MountPoint's child count before and after the
        // Reconcile, through RowCountWindow. Wrapper-mounted fibers own their entire MountPoint and don't
        // participate in the shift.
        internal static void ReconcileOwnRows(
            ComponentFiber fiber, VNode?[] oldTree, VNode?[] newTree, double frameBudgetMs)
        {
            var reconciler = fiber.Reconciler!;
            if (!fiber.IsInlineMounted)
            {
                reconciler.Reconcile(fiber.MountPoint, oldTree, newTree, frameBudgetMs);
                return;
            }
            var rows = new RowCountWindow(fiber);
            var slotLimit = NextInlineSiblingSlotStart(fiber);
            reconciler.Reconcile(fiber.MountPoint, oldTree, newTree, frameBudgetMs, fiber.MountSlotStart, slotLimit);
            PropagateInlineSlotShift(fiber, rows.UnshiftedChange);
        }

        // When deferReconcile is true (initial inline mount), the commit is performed by the caller's parent
        // expansion rather than this fiber's own Reconciler: the newTree is captured on PreviousTree by the
        // caller and consumed by ExpandInlineRecursive, which inserts the output VEs into the parent at the
        // fiber's slot range — so the FiberRenderer-side bookkeeping is skipped here to avoid using the
        // unexpanded VNode count.
        internal static void ReconcileIntoSlotRange(
            ComponentFiber fiber, VNode?[] oldTree, VNode?[] newTree, double frameBudgetMs, bool deferReconcile)
        {
            // The array this reconcile is expanding, for the children it stamps on the way through.
            var treeContext = fiber.Reconciler?.Context;
            var enclosingFiberTree = treeContext?.CurrentFiberTree;
            if (treeContext != null) treeContext.CurrentFiberTree = newTree;
            try
            {
                if (!deferReconcile) ReconcileOwnRows(fiber, oldTree, newTree, frameBudgetMs);
            }
            finally
            {
                if (treeContext != null) treeContext.CurrentFiberTree = enclosingFiberTree;
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
