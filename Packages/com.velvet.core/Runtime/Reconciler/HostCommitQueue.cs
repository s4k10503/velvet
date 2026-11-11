#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Ops for the writes a reconcile makes into a container: its child list, the rows each fiber and each Portal is
    // recorded at, a Suspense's hide and reveal, and the fibers it drops. The container can be one the same walk
    // created — a new element's children reconcile through the same diff — and nothing here tells that apart from
    // a committed one. ChildReconciler, GeneralPathReconciler and ChildElementPlacement call no child-list writer of
    // their own (HostCommitRoutingTests), though a method they call can. Each op applies where it is called.
    internal sealed class HostCommitQueue
    {
        private readonly ReconcilerContext _ctx;
        private readonly FiberElementCleaner _cleaner;

        internal HostCommitQueue(ReconcilerContext ctx)
        {
            _ctx = ctx;
            _cleaner = new FiberElementCleaner(ctx);
        }

        // At the index LogicalSlotCursor resolved for the child's slot.
        internal void Insert(VisualElement parent, int physical, VisualElement child) => parent.Insert(physical, child);

        // Takes element out of parent with everything it holds released (FiberElementCleaner). Nothing is done
        // where element is no longer parent's child.
        internal void Remove(VisualElement parent, VisualElement element, int near)
        {
            var at = IndexNear(parent, element, near);
            if (at >= 0) _cleaner.RemoveElement(parent, at);
        }

        // parent.IndexOf(element), searched outward from near, the index the caller last knew the element at, so a
        // removal costs how far the element has moved since rather than its index. IndexOfNear gives up after a short
        // window because its seed is another element's position; this one's is the element's own.
        private static int IndexNear(VisualElement parent, VisualElement element, int near)
        {
            var count = parent.childCount;
            if (count == 0) return -1;
            var at = System.Math.Clamp(near, 0, count - 1);
            if (ReferenceEquals(parent[at], element)) return at;
            for (int below = at - 1, above = at + 1; below >= 0 || above < count; below--, above++)
            {
                if (below >= 0 && ReferenceEquals(parent[below], element)) return below;
                if (above < count && ReferenceEquals(parent[above], element)) return above;
            }
            return -1;
        }

        // Re-places the non-LIS elements so the slot range [slotStart, slotStart + count) ends up in
        // newElements order, walking RIGHT→LEFT and inserting each moved element immediately BEFORE the element
        // that newElements says is its right neighbour (newElements[i + 1], already placed by this point;
        // for the right-most element, the first sibling after the range). An absolute-index insert
        // (parent.Insert(slotStart + i, e)) is only correct when the untouched LIS anchors already sit at their
        // target slots; for a rotated list — where an anchor occupies the WRONG absolute slot — it drops a
        // moved element at the wrong place and swaps a neighbouring pair. Anchoring on the actual neighbour
        // element is order-faithful regardless of where the anchors physically are, so the element backing each
        // key stays stable across renders — which a keyed AnimatePresence relies on (an exit animation is armed
        // against one element instance; churning the element under a ghost key strands its exit and leaks the
        // ghost).
        //
        // Both DOM lookups are by ELEMENT, not index, so each move would naively cost a parent.IndexOf scan
        // from zero — O(childCount) per move, O(N²) over a full rotation where almost every element moves (LIS
        // size 1). The detach side has the same hidden cost: RemoveFromHierarchy scans for the element's index
        // internally. <see cref="IndexOfNear"/> seeds each lookup from a running hint: the next move's right
        // neighbour is the element just placed, and for the structured rotations that drive this path (reverse /
        // sort-flip / carousel) consecutive removals also cluster, so both lookups resolve in O(1) — collapsing
        // the scan term to O(N) for those shapes (the measured win). An unstructured shuffle has no such locality;
        // there IndexOfNear falls back to a plain scan, no worse than the IndexOf / RemoveFromHierarchy it replaced.
        // The residual O(N·childCount) of the incremental Insert/RemoveAt shifts themselves is unchanged — only the
        // scan term is removed; the reorder still runs as one unyielded slice, so a huge single-frame rotation is
        // bounded by that structural cost, not by this lookup.
        internal void Reorder(
            VisualElement parent,
            List<(VisualElement? element, bool isExisting)> newElements,
            HashSet<int> lisIndices,
            int slotStart)
        {
            // The element that follows this whole range in the LIVE tree — the anchor for the range's right-most
            // element. The range currently occupies a contiguous block of its still-parented elements starting at
            // slotStart (Pass 2 Remove compacted out the dropped ones; freshly created elements are NOT yet
            // inserted), so the next sibling sits just past those. Deriving the boundary from the LIVE count, not
            // newElements.Count, is what keeps the walk correct whether the reordered elements are already parented
            // (the synchronous paths, where the count equals newElements.Count) or freshly created and still
            // detached (the time-sliced keyed path, where they are not in the parent yet, so using the full count
            // would overshoot past a following sibling's rows and append the range to the very end). Captured once
            // before any insertion — it is only ever the right-most element's anchor, and inserting before it never
            // moves it out of the range's tail.
            var liveCount = 0;
            foreach (var (element, _) in newElements)
            {
                if (element != null && element.parent == parent) liveCount++;
            }
            // The first element PAST this range, by logical slot: the anchor everything in the range is
            // inserted before. Converted, because a leading z-container or an invisible child inside the
            // range shifts the physical position of that slot — reading it physically picked the range's own
            // last element as its anchor and inserted the tail before it, inverting the declared order.
            var afterLogical = slotStart + liveCount;
            var afterIndex = LogicalChildSlots.ToPhysical(parent, afterLogical);
            var afterRangeAnchor = afterLogical < LogicalChildSlots.Count(parent)
                ? parent.ElementAt(afterIndex)
                : null;

            // Search hints for IndexOfNear: the last insertion lands the right neighbour for the next move, and
            // consecutive removals cluster, so seeding each search from the previous result keeps it O(1) on the
            // common path.
            var insertHint = afterIndex;
            var removeHint = slotStart;

            for (var i = newElements.Count - 1; i >= 0; i--)
            {
                if (lisIndices.Contains(i)) continue;

                var (element, isExisting) = newElements[i];
                if (element == null) continue;
                if (isExisting && element.parent == parent)
                {
                    // Equivalent to element.RemoveFromHierarchy() but skips its internal scan-from-zero by
                    // locating the element near the previous removal.
                    var removeAt = IndexOfNear(parent, element, removeHint);
                    if (removeAt >= 0)
                    {
                        removeHint = removeAt;
                        parent.RemoveAt(removeAt);
                    }
                    else
                    {
                        element.RemoveFromHierarchy();
                    }
                }

                var nextSibling = i + 1 < newElements.Count ? newElements[i + 1].element : afterRangeAnchor;
                // A non-right-most element whose declared right neighbour is not in the parent is only reachable
                // on malformed input (e.g. a duplicate key orphaned the neighbour). Appending to the absolute end
                // would drop it past a following tenant's rows; fall back to the range's right boundary so it
                // stays within this fiber's slot range.
                if (nextSibling != null && nextSibling.parent != parent)
                {
                    nextSibling = afterRangeAnchor != null && afterRangeAnchor.parent == parent ? afterRangeAnchor : null;
                }

                if (nextSibling != null)
                {
                    var pos = IndexOfNear(parent, nextSibling, insertHint);
                    parent.Insert(pos, element);
                    insertHint = pos;
                }
                else
                {
                    // The physical position just after the last rendered child, so a trailing invisible
                    // child (a filter bounds spacer) stays last through the reorder — parent.Add would
                    // append past it.
                    var end = LogicalChildSlots.ToPhysical(parent, LogicalChildSlots.Count(parent));
                    parent.Insert(end, element);
                    insertHint = end;
                }
            }
        }

        // parent.IndexOf(target) that searches OUTWARD from a hint instead of from index 0. A VisualElement
        // appears in its parent at most once, so the nearest match is THE match — the result equals
        // parent.IndexOf(target). ElementAt is an O(1) list index, so when the hint has locality (the structured
        // reorder shapes that drive this path — reverse / sort-flip / carousel — place each element at or beside
        // the hint) the lookup resolves in O(1). Beyond a short window the hint has no locality (an unstructured
        // shuffle's removal order does not track DOM position), so rather than spiral outward across the whole
        // range — which, scanning BOTH directions, would cost ~2x a plain scan — it falls back to parent.IndexOf.
        // That bounds the worst case to within a small constant of IndexOf (never the 2x blow-up). Returns -1 only
        // when target is not a child (callers guard that case).
        private static int IndexOfNear(VisualElement parent, VisualElement target, int hint)
        {
            var n = parent.childCount;
            if (n == 0) return -1;
            if (hint < 0) hint = 0; else if (hint >= n) hint = n - 1;
            const int window = 8;
            for (var d = 0; d <= window; d++)
            {
                var hi = hint + d;
                if (hi < n && parent.ElementAt(hi) == target) return hi;
                if (d > 0)
                {
                    var lo = hint - d;
                    if (lo >= 0 && parent.ElementAt(lo) == target) return lo;
                }
            }
            return parent.IndexOf(target);
        }

        // A Suspense's hide of one of its outermost host elements, and its reveal. releaseFocus lets go of a focus
        // inside the element as the hide lands, which UI Toolkit does only at its next layout pass
        // (SuspenseHiddenFocusPanelTests).
        internal void Hide(VisualElement element, bool releaseFocus)
        {
            SuspenseHiddenElements.Hide(element);
            _ctx.DetachRefsWhileHidden(element);
            if (!releaseFocus) return;
            var focused = element.focusController?.focusedElement as VisualElement;
            if (SuspenseHiddenElements.IsAtOrUnderHidden(focused)) focused!.Blur();
        }

        internal void Unhide(VisualElement element)
        {
            SuspenseHiddenElements.Reveal(element);
            _ctx.AttachRefsHiddenUnder(element);
        }

        // Where a walk placed a component's rows.
        internal void PlaceRows(ComponentFiber fiber, int slotStart, int count)
        {
            fiber.MountSlotStart = slotStart;
            fiber.MountSlotCount = count;
        }

        // A change in the number of rows an inline fiber holds, and the slots it moves (FiberCommitWork).
        internal void ShiftRows(ComponentFiber fiber, int delta) => FiberCommitWork.PropagateInlineSlotShift(fiber, delta);

        // A change in the length of a Portal's range on target, and the ranges and fibers behind it.
        internal void ShiftBehindPortalRange(VisualElement target, VisualElement placeholder, PortalSlotInfo changed, int delta)
        {
            PortalSlotTracker.ShiftRangesBehind(_ctx.PortalState.Stored, target, placeholder, changed, delta);
            FiberCommitWork.ShiftTenantsAfterPortalRange(_ctx.ComponentRegistry, target, placeholder, changed, delta);
        }

        // A change in the number of target's own rows, which every range on target follows.
        internal void FollowOwnRows(VisualElement target, int rowsBehindBefore)
            => FiberCommitWork.FollowOwnRows(_ctx, target, rowsBehindBefore);

        // The fibers one container's old side reached and its new side did not: their effect cleanups run before
        // the container's removals, and their disposal after.
        internal void CleanUpOrphans(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers)
        {
            if (oldFibers.Count == 0) return;
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) fiber.MarkBeingDeleted();
            }
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) fiber.RecordTheNearestAncestorOutsideTheDeletion();
            }
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) FiberEffects.RunOrphanFiberEffectCleanups(fiber);
            }
        }

        internal void DisposeOrphans(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers)
        {
            if (oldFibers.Count == 0) return;
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) _ctx.ComponentRegistry.DisposeAndRemove(fiber);
            }
        }
    }
}
