using System;

namespace Velvet
{
    // Returns a committed VNode tree (and the inline old trees deferred during a reconcile pass)
    // to the VNode pool. Extracted from the render core: NormalizeToArray shapes a render body's
    // output, ReturnPooledObjects recycles a tree's top-level pooled objects, and
    // DrainDeferredInlineOldTreeReturns flushes the deferred inline baselines at the top-level
    // reconcile boundary. ReturnPooledTreeRecursive (editor-only) recycles a never-reconciled tree.
    internal static class FiberTreeReturn
    {
        internal static VNode[] NormalizeToArray(VNode node)
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
        // ReconcilerContext.DeferredInlineOldTreeReturns for why the return must be deferred.
        internal static void DrainDeferredInlineOldTreeReturns(ReconcilerContext ctx)
        {
            var queue = ctx.DeferredInlineOldTreeReturns;
            if (queue.Count == 0) return;
            for (var i = 0; i < queue.Count; i++)
            {
                ReturnPooledObjects(queue[i]);
            }
            queue.Clear();
        }

        internal static void ReturnPooledObjects(VNode[] tree)
        {
            if (tree == null || tree.Length == 0) return;

            for (var i = 0; i < tree.Length; i++)
            {
                switch (tree[i])
                {
                    case ElementNode elem:
                        VNodePool.ReturnProps(elem.Props);
                        VNodePool.ReturnEventArray(elem.Events);
                        VNodePool.ReturnNodeArray(elem.Children);
                        break;
                    case MotionNode motion:
                        VNodePool.ReturnProps(motion.Props);
                        VNodePool.ReturnEventArray(motion.Events);
                        VNodePool.ReturnNodeArray(motion.Children);
                        break;
                }
            }

            VNodePool.ReturnNodeArray(tree);
        }

#if UNITY_EDITOR
        // Returns every pooled object in tree recursively, including descendant element
        // children. The normal flow returns only the top level because the reconciler recycles descendants as
        // it walks the tree; a discarded tree that is never reconciled (the double-invoke diagnostic pass) has no
        // such walk, so its descendants must be returned explicitly to avoid draining the pool.
        internal static void ReturnPooledTreeRecursive(VNode[] tree)
        {
            if (tree == null || tree.Length == 0) return;

            for (var i = 0; i < tree.Length; i++)
            {
                switch (tree[i])
                {
                    case ElementNode elem:
                        ReturnPooledTreeRecursive(elem.Children);
                        VNodePool.ReturnProps(elem.Props);
                        VNodePool.ReturnEventArray(elem.Events);
                        VNodePool.ReturnNodeArray(elem.Children);
                        break;
                    case MotionNode motion:
                        ReturnPooledTreeRecursive(motion.Children);
                        VNodePool.ReturnProps(motion.Props);
                        VNodePool.ReturnEventArray(motion.Events);
                        VNodePool.ReturnNodeArray(motion.Children);
                        break;
                    case FragmentNode fragment:
                        ReturnPooledTreeRecursive(fragment.Children);
                        break;
                    case PortalNode portal:
                        ReturnPooledTreeRecursive(portal.Children);
                        break;
                    case SuspenseNode suspense:
                        ReturnPooledTreeRecursive(suspense.Children);
                        break;
                    case AnimatePresenceNode presence:
                        ReturnPooledTreeRecursive(presence.Children);
                        break;
                }
            }

            VNodePool.ReturnNodeArray(tree);
        }
#endif
    }
}
