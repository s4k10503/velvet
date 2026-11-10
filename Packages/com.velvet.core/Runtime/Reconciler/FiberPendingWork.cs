using UnityEngine.UIElements;

namespace Velvet
{
    // React bails an element it is handed again, and skips a subtree it was handed again, only where no fiber in it
    // has work pending: its lanes and childLanes. ComponentFiber.PendingWorkBelow stands for childLanes here, so the
    // question costs one read where no work has been marked below since it was last answered.
    internal static class FiberPendingWork
    {
        // Work of the fiber's own that a pass reaching it must render it for, beside IsDirty and RenderOwed: a pass of
        // its own that suspended, a reconcile parked partway and a read still waiting.
        internal static bool HasOwn(ComponentFiber fiber)
            => fiber.SuspendedOn != null
                || fiber.Reconciler?.HasPendingWork == true
                || ComponentBoundarySearch.HasPendingAsyncSlot(fiber);

        internal static bool IsPendingItself(ComponentFiber fiber)
            => fiber.IsDirty || fiber.RenderOwed || HasOwn(fiber);

        // Called wherever a fiber takes work of its own: IsDirty, RenderOwed and SuspendedOn on their writes, a
        // parked reconcile from Reconciler, a pending read from Hooks.Use, and a fiber appended under a new parent.
        // Stops at the first ancestor already marked, which holds because a marked fiber's ancestors are marked.
        internal static void MarkAncestors(ComponentFiber fiber)
        {
            for (var ancestor = fiber.Parent; ancestor is { PendingWorkBelow: false }; ancestor = ancestor.Parent)
            {
                ancestor.PendingWorkBelow = true;
            }
        }

        // Whether a fiber below root, root excluded, has work pending. Clears the marks it finds stale.
        internal static bool Below(ComponentFiber root)
        {
            if (!root.PendingWorkBelow) return false;
            for (var child = root.Child; child != null; child = child.Sibling)
            {
                if (PendingAtOrBelow(child)) return true;
            }
            root.PendingWorkBelow = false;
            return false;
        }

        // Whether a fiber below owner whose output sits inside element has work pending, which is what a walk that
        // leaves element unvisited would leave unrendered. A fiber is placed by its mount point, or by the
        // placeholder of the Portal it renders through; one with neither counts as inside.
        internal static bool HoldsPendingUnder(ComponentFiber owner, VisualElement element)
        {
            if (!owner.PendingWorkBelow) return false;
            var anyPending = false;
            for (var child = owner.Child; child != null; child = child.Sibling)
            {
                if (!PendingAtOrBelow(child)) continue;
                anyPending = true;
                var placed = child.OwningPortalPlaceholder ?? child.MountPoint;
                if (placed == null || ReferenceEquals(placed, element) || element.Contains(placed)) return true;
            }
            if (!anyPending) owner.PendingWorkBelow = false;
            return false;
        }

        // Descends only through marked fibers, so a clean subtree costs one read. When nothing is found, the marks
        // walked are stale and cleared.
        private static bool PendingAtOrBelow(ComponentFiber top)
        {
            if (IsPendingItself(top)) return true;
            if (!top.PendingWorkBelow) return false;
            if (FindPendingBelow(top)) return true;
            ClearMarks(top);
            return false;
        }

        private static bool FindPendingBelow(ComponentFiber top)
        {
            var node = top.Child;
            while (node != null)
            {
                if (IsPendingItself(node)) return true;
                if (node.PendingWorkBelow && node.Child != null)
                {
                    node = node.Child;
                    continue;
                }
                node = NextOutside(node, top);
            }
            return false;
        }

        private static void ClearMarks(ComponentFiber top)
        {
            top.PendingWorkBelow = false;
            var node = top.Child;
            while (node != null)
            {
                if (node.PendingWorkBelow)
                {
                    node.PendingWorkBelow = false;
                    if (node.Child != null)
                    {
                        node = node.Child;
                        continue;
                    }
                }
                node = NextOutside(node, top);
            }
        }

        // The next sibling of node or of its nearest ancestor below top, or null once the walk returns to top.
        private static ComponentFiber? NextOutside(ComponentFiber node, ComponentFiber top)
        {
            while (node.Sibling == null)
            {
                var parent = node.Parent;
                if (parent == null || ReferenceEquals(parent, top)) return null;
                node = parent;
            }
            return node.Sibling;
        }
    }
}
