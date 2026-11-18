#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Ops for the writes a reconcile makes into a container: its child list, the rows each fiber and each Portal is
    // recorded at, a Suspense's hide and reveal, and the fibers it drops. The container can be one the same walk
    // created — a new element's children reconcile through the same diff — and nothing here tells that apart from
    // a committed one. ChildReconciler, GeneralPathReconciler, ChildElementPlacement and NodePatchWalk call no
    // child-list writer of their own (HostCommitRoutingTests), though a method they call can. Each op applies where it
    // is called.
    internal sealed class HostCommitQueue
    {
        private readonly ReconcilerContext _ctx;
        private readonly FiberElementCleaner _cleaner;

        internal HostCommitQueue(ReconcilerContext ctx)
        {
            _ctx = ctx;
            _cleaner = new FiberElementCleaner(ctx);
        }

        internal void Insert(VisualElement parent, int physical, VisualElement child) => ChildListWrites.Insert(parent, physical, child);

        // Where LogicalChildSlots.ToPhysical puts a child at the slot.
        internal void InsertAtSlot(VisualElement parent, int slot, VisualElement child)
            => ChildListWrites.InsertAtSlot(parent, slot, child);

        // Takes element out of parent with everything it holds released (FiberElementCleaner). Nothing is done
        // where element is no longer parent's child.
        internal void Remove(VisualElement parent, VisualElement element)
        {
            var at = ChildListWrites.PhysicalIndexOf(parent, element);
            if (at >= 0) _cleaner.RemoveElement(parent, at);
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
        // Both lookups are by ELEMENT and are answered by the container's row model rather than by a scan of its
        // children (KeyedReorderRowReadScalingTests).
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

            for (var i = newElements.Count - 1; i >= 0; i--)
            {
                if (lisIndices.Contains(i)) continue;

                var (element, isExisting) = newElements[i];
                if (element == null) continue;
                if (isExisting && element.parent == parent)
                {
                    var removeAt = ChildListWrites.PhysicalIndexOf(parent, element);
                    if (removeAt >= 0)
                    {
                        ChildListWrites.RemoveAt(parent, removeAt);
                    }
                    else
                    {
                        // MUTANT_SURVIVES(equivalent, line removed): PhysicalIndexOf misses only an element outside the content list the insertion below indexes, and InsertCore takes the child out of wherever it stands.
                        ChildListWrites.RemoveFromHierarchy(element);
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
                    ChildListWrites.Insert(parent, ChildListWrites.PhysicalIndexOf(parent, nextSibling), element);
                }
                else
                {
                    // The physical position just after the last rendered child, so a trailing invisible
                    // child (a filter bounds spacer) stays last through the reorder — parent.Add would
                    // append past it.
                    var end = LogicalChildSlots.ToPhysical(parent, LogicalChildSlots.Count(parent));
                    ChildListWrites.Insert(parent, end, element);
                }
            }
        }

        // A Suspense's hide of one of its outermost host elements.
        internal void Hide(VisualElement element)
        {
            SuspenseHiddenElements.Hide(element);
            _ctx.DetachRefsWhileHidden(element);
        }

        internal void ApplyOffscreenChanges(List<(int Row, VisualElement Element, bool Hidden)>? displayChanges,
            List<(ComponentFiber Fiber, bool Hidden)>? offscreenChanges)
            => SuspenseOffscreenCommit.Apply(_ctx, displayChanges, offscreenChanges);

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

        // What one container's reconcile deletes, taken down before its removals (DeletionCommit): the fibers its
        // old side reached and its new side did not, whose disposal comes after them (DisposeOrphans), and the rows
        // it removes.
        internal DeletionCommit BeginDeletion(VisualElement? container, int slotStart, OldSlotOccupants occupants)
            => DeletionCommit.Begin(_ctx, _cleaner, container, slotStart, occupants);
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
