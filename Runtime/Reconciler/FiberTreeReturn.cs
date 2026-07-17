using System;
using System.Collections.Generic;

namespace Velvet
{
    // Returns a committed VNode tree (and the inline old trees deferred during a reconcile pass)
    // to the VNode pool. Extracted from the render core: NormalizeToArray shapes a render body's
    // output, ReturnRetiredTree recycles a retired tree, and DrainDeferredInlineOldTreeReturns
    // flushes the deferred inline baselines at the top-level reconcile boundary.
    //
    // Recycling is a mark-and-sweep over the RETIRED tree: the mark pass collects every node still
    // reachable from committed state, and the sweep returns the retired tree's pooled objects
    // RECURSIVELY, sparing marked subtrees. Two forces make both halves necessary:
    //   - Depth: factory-rented props bags / event arrays / child arrays sit at every nesting
    //     level (and under Portal / WorldSpace / Suspense / provider boundaries — those are
    //     logical boundaries, not recycle boundaries), so a top-level-only return strands one
    //     bag per render for every nested renting element.
    //   - Sharing: memoization legitimately carries the SAME node instances across consecutive
    //     trees, so an unmarked recursive return would recycle objects the committed tree still
    //     reads as its patch baseline — and the pool would hand them to an unrelated mount to
    //     overwrite.
    //
    // The live-mark roots, per retirement:
    //   - the owner fiber's committed PreviousTree and parked PendingOldTree;
    //   - hook-slot-held node roots (auto-memo slots, Hooks.UseMemo / UseState / UseRef values
    //     that are a VNode or a list of VNodes) of the owner and its LOGICAL ancestor chain — a
    //     detached mount (portal / world-space drain) parents off the drain anchor, so the chain
    //     hops through DetachedMountContext.LogicalParent to reach the declaring components whose
    //     slots feed nodes in as props;
    //   - AnimatePresence bookkeeping (PresenceStates' committed entries): an exiting ghost's
    //     node outlives the tree that last emitted it, staying the old-side baseline until the
    //     exit finishes;
    //   - every parked PendingOldTree in the context: a paused time-sliced pass keeps reading its
    //     baseline across frames, so no other fiber's retirement may recycle nodes it shares.
    //
    // Outside those roots, holding a factory-built node across renders is not tracked: a node
    // carried inside a composite (a user record / tuple in a memo or state slot, a component
    // props record, a Store) re-enters later renders with pooled parts the sweep may have
    // recycled. Cache nodes directly in a UseMemo / UseState / UseRef slot (as the node or a
    // list of nodes) — those slots the mark pass reads.
    internal static class FiberTreeReturn
    {
        internal static VNode?[] NormalizeToArray(VNode node)
        {
            if (node == null)
            {
                return Array.Empty<VNode>();
            }

            if (node is FragmentNode fragment)
            {
                return fragment.Children ?? Array.Empty<VNode>();
            }

            return new[] { node };
        }

        // Returns the inline old trees queued by RenderInlineForExpansion during a reconcile
        // pass to the VNode pool. Called once at the top-level reconcile boundary (after the whole pass
        // has finished reading them as patch baselines) so the deferral does not add GC pressure — the
        // nodes are pooled for the next pass, just not mid-pass. See
        // ReconcilerContext.DeferredInlineOldTreeReturns for why the return must be deferred.
        // All entries share one mark pass (their owners' chains overlap heavily — K inline children
        // under one parent would otherwise re-mark the same ancestors K times), then sweep against the
        // union. Union marking only ever spares MORE, and an over-spared dead object is ordinary GC
        // garbage, so correctness is one-sided.
        internal static void DrainDeferredInlineOldTreeReturns(ReconcilerContext ctx)
        {
            var queue = ctx.DeferredInlineOldTreeReturns;
            if (queue.Count == 0) return;
            var live = AcquireLiveMarks();
            try
            {
                for (var i = 0; i < queue.Count; i++)
                {
                    MarkOwnerRoots(queue[i].Owner, live);
                }
                for (var i = 0; i < queue.Count; i++)
                {
                    SweepTree(queue[i].Tree, live);
                }
            }
            finally
            {
                ReleaseLiveMarks(live);
                queue.Clear();
            }
        }

