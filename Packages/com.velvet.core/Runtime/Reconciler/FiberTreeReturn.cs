using System;
using System.Collections.Generic;

namespace Velvet
{
    // Returns a committed VNode tree (and the inline old trees deferred during a reconcile pass)
    // to the VNode pool. Extracted from the render core: NormalizeToArray shapes a render body's
    // output, ReturnRetiredTree recycles a retired tree, and DrainDeferredInlineOldTreeReturns
    // flushes the deferred inline baselines at the top-level reconcile boundary.
    //
    // Recycling is a mark-and-sweep over the RETIRED tree: the mark pass collects every object
    // still reachable from the owner fiber's committed state (its PreviousTree, plus the memoized
    // VNode roots of the fiber and its ancestor chain), and the sweep returns the retired tree's
    // pooled objects RECURSIVELY, sparing anything marked live. Two forces make both halves
    // necessary:
    //   - Depth: factory-rented props bags / event arrays / child arrays sit at every nesting
    //     level (and under Portal / WorldSpace / Suspense / Provider boundaries — those are
    //     logical boundaries, not recycle boundaries), so a top-level-only return strands one
    //     bag per render for every nested renting element.
    //   - Sharing: memoization (the compiler's auto-memo slots, Hooks.UseMemo, a props-bail that
    //     keeps a child's tree referencing parent-built nodes) legitimately carries the SAME node
    //     instances across consecutive trees, so an unmarked recursive return would recycle
    //     objects the committed tree still reads as its patch baseline — and the pool would hand
    //     them to an unrelated mount to overwrite.
    // A consumer-cached factory-built node that leaves the tree one render and returns later is
    // outside this protection (nothing live references it in between); caching whole factory
    // nodes across renders is not a supported pattern — cache with Hooks.UseMemo instead, whose
    // slot the mark pass reads.
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
        // pass to the VNode pool. Called once at the top-level reconcile boundary (after the whole pass has
        // finished reading them as patch baselines and no renter can alias them) so the deferral does not
        // add GC pressure — the nodes are pooled for the next pass, just not mid-pass. See
        // ReconcilerContext.DeferredInlineOldTreeReturns for why the return must be deferred. Each entry
        // carries its owning fiber so the sweep marks that fiber's NOW-committed tree as live (by drain
        // time the owner's PreviousTree holds the render that retired this baseline).
        internal static void DrainDeferredInlineOldTreeReturns(ReconcilerContext ctx)
        {
            var queue = ctx.DeferredInlineOldTreeReturns;
            if (queue.Count == 0) return;
            for (var i = 0; i < queue.Count; i++)
            {
                var (tree, owner) = queue[i];
                ReturnRetiredTree(tree, owner);
            }
            queue.Clear();
        }

        // Reused mark set. Main-thread only (like VNodePool) and non-reentrant by construction: neither
        // the mark nor the sweep runs user code or re-enters the reconciler, so no nested return can
        // begin while one is in progress. Cleared after each sweep to release object refs for GC.
        private static readonly HashSet<object> s_liveMarks = new();

        // Returns retired's pooled objects (props bags, single-event arrays, node arrays) to
        // VNodePool, recursing through every child-bearing node kind, while sparing objects still
        // reachable from owner's committed state. Pass a null owner when the whole
        // tree is being discarded with no committed successor (unmount teardown) — the sweep then
        // returns everything unconditionally. Returns are idempotent (rent-scoped pool ownership),
        // so a node reachable through two retired trees recycles exactly once.
        internal static void ReturnRetiredTree(VNode?[]? retired, ComponentFiber? owner)
        {
            if (retired == null || retired.Length == 0) return;

            var live = s_liveMarks;
            try
            {
                if (owner != null)
                {
                    MarkTree(owner.PreviousTree, live);
                    // Memoized roots along the ancestor chain: a deps-stable Hooks.UseMemo (or auto-memo
                    // slot) re-emits the same node instance on a LATER render even when the current
                    // committed tree omits it (e.g. V.When(false, memoized)), and an ancestor's memoized
                    // node can flow into this fiber's tree as props — so slot-held trees anywhere up the
                    // chain must survive the sweep for their comeback render to reuse them intact.
                    for (var fiber = owner; fiber != null; fiber = fiber.Parent)
                    {
                        MarkMemoRoots(fiber, live);
                    }
                }
                SweepTree(retired, live);
            }
            finally
            {
                live.Clear();
            }
        }

