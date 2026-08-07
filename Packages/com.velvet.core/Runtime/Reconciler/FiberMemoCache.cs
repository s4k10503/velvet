using System;
using System.Collections.Generic;

namespace Velvet
{
    // Dependency-array cache for MemoNode.
    // Skips the Factory invocation and returns the cached VNode when the dependency array is unchanged, or
    // when the same node instance is expanded again.
    internal sealed class FiberMemoCache
    {
        private readonly Dictionary<string, (object?[]? deps, MemoNode node, VNode cached)> _cache = new();

        // Convenience overload for callers that don't need the cache-hit flags.
        public VNode GetOrCompute(string cacheKey, MemoNode memo) => GetOrComputeWithHitInfo(cacheKey, memo).result;

        public (VNode result, bool wasHit, VNode? previousCached) GetOrComputeWithHitInfo(string cacheKey, MemoNode memo)
        {
            VNode? previousCached = null;
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                // Two hit conditions, because a null dependency array is "no dependency array" rather than "an
                // unchanged one": AreEqualDeps answers true for two nulls, which would cache such a node for
                // its whole life. Once the deps arm stops answering for it, identity is what keeps the old-side
                // expansion off the Factory — GeneralPathReconciler.ExpandMemoInline reaches this method once
                // for the old tree and once for the new, and only the old side carries the node that produced
                // the cached inner. Drop the identity arm and the factory-call count in ReconcilerMemoTests'
                // omitted-versus-empty case goes up by one per reconcile.
                if (ReferenceEquals(entry.node, memo)
                    || (memo.Dependencies != null && ObjectIs.AreEqualDeps(entry.deps, memo.Dependencies)))
                {
                    return (entry.cached, true, null);
                }
                previousCached = entry.cached;
            }

            var result = memo.Factory();
            if (result == null)
            {
                throw new InvalidOperationException("MemoNode.Factory returned null.");
            }
            _cache[cacheKey] = (memo.Dependencies, memo, result);
            return (result, false, previousCached);
        }

        // Returns the currently cached inner VNode for cacheKey without invoking the
        // Factory. Used by FiberContextSpine to follow a committed Memo's inner while
        // reconstructing the live context cursor — a recompute there would run the user Factory and
        // mutate the cache outside a reconcile. Returns false when nothing is cached yet.
        public bool TryPeek(string cacheKey, out VNode? cached)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                cached = entry.cached;
                return true;
            }
            cached = null;
            return false;
        }

        public void Clear() => _cache.Clear();

        // Called at reconciler disposal: the whole mounted tree is torn down with it, so there is no
        // committed successor to spare (a cached inner shared with an already-retired committed tree is
        // a harmless overlap — pool returns are idempotent).
        public void DisposeAndReturnCachedTrees()
        {
            foreach (var entry in _cache.Values)
            {
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(entry.cached), owner: null);
            }
            _cache.Clear();
        }
    }
}