        // Returns retired's pooled objects (props bags, single-event arrays, node arrays) to
        // VNodePool, recursing through every child-bearing node kind, while sparing nodes still
        // reachable from the live-mark roots (see the header). Pass a null owner only when the
        // whole tree is being discarded with no committed successor and no owning fiber left (a
        // memo cache torn down at reconciler disposal) — the sweep then returns everything.
        // Returns are idempotent (rent-scoped pool ownership) and pass-deferred (VNodePool release
        // staging), so a node reachable through several retired trees recycles exactly once and is
        // never re-rented within the pass that retired it.
        internal static void ReturnRetiredTree(VNode?[]? retired, ComponentFiber? owner)
        {
            if (retired == null || retired.Length == 0) return;

            var live = AcquireLiveMarks();
            try
            {
                MarkOwnerRoots(owner, live);
                SweepTree(retired, live);
            }
            finally
            {
                ReleaseLiveMarks(live);
            }
        }

        // Unmount-time variant: retires the fiber's committed tree, its parked baseline, and the
        // node roots its own (about-to-be-disposed) hook slots held — a slot-held node that is
        // currently toggled OUT of the output is in no retiring tree, so without this it would
        // strand when the slot lists are cleared. The caller passes the roots collected BEFORE
        // slot disposal and calls this AFTER it, so the mark reduces to the surviving ancestors'
        // roots (nodes that flowed into this fiber's trees as props and outlive it).
        internal static void ReturnRetiredTreesForUnmount(
            ComponentFiber fiber, VNode?[]? retiredTree, VNode?[]? parkedTree, List<VNode>? slotRoots)
        {
            if (retiredTree == null && parkedTree == null && (slotRoots == null || slotRoots.Count == 0))
            {
                return;
            }

            var live = AcquireLiveMarks();
            try
            {
                MarkOwnerRoots(fiber, live);
                if (retiredTree != null) SweepTree(retiredTree, live);
                if (parkedTree != null) SweepTree(parkedTree, live);
                if (slotRoots != null)
                {
                    for (var i = 0; i < slotRoots.Count; i++)
                    {
                        SweepNode(slotRoots[i], live);
                    }
                }
            }
            finally
            {
                ReleaseLiveMarks(live);
            }
        }

        // Collects the node roots held by the fiber's DISPOSABLE hook slots — the memo / ref slot
        // lists Unmount clears — for ReturnRetiredTreesForUnmount. State slots are deliberately
        // excluded: Unmount preserves them for a remount, so their held nodes stay live (and the
        // mark, which reads the still-populated state slots, would spare them anyway). Allocates
        // only when a slot actually holds a node — unmount-path-only cost.
        internal static List<VNode>? CollectSlotRootsForUnmount(ComponentFiber fiber)
        {
            List<VNode>? roots = null;
            VisitDisposableSlotRoots(fiber, root => CollectRootNodes(root, ref roots));
            return roots;
        }

        // Collects the node roots held by the fiber's PERSISTENT (state / reducer) slots, for the
        // terminal Dispose path: unlike Unmount, Dispose never remounts, so the caller clears the
        // state slots and retires these roots — otherwise an element-in-state fiber would pin its
        // node's pooled parts forever.
        internal static List<VNode>? CollectStateSlotRootsForDispose(ComponentFiber fiber)
        {
            List<VNode>? roots = null;
            VisitPersistentSlotRoots(fiber, root => CollectRootNodes(root, ref roots));
            return roots;
        }

