#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Element placement for the keyed/general child-diff commit: given the committed new-element list,
    // computes the Longest Increasing Subsequence of their post-removal DOM positions (the anchors that
    // stay put) and has the non-anchor elements re-placed into new order with the minimum number of moves.
    // Pulled out of ChildReconciler as its own collaborator — placement is a self-contained algorithm
    // distinct from the diff that decides WHICH elements move.
    internal sealed class ChildElementPlacement
    {
        private readonly ReconcilerBufferPool _pool;
        private readonly HostCommitQueue _host;

        public ChildElementPlacement(ReconcilerBufferPool pool, HostCommitQueue host)
        {
            _pool = pool;
            _host = host;
        }

        // The four coordinates one placement pass works in. SlotStart anchors the slot-local conversion;
        // ScanStart is the first DOM index to map, which is SlotStart + linearEnd for the keyed paths and
        // SlotStart for the general path, whose walk performs no linear prefix. OldLen and LogicalNewLen only
        // bound the scan: no more elements can be retained than existed, so the OldLen term alone already
        // covers every retained element, and LogicalNewLen is a conservative upper bound each path spells its
        // own way (newNodes.Length for the keyed paths, newElements.Count for the general one).
        internal readonly struct PlacementRange
        {
            internal int SlotStart { get; init; }
            internal int ScanStart { get; init; }
            internal int OldLen { get; init; }
            internal int LogicalNewLen { get; init; }
        }

        // Synchronous-path wrapper: rents the anchor set, computes the LIS anchors, re-places the
        // non-anchor elements, then returns the set. Shared by the synchronous keyed and general
        // commit paths. The time-sliced path cannot use this — it must keep lisIndices alive in
        // KeyedReconcileState.LisIndices across a yield boundary between ComputeLis and Pass2Reorder —
        // so it rents the set itself and calls ComputeLisAnchors directly. The reorder region begins where
        // the LIS scan does, so PlacementRange.ScanStart serves both. Returns what ReorderToNewElementOrder does.
        internal bool ComputeAnchorsAndReorder(
            VisualElement? parent,
            List<(VisualElement? element, bool isExisting)> newElements,
            in PlacementRange range)
        {
            var pool = _pool;
            var lisIndices = pool.RentIntSet();
            try
            {
                ComputeLisAnchors(parent, newElements, in range, lisIndices);
                return ReorderToNewElementOrder(parent, newElements, lisIndices, range.ScanStart);
            }
            finally
            {
                pool.ReturnIntSet(lisIndices);
            }
        }

        // Shared patience-sort LIS over the post-removal DOM positions of the committed new elements,
        // used by all three keyed-diff commit paths (synchronous keyed, time-sliced keyed, general
        // live-context). Builds a slot-local DOM-position map over the scanned range,
        // maps each isExisting new element to its current slot-local index (or -1 for a created /
        // replaced orphan), then computes the LIS of that index sequence in O(N log N). LIS members
        // are the anchors that stay put during ReorderToNewElementOrder, yielding the minimum number
        // of DOM moves.
        //
        // The anchor set is written into the caller-owned lisIndices; the caller controls its
        // lifetime (the time-sliced path keeps it in KeyedReconcileState.LisIndices across a yield
        // boundary, the synchronous paths return it in their own try/finally). Every other scratch
        // buffer is rented and returned within the call — domPosMap / domIndices / lisResult here,
        // lisAncestors / pileTop in AccumulateLisAnchors — so the call allocates nothing in steady state.
        //
        // The scan's upper bound is derived here rather than by each caller, so the childCount clamp lives
        // in one place (PlacementRange documents what the coordinates mean).
        internal void ComputeLisAnchors(
            VisualElement? parent,
            List<(VisualElement? element, bool isExisting)> newElements,
            in PlacementRange range,
            HashSet<int> lisIndices)
        {
            var slotStart = range.SlotStart;
            var pool = _pool;
            var domPosMap = pool.RentElementIndexMap();
            var domIndices = pool.RentIntList();
            // lisResult holds the minimum tail value of each patience-sort pile.
            var lisResult = pool.RentIntList();
            try
            {
                // range.ScanStart / slotStart / both lengths are LOGICAL, so the scan bounds are converted
                // before they can index the DOM (LogicalChildSlots). Physically walking from a logical
                // slotStart used to be correct only because the entry gate had already folded a leading
                // z-container's offset in; with an invisible child anywhere in the range it both under-covers
                // the retained rows — the last one then maps to -1, reads as a created orphan, and is
                // detached and re-attached on every re-render — and skews every recorded position.
                var physicalScanStart = LogicalChildSlots.ToPhysical(parent!, range.ScanStart);
                var physicalRangeEnd = Math.Min(parent!.childCount,
                    LogicalChildSlots.ToPhysical(parent!, slotStart + Math.Max(range.OldLen, range.LogicalNewLen)));
                // Slot-local positions in O(N). parent.IndexOf per element would be
                // O(childCount) x newElements.Count = O(N^2). An invisible child in the range is simply not
                // recorded, so it can never be picked as an anchor.
                var logicalPos = range.ScanStart - slotStart;
                for (var idx = physicalScanStart; idx < physicalRangeEnd; idx++)
                {
                    var child = parent!.ElementAt(idx);
                    if (SilhouetteBoundsSpacer.IsSpacer(child))
                    {
                        continue;
                    }
                    domPosMap[child] = logicalPos;
                    logicalPos++;
                }

                // Current position of each committed element, in new order.
                //   isExisting=true  retained element → its post-removal slot-local index.
                //   isExisting=false created/replaced orphan (parent == null) → -1 (always moved).
                for (var i = 0; i < newElements.Count; i++)
                {
                    var (element, isExisting) = newElements[i];
                    domIndices.Add(isExisting && element != null && domPosMap.TryGetValue(element, out var pos) ? pos : -1);
                }

                AccumulateLisAnchors(domIndices, lisResult, newElements.Count, lisIndices);
            }
            finally
            {
                pool.ReturnElementIndexMap(domPosMap);
                pool.ReturnIntList(domIndices);
                pool.ReturnIntList(lisResult);
            }
        }

        // Patience sort over domIndices, adding the LIS members to lisIndices. lisResult arrives empty and
        // leaves holding the minimum tail value of each pile; the caller owns it because it also owns the
        // rent/return of the buffers the position mapping needed.
        //
        // pileTop[k] is the index (into the new-element order) of the most recently placed element on pile
        // k, which is what gives each element its predecessor in O(1) for the reverse reconstruction.
        private void AccumulateLisAnchors(
            List<int> domIndices, List<int> lisResult, int count, HashSet<int> lisIndices)
        {
            var pool = _pool;
            var lisAncestors = pool.RentIntList();
            var pileTop = pool.RentIntList();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    lisAncestors.Add(-1);
                    pileTop.Add(-1);
                }

                for (var i = 0; i < count; i++)
                {
                    var v = domIndices[i];
                    if (v < 0) continue; // Created/replaced orphan: never an anchor.

                    // Binary search for the first pile tail strictly greater than v.
                    var lo = 0;
                    var hi = lisResult.Count;
                    while (lo < hi)
                    {
                        var mid = (lo + hi) >> 1;
                        if (lisResult[mid] < v) lo = mid + 1; else hi = mid;
                    }

                    if (lo < lisResult.Count) lisResult[lo] = v; else lisResult.Add(v);
                    lisAncestors[i] = lo > 0 ? pileTop[lo - 1] : -1;
                    pileTop[lo] = i;
                }

                // Reconstruct the LIS in reverse from the tail to fix the anchor set.
                if (lisResult.Count > 0)
                {
                    var cur = pileTop[lisResult.Count - 1];
                    while (cur >= 0)
                    {
                        lisIndices.Add(cur);
                        cur = lisAncestors[cur];
                    }
                }
            }
            finally
            {
                pool.ReturnIntList(lisAncestors);
                pool.ReturnIntList(pileTop);
            }
        }

        // Re-places the non-LIS elements so the slot range from slotStart ends up in newElements order
        // (HostCommitQueue.Reorder). Shared by the synchronous paths (the general/presence FinalizeGeneralCommit and
        // the sync keyed reconcile) and by the time-sliced keyed Pass2Reorder, which runs it as one slice once its
        // linear/remove passes have time-sliced through the create/patch work.
        // Returns whether it placed the elements out of the order the components in them were created in, which
        // ComponentRegistry.CommitChildOrderToRows then gives their fibers.
        internal bool ReorderToNewElementOrder(
            VisualElement? parent,
            System.Collections.Generic.List<(VisualElement? element, bool isExisting)> newElements,
            System.Collections.Generic.HashSet<int>? lisIndices,
            int slotStart)
        {
            if (parent == null || lisIndices == null) return false;
            // Every element is an anchor only when each was retained and already stands in new order, which is
            // also what PlacesOutOfCreationOrder answers false for; nothing is moved below either.
            if (lisIndices.Count == newElements.Count) return false;
            var outOfCreationOrder = PlacesOutOfCreationOrder(parent, newElements);
            _host.Reorder(parent, newElements, lisIndices, slotStart);
            return outOfCreationOrder;
        }

        // A kept element whose new place is ahead of one kept before it, or a created one ahead of a kept one: the
        // components inside a created element are created after those inside every kept one.
        private bool PlacesOutOfCreationOrder(
            VisualElement parent, System.Collections.Generic.List<(VisualElement? element, bool isExisting)> newElements)
        {
            var positions = _pool.RentElementIndexMap();
            try
            {
                for (var i = 0; i < parent.childCount; i++) positions[parent.ElementAt(i)] = i;
                var lastKept = -1;
                var createdSeen = false;
                foreach (var (element, isExisting) in newElements)
                {
                    if (element == null) continue;
                    if (!isExisting || !positions.TryGetValue(element, out var position))
                    {
                        createdSeen = true;
                        continue;
                    }
                    if (createdSeen || position < lastKept) return true;
                    lastKept = position;
                }
                return false;
            }
            finally
            {
                _pool.ReturnElementIndexMap(positions);
            }
        }
    }
}
