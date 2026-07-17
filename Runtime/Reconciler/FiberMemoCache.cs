using System;
using System.Collections.Generic;

namespace Velvet
{
    // Dependency-array cache for MemoNode.
    // Skips the Factory invocation and returns the cached VNode when the dependency array is unchanged.
    internal sealed class FiberMemoCache
    {
        private readonly Dictionary<string, (object?[]? deps, VNode cached)> _cache = new();

        // Convenience overload for callers that don't need the cache-hit flags.
        public VNode GetOrCompute(string cacheKey, MemoNode memo) => GetOrComputeWithHitInfo(cacheKey, memo).result;

        // Returns the cached VNode or computes a new one, along with cache-hit information.
        // Used by PatchNode to skip child-tree rebuilds on a cache hit.
        public (VNode result, bool wasHit, VNode? previousCached) GetOrComputeWithHitInfo(string cacheKey, MemoNode memo)
        {
            VNode? previousCached = null;
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (ObjectIs.AreEqualDeps(entry.deps, memo.Dependencies))
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
            _cache[cacheKey] = (memo.Dependencies, result);
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

        // Returns every cached inner tree to the VNode pool, then clears. Called at reconciler
        // disposal: the whole mounted tree is torn down with it, so there is no committed successor
        // to spare (a cached inner shared with an already-retired committed tree is a harmless
        // overlap — pool returns are idempotent).
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