        private static void CollectRootNodes(object root, ref List<VNode>? roots)
        {
            switch (root)
            {
                case VNode node:
                    (roots ??= new List<VNode>()).Add(node);
                    break;
                case IReadOnlyList<VNode?> list:
                    for (var i = 0; i < list.Count; i++)
                    {
                        var node = list[i];
                        if (node != null) (roots ??= new List<VNode>()).Add(node);
                    }
                    break;
            }
        }

        #region live marks

        // Reused mark set, reference-identity keyed (VNodes and pooled parts have no equality
        // overrides, but the default comparer still virtual-dispatches; reference hashing is
        // branch-free). Main-thread only, like VNodePool. The in-use flag makes an unexpected
        // nested retirement fall back to a fresh set instead of clearing the outer sweep's marks
        // mid-walk — neither pass runs user code today, so the fallback is a structural guarantee
        // rather than a hot path.
        private static readonly HashSet<object> s_liveMarks = new(ReferenceComparer.Instance);
        private static bool s_liveMarksInUse;

        // A mark from one huge committed tree would otherwise pin its bucket storage for the
        // process lifetime; past this count the set is trimmed after the sweep.
        private const int LiveMarksTrimThreshold = 4096;

        private static HashSet<object> AcquireLiveMarks()
        {
            if (s_liveMarksInUse)
            {
                return new HashSet<object>(ReferenceComparer.Instance);
            }
            s_liveMarksInUse = true;
            return s_liveMarks;
        }

