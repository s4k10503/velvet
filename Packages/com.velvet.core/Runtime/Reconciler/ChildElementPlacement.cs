using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Element placement for the keyed/general child-diff commit: given the committed new-element list,
    // computes the Longest Increasing Subsequence of their post-removal DOM positions (the anchors that
    // stay put) and re-places the non-anchor elements into new order with the minimum number of moves.
    // Pulled out of ChildReconciler as its own collaborator — placement is a self-contained algorithm
    // (depends only on the buffer pool) distinct from the diff that decides WHICH elements move.
    internal sealed class ChildElementPlacement
    {
        private readonly ReconcilerBufferPool _pool;

        public ChildElementPlacement(ReconcilerBufferPool pool) => _pool = pool;

        // Synchronous-path wrapper: rents the anchor set, computes the LIS anchors, re-places the
        // non-anchor elements, then returns the set. Shared by the synchronous keyed and general
        // commit paths. The time-sliced path cannot use this — it must keep lisIndices alive in
        // KeyedReconcileState.LisIndices across a yield boundary between ComputeLis and Pass2Reorder —
        // so it rents the set itself and calls ComputeLisAnchors directly. slotStart is the slot range
        // origin used for slot-local coordinate conversion; regionStart is both the LIS scan start and
        // the reorder region start. They differ for the sync keyed path (regionStart = slotStart +
        // linearEnd) and coincide for the general path (regionStart = slotStart, linearEnd == 0).
        internal void ComputeAnchorsAndReorder(
            VisualElement parent,
            List<(VisualElement element, bool isExisting)> newElements,
            int slotStart,
            int regionStart,
            int oldLen,
            int logicalNewLen)
        {
            var pool = _pool;
            var lisIndices = pool.RentIntSet();
            try
            {
                ComputeLisAnchors(parent, newElements, slotStart, regionStart, oldLen, logicalNewLen, lisIndices);
                ReorderToNewElementOrder(parent, newElements, lisIndices, regionStart);
            }
            finally
            {
                pool.ReturnIntSet(lisIndices);
            }
        }

        // Shared patience-sort LIS over the post-removal DOM positions of the committed new elements,
        // used by all three keyed-diff commit paths (synchronous keyed, time-sliced keyed, general
        // live-context). Builds a slot-local DOM-position map over [scanStart, rangeEndExclusive),
        // maps each isExisting new element to its current slot-local index (or -1 for a created /
        // replaced orphan), then computes the LIS of that index sequence in O(N log N). LIS members
        // are the anchors that stay put during ReorderToNewElementOrder, yielding the minimum number
        // of DOM moves.
        //
        // The anchor set is written into the caller-owned lisIndices; the caller controls its
        // lifetime (the time-sliced path keeps it in KeyedReconcileState.LisIndices across a yield
        // boundary, the synchronous paths return it in their own try/finally). Every other scratch
        // buffer (domPosMap / domIndices / lisResult / lisAncestors / pileTop) is rented and returned
        // here, so the call allocates nothing in steady state.
        //
        // slotStart anchors the slot-local coordinate conversion; scanStart is the first DOM index to
        // map (slotStart + linearEnd for the keyed paths, slotStart for the general path). The scan
        // upper bound — Min(childCount, slotStart + Max(oldLen, logicalNewLen)) — is derived here so the
        // childCount clamp lives in one place. The oldLen term alone already bounds the range that
        // matters: no more elements can be retained than existed, so every isExisting element sits below
        // slotStart + oldLen; created elements are orphans (parent == null) and are never looked up.
        // logicalNewLen is therefore a conservative upper bound whose exact value is not correctness-
        // critical; each path passes its own (newNodes.Length for the keyed paths, newElements.Count for
        // the general path, where linearEnd == 0) to preserve the original per-path clamp exactly.
        internal void ComputeLisAnchors(
            VisualElement parent,
            List<(VisualElement element, bool isExisting)> newElements,
            int slotStart,
            int scanStart,
            int oldLen,
            int logicalNewLen,
            HashSet<int> lisIndices)
        {
            var pool = _pool;
            var domPosMap = pool.RentElementIndexMap();
            var domIndices = pool.RentIntList();
            // lisResult holds the minimum tail value of each patience-sort pile.
            var lisResult = pool.RentIntList();
            try
            {
                // The retained range never exceeds max(oldLen, logicalNewLen) entries; clamp by
                // childCount as a safety net while staying within this fiber's slot range.
                var rangeEndExclusive = Math.Min(parent.childCount, slotStart + Math.Max(oldLen, logicalNewLen));
                // Slot-local DOM positions in O(N). parent.IndexOf per element would be
                // O(childCount) x newElements.Count = O(N^2).
                for (var idx = scanStart; idx < rangeEndExclusive; idx++)
                {
                    domPosMap[parent.ElementAt(idx)] = idx - slotStart;
                }

                // Current position of each committed element, in new order.
                //   isExisting=true  retained element → its post-removal slot-local index.
                //   isExisting=false created/replaced orphan (parent == null) → -1 (always moved).
                for (var i = 0; i < newElements.Count; i++)
                {
                    var (element, isExisting) = newElements[i];
                    domIndices.Add(isExisting && domPosMap.TryGetValue(element, out var pos) ? pos : -1);
                }

                // pileTop[k] is the index (into newElements) of the most recently placed element on
                // pile k, giving each element's predecessor in O(1) for the reverse reconstruction.
                var lisAncestors = pool.RentIntList();
                var pileTop = pool.RentIntList();
                try
                {
                    for (var i = 0; i < newElements.Count; i++)
                    {
                        lisAncestors.Add(-1);
                        pileTop.Add(-1);
                    }

                    for (var i = 0; i < newElements.Count; i++)
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
            finally
            {
                pool.ReturnElementIndexMap(domPosMap);
                pool.ReturnIntList(domIndices);
                pool.ReturnIntList(lisResult);
            }
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
        // ghost). Shared by the synchronous paths (the general/presence FinalizeGeneralCommit and the sync
        // keyed reconcile) and by the time-sliced keyed Pass2Reorder, which runs this whole walk as one
        // atomic slice once its linear/remove passes have time-sliced through the create/patch work.
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
        internal void ReorderToNewElementOrder(
            VisualElement parent,
            System.Collections.Generic.List<(VisualElement element, bool isExisting)> newElements,
            System.Collections.Generic.HashSet<int> lisIndices,
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
                if (element.parent == parent) liveCount++;
            }
            var afterIndex = slotStart + liveCount;
            var afterRangeAnchor = afterIndex < parent.childCount ? parent.ElementAt(afterIndex) : null;

            // Search hints for IndexOfNear: the last insertion lands the right neighbour for the next move, and
            // consecutive removals cluster, so seeding each search from the previous result keeps it O(1) on the
            // common path.
            var insertHint = afterIndex;
            var removeHint = slotStart;

            for (var i = newElements.Count - 1; i >= 0; i--)
            {
                if (lisIndices.Contains(i)) continue;

                var (element, isExisting) = newElements[i];
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
                    parent.Add(element);
                    insertHint = parent.childCount - 1;
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
    }
}
