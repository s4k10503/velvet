namespace Velvet
{
    // React bails an element it is handed again, and skips a subtree it was handed again, only where no fiber in it
    // has work pending (its lanes and childLanes). ComponentRegistry bails a plain component on HasOwn, and
    // ChildReconciler skips a node handed back by instance on Below.
    internal static class FiberPendingWork
    {
        // Work of the fiber's own that a pass reaching it must render it for, beside IsDirty and RenderOwed: a pass of
        // its own that suspended, a reconcile parked partway and a read still waiting.
        internal static bool HasOwn(ComponentFiber fiber)
            => fiber.SuspendedOn != null
                || fiber.Reconciler?.HasPendingWork == true
                || ComponentBoundarySearch.HasPendingAsyncSlot(fiber);

        // Whether a fiber below root, root excluded, is dirty, is owed a render or has work of its own pending. Walks
        // the fiber links without a stack.
        internal static bool Below(ComponentFiber root)
        {
            var node = root.Child;
            while (node != null)
            {
                if (node.IsDirty || node.RenderOwed || HasOwn(node)) return true;
                if (node.Child != null)
                {
                    node = node.Child;
                    continue;
                }
                while (node.Sibling == null)
                {
                    node = node.Parent;
                    if (node == null || ReferenceEquals(node, root)) return false;
                }
                node = node.Sibling;
            }
            return false;
        }
    }
}