        private static void ReleaseLiveMarks(HashSet<object> live)
        {
            if (!ReferenceEquals(live, s_liveMarks)) return;
            var oversized = live.Count > LiveMarksTrimThreshold;
            live.Clear();
            if (oversized)
            {
                live.TrimExcess();
            }
            s_liveMarksInUse = false;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new();
            bool IEqualityComparer<object>.Equals(object? a, object? b) => ReferenceEquals(a, b);
            int IEqualityComparer<object>.GetHashCode(object o)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        // Marks everything committed state can reach from this owner: its two tree baselines, the
        // slot roots along its logical ancestor chain, and the context-wide registries (presence
        // ghosts, parked baselines). Safe to call for several owners into one set (union mark);
        // already-marked subtrees short-circuit, so overlapping chains cost one walk.
        private static void MarkOwnerRoots(ComponentFiber? owner, HashSet<object> live)
        {
            if (owner == null) return;

            WalkTree(owner.PreviousTree, live, WalkMode.Mark);
            WalkTree(owner.PendingOldTree, live, WalkMode.Mark);

            for (var fiber = owner; fiber != null; fiber = LogicalParentOf(fiber))
            {
                MarkFiberSlotRoots(fiber, live);
            }

            var ctx = owner.Reconciler?.Context;
            if (ctx == null) return;

            // Exiting AnimatePresence ghosts: PresenceBoundaryState.Committed keeps re-emitting the
            // removed child's node as the old-side baseline until its exit animation completes, long
            // after the tree that last contained it retired.
            foreach (var state in ctx.PresenceStates.Values)
            {
                var committed = state.Committed;
                for (var i = 0; i < committed.Count; i++)
                {
                    MarkNode(committed[i].node, live);
                }
            }

            // Parked time-sliced baselines: a paused pass keeps reading its PendingOldTree across
            // frames, so another fiber's retirement in between must not recycle shared nodes.
            foreach (var parked in ctx.ParkedBaselineFibers)
            {
                if (ReferenceEquals(parked, owner)) continue;
                WalkTree(parked.PendingOldTree, live, WalkMode.Mark);
                MarkFiberSlotRoots(parked, live);
            }
        }

        // A detached mount (portal / world-space drain) parents off the drain anchor, not the
        // component whose render declared it — hop through the captured logical parent so the
        // declaring chain's slots are reachable from portal content.
        private static ComponentFiber? LogicalParentOf(ComponentFiber fiber)
            => fiber.DetachedMountContext?.LogicalParent ?? fiber.Parent;

        private static void MarkFiberSlotRoots(ComponentFiber fiber, HashSet<object> live)
        {
            void Mark(object root) => MarkSlotRoot(root, live);
            VisitDisposableSlotRoots(fiber, Mark);
            VisitPersistentSlotRoots(fiber, Mark);
        }

        private static void MarkSlotRoot(object root, HashSet<object> live)
        {
            switch (root)
            {
                case VNode node:
                    MarkNode(node, live);
                    break;
                case IReadOnlyList<VNode?> list:
                    // A slot-held rented ARRAY can itself arrive at a sweep as a retired tree root
                    // (a fragment-rooted body unwraps to its children array via NormalizeToArray),
                    // so the array object joins the mark alongside its nodes.
                    if (list is VNode?[] array) live.Add(array);
                    for (var i = 0; i < list.Count; i++)
                    {
                        MarkNode(list[i], live);
                    }
                    break;
            }
        }

        // Enumerates the node roots held by the slot lists Unmount DISPOSES (compiler auto-memo
        // slots — whole-body trees — plus UseMemo / UseRef values that are a node or a list of
        // nodes). A slot with stable inputs re-emits the SAME instance on a later render even when
        // the current committed tree omits it (V.When toggling a memoized subtree), so these roots
        // must survive sweeps while their slot lives — and retire with it when it is disposed.
        private static void VisitDisposableSlotRoots(ComponentFiber fiber, Action<object> visitRoot)
        {
            var memoSlots = fiber.MemoSlots;
            if (memoSlots != null)
            {
                for (var i = 0; i < memoSlots.Count; i++)
                {
                    var slot = memoSlots[i];
                    if (slot == null) continue;
                    if (slot.CachedResult != null) visitRoot(slot.CachedResult);
                    if (slot.NextCachedResult != null && !ReferenceEquals(slot.NextCachedResult, slot.CachedResult))
                    {
                        visitRoot(slot.NextCachedResult);
                    }
                }
            }

            var valueSlots = fiber.MemoValueSlots;
            if (valueSlots != null)
            {
                for (var i = 0; i < valueSlots.Count; i++)
                {
                    var root = valueSlots[i]?.RecycleMarkRoot;
                    if (root != null) visitRoot(root);
                }
            }

            var refSlots = fiber.RefSlots;
            if (refSlots != null)
            {
                for (var i = 0; i < refSlots.Count; i++)
                {
                    var root = refSlots[i]?.RecycleMarkRoot;
                    if (root != null) visitRoot(root);
                }
            }
        }

        // Enumerates the node roots held by the slot lists Unmount PRESERVES for a remount
        // (UseState / UseReducer values). They stay live across unmount; the terminal Dispose path
        // clears the slots and retires them.
        private static void VisitPersistentSlotRoots(ComponentFiber fiber, Action<object> visitRoot)
        {
            var stateSlots = fiber.StateSlots;
            if (stateSlots != null)
            {
                for (var i = 0; i < stateSlots.Count; i++)
                {
                    var root = stateSlots[i]?.RecycleMarkRoot;
                    if (root != null) visitRoot(root);
                }
            }
        }

        // Add returning false means this node was already marked through another path (a shared
        // subtree reachable twice); its descendants are marked too, so stop. Marking is
        // node-granular plus the tree ARRAYS: a factory rents exactly one props bag / event array
        // per node, so pooled parts can only be shared by sharing their node, and the sweep prunes
        // at marked nodes before ever consulting parts (a caller-supplied part attached to two
        // hand-built nodes is protected by pool ownership instead — Return no-ops on what was
        // never rented). Arrays must be marked individually because a live array can arrive at a
        // sweep as a retired tree ROOT (fragment unwrapping), where SweepTree consults the set for
        // the array itself before any node prune applies.
        private static void MarkNode(VNode? node, HashSet<object> live)
        {
            if (node == null || !live.Add(node)) return;
            WalkChildSlots(node, live, WalkMode.Mark);
        }

        #endregion

        #region sweep

        private static void SweepTree(VNode?[] tree, HashSet<object> live)
        {
            if (tree == null || tree.Length == 0) return;
            if (live.Count > 0 && live.Contains(tree)) return;
            for (var i = 0; i < tree.Length; i++)
            {
                SweepNode(tree[i], live);
            }
            VNodePool.ReturnNodeArray(tree);
        }

        private static void SweepNode(VNode? node, HashSet<object> live)
        {
            if (node == null) return;
            // A marked node's entire subtree is committed state — nothing below it retires.
            if (live.Count > 0 && live.Contains(node)) return;
            WalkChildSlots(node, live, WalkMode.Sweep);
            if (node is BaseElementNode element)
            {
                VNodePool.ReturnProps(element.Props);
                if (element.Events is { Length: > 0 })
                {
                    VNodePool.ReturnEventArray(element.Events);
                }
            }
        }

        #endregion

        #region shared child-slot walk

        private enum WalkMode { Mark, Sweep }

        // The ONE switch that knows which node kinds bear children (and which child slots they
        // have). Mark and sweep both descend through here so the two passes cannot drift: a kind
        // descended by one but not the other either re-opens the per-render leak (mark-only) or
        // recycles live committed subtrees (sweep-only).
        //
        // Deliberately opaque on both sides: MemoNode (its inner lives in the memo cache — the
        // cache's own replace / dispose paths retire it) and ComponentNode (its props are an
        // opaque user value; a node passed through a props record is protected only while a
        // slot along the logical ancestor chain also holds it — see the header contract).
        // ContextProviderNode's VALUE is marked but never swept: a consumer may have committed the
        // value's node into its own tree, and leaking a provider-held node is recoverable while
        // recycling a consumed one is not.
        private static void WalkChildSlots(VNode node, HashSet<object> live, WalkMode mode)
        {
            switch (node)
            {
                case BaseElementNode element:
                    WalkTree(element.Children, live, mode);
                    break;
                case FragmentNode fragment:
                    WalkTree(fragment.Children, live, mode);
                    break;
                case PortalNode portal:
                    WalkTree(portal.Children, live, mode);
                    break;
                case WorldSpaceNode worldSpace:
                    WalkTree(worldSpace.Children, live, mode);
                    break;
                case SuspenseNode suspense:
                    WalkOne(suspense.Fallback, live, mode);
                    WalkTree(suspense.Children, live, mode);
                    break;
                case AnimatePresenceNode presence:
                    WalkTree(presence.Children, live, mode);
                    break;
                case ContextProviderNode provider:
                    if (mode == WalkMode.Mark)
                    {
                        var value = provider.BoxedValueForRecycleMark;
                        if (value != null) MarkSlotRoot(value, live);
                    }
                    WalkTree(provider.Children, live, mode);
                    break;
            }
        }

        private static void WalkOne(VNode? node, HashSet<object> live, WalkMode mode)
        {
            if (mode == WalkMode.Mark) MarkNode(node, live);
            else SweepNode(node, live);
        }

        private static void WalkTree(VNode?[]? tree, HashSet<object> live, WalkMode mode)
        {
            if (tree == null || tree.Length == 0) return;
            if (mode == WalkMode.Mark)
            {
                // The array object joins the mark too: a live children array can arrive at a later
                // sweep as a retired tree ROOT (a memoized fragment-rooted body unwraps to its
                // children array via NormalizeToArray), and SweepTree's entry guard consults the
                // set for the array before any node prune applies.
                live.Add(tree);
                for (var i = 0; i < tree.Length; i++)
                {
                    MarkNode(tree[i], live);
                }
            }
            else
            {
                SweepTree(tree, live);
            }
        }

        #endregion
    }
}
