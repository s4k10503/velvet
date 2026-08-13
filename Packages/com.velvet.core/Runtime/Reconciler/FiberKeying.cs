#nullable enable
using System;
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
    // on the derived keys — otherwise a registry lookup misses and either the spine reconstruction
    // fails to recognize a child or a fiber's state is reset. Centralizing the derivation here makes that lockstep structural: changing a
    // keying rule changes both walkers at once.

    // How the child array a node sits in was opened. Part of the structural path below, so two arrays opened
    // at the same node index by different constructs — a Suspense's primary vs its fallback, a Memo's
    // resolved inner vs a Fragment's children — never compose to the same path.
    internal enum WalkPathKind : byte
    {
        Fragment = 1,
        Provider = 2,
        Component = 3,
        Memo = 4,
        SuspensePrimary = 5,
        SuspenseFallback = 6,
        Presence = 7,
        PresenceChild = 8,
    }

    // The two position coordinates the inline-expansion walk carries down one descent. Both are derived from
    // the same contribution at every level, by one factory per construct below, so they cannot drift apart.
    //
    // Scope is the fiber-keying scope chain, and deliberately COLLAPSES: a Fragment / Provider / Component
    // contributes nothing while no enclosing keyed boundary has established a scope, which is what keeps an
    // unkeyed subtree participating in its parent's plain keyed/indexed list. The registry and
    // FiberContextSpine depend on that exact rule.
    //
    // Path never collapses: every construct the descent passes through contributes, starting at the walk root.
    // Provider pairing needs that. Under Scope alone, a Provider nested directly inside another one is
    // indistinguishable from its parent at the top level (both null scope, both node index 0), and so are two
    // Providers at index 0 of two different unkeyed Fragments — pairing either against the wrong side's node
    // silently compares against a value that was never theirs.
    //
    // Path is a 64-bit rolling hash of those contributions rather than the composed string Scope is: composing
    // a string per level would allocate on the walk's hottest path, which today allocates nothing at all in
    // the scope-less regime. The cost of that choice is stated rather than hidden — two DIFFERENT paths that
    // hash alike pair the wrong Providers, which is usually a spurious notification but is a stale consumer
    // when the Provider wrongly paired against carries the same context and an equal value. Every
    // contribution, an explicit key's characters included, is folded into the full 64-bit accumulator, so the
    // bound is uniform: ~1e-16 for the ~100 Provider positions one walk can hold. Folding a key through its
    // 32-bit string hash instead would make sibling keys the dominant term at ~1e-6 for a 100-item keyed
    // list — and, string hashing being deterministic within a process, a colliding pair would collide on
    // every render rather than being a per-reconcile roll of the dice.
    //
    // A Provider whose path is NOT found does not guess: it falls back to the walk-order pairing that
    // predates position pairing (see GeneralPathReconciler.ProviderPairTable).
    internal readonly struct WalkPosition
    {
        internal readonly string? Scope;
        internal readonly long Path;

        internal WalkPosition(string? scope, long path)
        {
            Scope = scope;
            Path = path;
        }
    }

    // Tree position of one inline-expanded ContextProviderNode, used to pair a new-side Provider with the
    // Provider that held the same position on the old side (whose value it must be compared against to decide
    // whether consumers need notifying). Position — not order of appearance: the two sides emit different
    // numbers of Providers whenever a Suspense swaps primary for fallback or a conditional Provider appears,
    // and an order-based pairing then compares every following Provider against a stranger's value.
    //
    // Equality and hashing are spelled out rather than left to a record struct: this is a dictionary key on the
    // inline-expansion walk, hashed once per Provider per side, and the compiler-generated members route every
    // field through EqualityComparer<T>.Default — a virtual call per field that Mono does not devirtualize.
    internal readonly struct ProviderPairKey : IEquatable<ProviderPairKey>
    {
        private readonly ComponentFiber? _fiber;
        private readonly long _path;
        private readonly string? _key;
        private readonly int _index;

        internal ProviderPairKey(ComponentFiber? fiber, long path, string? key, int index)
        {
            _fiber = fiber;
            _path = path;
            _key = key;
            _index = index;
        }

        public bool Equals(ProviderPairKey other)
            => _index == other._index
                && _path == other._path
                && ReferenceEquals(_fiber, other._fiber)
                && string.Equals(_key, other._key, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ProviderPairKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _fiber?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (int)_path;
                hash = (hash * 397) ^ (int)(_path >> 32);
                hash = (hash * 397) ^ (_key?.GetHashCode() ?? 0);
                return (hash * 397) ^ _index;
            }
        }
    }

    internal static class FiberKeying
    {
        // FNV-1a 64 offset basis and prime. Chosen for being a one-multiply-per-contribution mix with no
        // table and no state beyond the accumulator, which is what keeps extending the path at every
        // recursion level off the walk's cost profile.
        private const ulong PathSeed = 14695981039346656037UL;
        private const ulong PathPrime = 1099511628211UL;
        // Folded in ahead of a key so an explicit key of "3" and a node index of 3 cannot compose alike.
        private const ulong KeyedContributionMarker = 0x9E3779B97F4A7C15UL;

        // The position an outer walk starts from. Scope-less (nothing has established a keyed boundary yet)
        // with the path accumulator at its seed.
        internal static WalkPosition WalkRoot => new(null, unchecked((long)PathSeed));

        // One level's contribution to the structural path. An explicit key replaces the positional index —
        // mirroring the Scope rule — so a keyed node keeps this ONE level's contribution when its siblings
        // shift around it. It does not pin the levels above: an unkeyed Fragment or Component that moves
        // changes the parent path, and the key below it moves with it.
        //
        // The key is folded CHARACTER BY CHARACTER into the 64-bit accumulator, not through its 32-bit
        // string hash: two keyed siblings differ only by their key's contribution, and routing that through
        // 32 bits would leave a ~1e-6 collision for a 100-item keyed list — deterministic per process, so a
        // colliding pair would mispair on every render rather than once in a blue moon.
        private static long ExtendPath(long parentPath, WalkPathKind kind, string? key, int nodeIndex)
        {
            unchecked
            {
                var hash = ((ulong)parentPath ^ (byte)kind) * PathPrime;
                if (key != null)
                {
                    hash = (hash ^ KeyedContributionMarker) * PathPrime;
                    foreach (var ch in key)
                    {
                        hash = (hash ^ ch) * PathPrime;
                    }
                }
                else
                {
                    hash = (hash ^ (uint)nodeIndex) * PathPrime;
                }
                return (long)hash;
            }
        }

        // The position a FragmentNode opens for its children.
        internal static WalkPosition FragmentChild(WalkPosition parent, string? fragmentKey, int nodeIndex)
            => new(FragmentChildScope(parent.Scope, fragmentKey, nodeIndex),
                ExtendPath(parent.Path, WalkPathKind.Fragment, fragmentKey, nodeIndex));

        // The position a ContextProviderNode opens for its children.
        internal static WalkPosition ProviderChild(WalkPosition parent, string? providerKey, int nodeIndex)
            => new(ProviderChildScope(parent.Scope, providerKey, nodeIndex),
                ExtendPath(parent.Path, WalkPathKind.Provider, providerKey, nodeIndex));

        // The position an inline ComponentNode opens when its committed PreviousTree is descended.
        internal static WalkPosition ComponentChild(WalkPosition parent, string? componentKey, int nodeIndex)
            => new(ComponentChildScope(parent.Scope, componentKey, nodeIndex),
                ExtendPath(parent.Path, WalkPathKind.Component, componentKey, nodeIndex));

        // The position a MemoNode opens for its resolved inner. The memo's own key selects the dep-cache
        // entry (MemoCacheKey), not the position, so only the node index contributes here.
        internal static WalkPosition MemoInner(WalkPosition parent, int nodeIndex)
            => new(MemoScope(parent.Scope, nodeIndex),
                ExtendPath(parent.Path, WalkPathKind.Memo, null, nodeIndex));

        // The position a Suspense's primary or fallback subtree renders under. The two branches contribute
        // different kinds, so a Provider in the fallback is never paired against one in the primary.
        internal static WalkPosition SuspenseSubtree(
            WalkPosition parent, string suspenseKey, string? ownKey, int nodeIndex, bool isFallback)
            => new(SuspenseSubtreeScope(suspenseKey, isFallback),
                ExtendPath(parent.Path,
                    isFallback ? WalkPathKind.SuspenseFallback : WalkPathKind.SuspensePrimary,
                    ownKey, nodeIndex));

        // The position of a DOM-less AnimatePresence itself; its Scope doubles as the boundary's state key.
        internal static WalkPosition Presence(WalkPosition parent, string? presenceKey, int nodeIndex)
            => new(PresenceKey(parent.Scope, presenceKey, nodeIndex),
                ExtendPath(parent.Path, WalkPathKind.Presence, presenceKey, nodeIndex));

        // The position one keyed AnimatePresence child renders under. Keyed by the child's own key, which is
        // stable across renders, so a Provider inside it pairs across a reorder.
        internal static WalkPosition PresenceChild(WalkPosition presence, string? childKey)
            => new(PresenceChildScope(presence.Scope, childKey),
                ExtendPath(presence.Path, WalkPathKind.PresenceChild, childKey, 0));

        // The position of a ContextProviderNode found at nodeIndex of the child array currently being walked
        // under fiber. An explicit key replaces the positional index so a keyed Provider keeps its identity
        // when siblings shift around it.
        internal static ProviderPairKey ProviderPosition(
            ComponentFiber? fiber, WalkPosition position, string? providerKey, int nodeIndex)
            => new(fiber, position.Path, providerKey, providerKey != null ? -1 : nodeIndex);

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
        internal static string ComposeFragmentScope(string? parentScope, string contribution)
            => parentScope == null ? contribution : parentScope + "\0" + contribution;

        // The scope a FragmentNode opens for its children. A keyed Fragment establishes (or extends)
        // the scope chain. An unkeyed Fragment contributes its positional index only when an enclosing
        // keyed Fragment already established a scope; otherwise it stays scope-less and its children
        // participate in the parent's keyed/indexed list under their own keys.
        internal static string? FragmentChildScope(string? parentScope, string? fragmentKey, int nodeIndex)
            => fragmentKey != null
                ? ComposeFragmentScope(parentScope, fragmentKey)
                : (parentScope == null ? null : ComposeFragmentScope(parentScope, Index(nodeIndex)));

        // The scope a ContextProviderNode opens for its children: null while scope-less, otherwise the
        // parent scope extended by the Provider's own key (or its positional index when unkeyed).
        internal static string? ProviderChildScope(string? parentScope, string? providerKey, int nodeIndex)
            => parentScope == null
                ? null
                : ComposeFragmentScope(parentScope, providerKey ?? Index(nodeIndex));

        // The scope a MemoNode opens for its resolved inner. Distinct "m"-prefixed index so a
        // nested Memo's position key cannot collide with an unkeyed Component at the same node index.
        internal static string MemoScope(string? parentScope, int nodeIndex)
            => ComposeFragmentScope(parentScope, "m" + Index(nodeIndex));

        // The dep-cache key for a MemoNode: its explicit key when present, otherwise its
        // MemoScope (a stable position scope, not a per-pass counter).
        internal static string MemoCacheKey(string? memoKey, string memoScope)
            => memoKey ?? memoScope;

        // The boundary key for a SuspenseNode (also the position key its
        // ReconcilerContext.SetSuspenseFallbackShown entry is stored under): the parent scope extended
        // by the Suspense's own key (or its positional index when unkeyed).
        internal static string SuspenseKey(string? parentScope, string? suspenseKey, int nodeIndex)
            => ComposeFragmentScope(parentScope, suspenseKey ?? Index(nodeIndex));

        // The scoped position key for a DOM-less AnimatePresence: its parent scope extended by the
        // AnimatePresence's own key (or its positional index when unkeyed). Used with the boundary fiber
        // to key its PresenceBoundaryState, mirroring SuspenseKey.
        internal static string PresenceKey(string? parentScope, string? presenceKey, int nodeIndex)
            => ComposeFragmentScope(parentScope, presenceKey ?? Index(nodeIndex));

        // The scope a single keyed AnimatePresence child renders under: the AnimatePresence's own scoped
        // key extended by the child's key, so each keyed child's descendant fibers stay in a disjoint,
        // render-stable scope (the child key is stable across renders, unlike a visitation index).
        internal static string PresenceChildScope(string? presenceScope, string? childKey)
            => ComposeFragmentScope(presenceScope, childKey ?? string.Empty);

        // The scope a Suspense's committed subtree renders under: its boundary key extended by
        // "p" for the primary children or "f" for the fallback, keeping primary and
        // fallback fibers in disjoint scopes.
        internal static string SuspenseSubtreeScope(string suspenseKey, bool isFallback)
            => ComposeFragmentScope(suspenseKey, isFallback ? "f" : "p");

        // The scope an inline ComponentNode opens when its committed PreviousTree is descended:
        // null while scope-less, otherwise the parent scope extended by the Component's own key (or its
        // positional index when unkeyed).
        internal static string? ComponentChildScope(string? parentScope, string? componentKey, int nodeIndex)
            => parentScope == null
                ? null
                : ComposeFragmentScope(parentScope, componentKey ?? Index(nodeIndex));

        // Invariant-culture stringification of a node index (the unkeyed scope contribution).
        internal static string Index(int nodeIndex)
            => nodeIndex.ToString(CultureInfo.InvariantCulture);
    }
}