        #region mark

        private static void MarkTree(VNode?[]? tree, HashSet<object> live)
        {
            if (tree == null || tree.Length == 0) return;
            live.Add(tree);
            for (var i = 0; i < tree.Length; i++)
            {
                MarkNode(tree[i], live);
            }
        }

        // Add returning false means this node was already marked through another path (a shared
        // subtree reachable twice); its descendants are marked too, so stop.
        private static void MarkNode(VNode? node, HashSet<object> live)
        {
            if (node == null || !live.Add(node)) return;
            switch (node)
            {
                case BaseElementNode element:
                    if (element.Props != null) live.Add(element.Props);
                    if (element.Events is { Length: > 0 }) live.Add(element.Events);
                    MarkTree(element.Children, live);
                    break;
                case FragmentNode fragment:
                    MarkTree(fragment.Children, live);
                    break;
                case PortalNode portal:
                    MarkTree(portal.Children, live);
                    break;
                case WorldSpaceNode worldSpace:
                    MarkTree(worldSpace.Children, live);
                    break;
                case SuspenseNode suspense:
                    MarkNode(suspense.Fallback, live);
                    MarkTree(suspense.Children, live);
                    break;
                case AnimatePresenceNode presence:
                    MarkTree(presence.Children, live);
                    break;
                case ContextProviderNode provider:
                    MarkTree(provider.Children, live);
                    break;
                    // MemoNode: its inner tree lives in the memo cache, which the sweep never descends
                    // into — nothing to protect here. ComponentNode: its props are an opaque user value
                    // the sweep cannot descend either; symmetry keeps both sides blind to it.
            }
        }

        private static void MarkMemoRoots(ComponentFiber fiber, HashSet<object> live)
        {
            var memoSlots = fiber.MemoSlots;
            if (memoSlots != null)
            {
                for (var i = 0; i < memoSlots.Count; i++)
                {
                    var slot = memoSlots[i];
                    if (slot == null) continue;
                    if (slot.CachedResult != null) MarkNode(slot.CachedResult, live);
                    if (slot.NextCachedResult != null) MarkNode(slot.NextCachedResult, live);
                }
            }

            var valueSlots = fiber.MemoValueSlots;
            if (valueSlots != null)
            {
                for (var i = 0; i < valueSlots.Count; i++)
                {
                    switch (valueSlots[i]?.RecycleMarkRoot)
                    {
                        case VNode node:
                            MarkNode(node, live);
                            break;
                        case VNode?[] tree:
                            MarkTree(tree, live);
                            break;
                    }
                }
            }
        }

        #endregion

        #region sweep

        private static void SweepTree(VNode?[] tree, HashSet<object> live)
        {
            if (tree == null || tree.Length == 0) return;
            if (live.Contains(tree)) return;
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
            switch (node)
            {
                case BaseElementNode element:
                    SweepTree(element.Children, live);
                    if (element.Props != null && !live.Contains(element.Props))
                    {
                        VNodePool.ReturnProps(element.Props);
                    }
                    if (element.Events is { Length: > 0 } && !live.Contains(element.Events))
                    {
                        VNodePool.ReturnEventArray(element.Events);
                    }
                    break;
                case FragmentNode fragment:
                    SweepTree(fragment.Children, live);
                    break;
                case PortalNode portal:
                    SweepTree(portal.Children, live);
                    break;
                case WorldSpaceNode worldSpace:
                    SweepTree(worldSpace.Children, live);
                    break;
                case SuspenseNode suspense:
                    SweepNode(suspense.Fallback, live);
                    SweepTree(suspense.Children, live);
                    break;
                case AnimatePresenceNode presence:
                    SweepTree(presence.Children, live);
                    break;
                case ContextProviderNode provider:
                    SweepTree(provider.Children, live);
                    break;
                    // MemoNode / ComponentNode: see the matching note in MarkNode.
            }
        }

        #endregion
    }
}
