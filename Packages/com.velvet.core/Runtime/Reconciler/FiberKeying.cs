using System.Collections.Generic;
using System.Globalization;

namespace Velvet
{
    // Single source of truth for the tree-position keying conventions used when a fiber's committed
    // output is walked: position keys for unkeyed inline ComponentNodes, the Fragment / Provider /
    // Memo / Suspense / element scope chain, and the Memo / Suspense state-cache keys.
    // Two walkers traverse a fiber's committed tree and must derive identical keys for the same node:
    // ChildReconciler's inline-expansion (which mounts / diffs fibers and emits DOM) and
    // FiberContextSpine's spine-rewalk (which re-pushes the Providers enclosing a spine child
    // for an isolated re-render). They perform different actions per node but must agree bit-for-bit
    // on the derived keys — otherwise a registry lookup keyed by <c>(parentFiber, positionKey,
    // identity)</c> misses and either the spine reconstruction fails to recognize a child or a fiber's
    // state is reset. Centralizing the derivation here makes that lockstep structural: changing a
    // keying rule changes both walkers at once.
    internal static class FiberKeying
    {
        // Returns the per-identity position key for an unkeyed inline ComponentNode: the n-th
        // occurrence of identity within one reconcile scope. Unkeyed siblings are
        // matched between renders by their render order. Mutates counters (bumps the
        // per-identity count) and returns the boxed (identity, idx) ValueTuple used by the
        // registry's 3-tuple key for equality.
        //
        // The boxed token is interned per (identity, idx) via boxCache, so repeated reconciles reuse
        // one box instead of allocating a fresh one on every walk. The box is only ever compared by
        // content (the registry never reference-compares position keys), so sharing it across renders
        // and walk passes is equality-safe.
        internal static object ResolveInlinePositionKey(
            Dictionary<object, int> counters, object identity,
            Dictionary<(object identity, int index), object> boxCache)
        {
            counters.TryGetValue(identity, out var idx);
            counters[identity] = idx + 1;
            var cacheKey = (identity, idx);
            if (!boxCache.TryGetValue(cacheKey, out var boxed))
            {
                boxed = (identity, idx);
                boxCache[cacheKey] = boxed;
            }
            return boxed;
        }

        // Composes a new scope by extending parentScope with
        // contribution. The NUL byte (U+0000) delimits scope segments;
        // V.Fragment rejects keys containing NUL at the factory so scope segments cannot collide
        // with user-supplied key contents. A null parentScope means the outermost
        // keyed boundary — the contribution becomes the entire scope.
        internal static string ComposeFragmentScope(string parentScope, string contribution)
            => parentScope == null ? contribution : parentScope + "\0" + contribution;

        // The scope a FragmentNode opens for its children. A keyed Fragment establishes (or extends)
        // the scope chain. An unkeyed Fragment contributes its positional index only when an enclosing
        // keyed Fragment already established a scope; otherwise it stays scope-less and its children
        // participate in the parent's keyed/indexed list under their own keys.
        internal static string FragmentChildScope(string parentScope, string fragmentKey, int nodeIndex)
            => fragmentKey != null
                ? ComposeFragmentScope(parentScope, fragmentKey)
                : (parentScope == null ? null : ComposeFragmentScope(parentScope, Index(nodeIndex)));

        // The scope a ContextProviderNode opens for its children: null while scope-less, otherwise the
        // parent scope extended by the Provider's own key (or its positional index when unkeyed).
        internal static string ProviderChildScope(string parentScope, string providerKey, int nodeIndex)
            => parentScope == null
                ? null
                : ComposeFragmentScope(parentScope, providerKey ?? Index(nodeIndex));

        // The scope a MemoNode opens for its resolved inner. Distinct "m"-prefixed index so a
        // nested Memo's position key cannot collide with an unkeyed Component at the same node index.
        internal static string MemoScope(string parentScope, int nodeIndex)
            => ComposeFragmentScope(parentScope, "m" + Index(nodeIndex));

        // The dep-cache key for a MemoNode: its explicit key when present, otherwise its
        // MemoScope (a stable position scope, not a per-pass counter).
        internal static string MemoCacheKey(string memoKey, string memoScope)
            => memoKey ?? memoScope;

        // The boundary key for a SuspenseNode (also the ReconcilerContext.SuspenseFallbackShown
        // state key suffix): the parent scope extended by the Suspense's own key (or its positional
        // index when unkeyed).
        internal static string SuspenseKey(string parentScope, string suspenseKey, int nodeIndex)
            => ComposeFragmentScope(parentScope, suspenseKey ?? Index(nodeIndex));

        // The scoped position key for a DOM-less AnimatePresence: its parent scope extended by the
        // AnimatePresence's own key (or its positional index when unkeyed). Used with the boundary fiber
        // to key its PresenceBoundaryState, mirroring SuspenseKey.
        internal static string PresenceKey(string parentScope, string presenceKey, int nodeIndex)
            => ComposeFragmentScope(parentScope, presenceKey ?? Index(nodeIndex));

        // The scope a single keyed AnimatePresence child renders under: the AnimatePresence's own scoped
        // key extended by the child's key, so each keyed child's descendant fibers stay in a disjoint,
        // render-stable scope (the child key is stable across renders, unlike a visitation index).
        internal static string PresenceChildScope(string presenceScope, string childKey)
            => ComposeFragmentScope(presenceScope, childKey);

        // The scope a Suspense's committed subtree renders under: its boundary key extended by
        // "p" for the primary children or "f" for the fallback, keeping primary and
        // fallback fibers in disjoint scopes.
        internal static string SuspenseSubtreeScope(string suspenseKey, bool isFallback)
            => ComposeFragmentScope(suspenseKey, isFallback ? "f" : "p");

        // The scope an inline ComponentNode opens when its committed PreviousTree is descended:
        // null while scope-less, otherwise the parent scope extended by the Component's own key (or its
        // positional index when unkeyed).
        internal static string ComponentChildScope(string parentScope, string componentKey, int nodeIndex)
            => parentScope == null
                ? null
                : ComposeFragmentScope(parentScope, componentKey ?? Index(nodeIndex));

        // Invariant-culture stringification of a node index (the unkeyed scope contribution).
        internal static string Index(int nodeIndex)
            => nodeIndex.ToString(CultureInfo.InvariantCulture);
    }
}
