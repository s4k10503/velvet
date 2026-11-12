using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // React's commitDeletionEffectsOnFiber over what one container's reconcile deletes, or over an element leaving
    // outside one: the pre-order walk of PreOrderTeardown, in which a component's insertion and layout destroys run,
    // and its passive destroys are queued for the passive phase (FiberEffects.CleanUpDeleted), where the walk reaches
    // the component, and an element's ref detaches where the walk reaches the element. It runs ahead of the removals,
    // which find neither left to run.
    internal sealed class DeletionCommit : PreOrderTeardown
    {
        // An instance per walk on the stack, so a cleanup that commits another walk takes an instance of its own.
        private static readonly Stack<DeletionCommit> s_pool = new();

        private FiberElementCleaner _cleaner = null!;
        private VisualElement? _container;
        private readonly List<(VisualElement? Row, bool Removed, ComponentFiber? Owner)> _rows = new();
        private readonly HashSet<VisualElement> _roots = new();
        private readonly HashSet<ComponentFiber> _orphans = new();
        private readonly HashSet<ComponentFiber> _collected = new();
        private readonly List<ComponentFiber> _fibers = new();
        private DeletionAnchor _rowAnchor;
        private bool _registeredRoots;

        // A container reconcile's deletion: Begin, a row for each old leaf in order, Run once the walk has decided
        // which of them it removes, and End once the removals have run.
        internal static DeletionCommit Begin(ReconcilerContext ctx, FiberElementCleaner cleaner, VisualElement? container)
        {
            var commit = s_pool.Count > 0 ? s_pool.Pop() : new DeletionCommit();
            commit.Ctx = ctx;
            commit._cleaner = cleaner;
            commit._container = container;
            return commit;
        }

        internal void AddRow(VisualElement? row, bool removed, ComponentFiber? owner) => _rows.Add((row, removed, owner));

        // React deletes a parent's children in the order it reconciled them, so the rows are taken in theirs, each
        // removed row as a subtree, and an orphan whose row stays where that row is.
        internal void Run(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers)
        {
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) _orphans.Add(fiber);
            }
            foreach (var (row, removed, _) in _rows)
            {
                if (removed && row != null) AddRowAsRoot(row);
            }
            foreach (var fiber in oldFibers)
            {
                if (_orphans.Contains(fiber) && _collected.Add(fiber)) _fibers.Add(fiber);
            }
            CollectHeldFibers();
            if (_fibers.Count == 0 && _roots.Count == 0) return;
            ComponentFiber.BeginDeletion(_fibers);
            foreach (var fiber in _fibers) Place(fiber);
            foreach (var (row, removed, owner) in _rows)
            {
                if (row == null) continue;
                _rowAnchor = AnchorOfRow(owner);
                if (removed) VisitRow(row);
                else TakeDownStartingAt(Ctx.ZLayerPlaceholders.TryGetValue(row, out var real) ? real : row);
            }
            _rowAnchor = DeletionAnchor.For(_container, Ctx.FiberStack.Current);
            if (_container != null) TakeDownEndingIn(_container);
            TakeDownTheRest();
            foreach (var root in _roots) Ctx.RemovalsTakenDown.Add(root);
            _registeredRoots = true;
        }

        internal void End()
        {
            if (_registeredRoots)
            {
                foreach (var root in _roots) Ctx.RemovalsTakenDown.Remove(root);
            }
            Clear();
            s_pool.Push(this);
        }

        // An element leaving outside a container reconcile's deletion. Without a component held below it, the element
        // teardown that follows (FiberElementCleaner.CleanupElementCore) reaches its refs in this walk's order.
        internal static void TakeDownLeaving(ReconcilerContext ctx, FiberElementCleaner cleaner, VisualElement element)
        {
            var commit = Begin(ctx, cleaner, element.hierarchy.parent);
            try
            {
                commit.AddRowAsRoot(element);
                commit.CollectHeldFibers();
                if (commit._fibers.Count == 0) return;
                ComponentFiber.BeginDeletion(commit._fibers);
                foreach (var fiber in commit._fibers) commit.Place(fiber);
                commit._rowAnchor = DeletionAnchor.For(commit._container, ctx.FiberStack.Current);
                commit.VisitRow(element);
                commit.TakeDownTheRest();
            }
            finally
            {
                commit.End();
            }
        }

        // A z-layer placeholder is followed by the element it stands for, as a visit of their parent takes them.
        private void VisitRow(VisualElement row)
        {
            Visit(row);
            if (Ctx.ZLayerPlaceholders.TryGetValue(row, out var real)) Visit(real);
        }

        private void AddRowAsRoot(VisualElement row)
        {
            AddRoot(row);
            if (Ctx.ZLayerPlaceholders.TryGetValue(row, out var real)) AddRoot(real);
        }

        // A Portal's rows are reached at its placeholder, so a row whose target the deletion holds is passed over
        // there (IsRoot), and the fibers inside the rows are collected with the rest.
        private void AddRoot(VisualElement element)
        {
            if (!_roots.Add(element)) return;
            if (Ctx.PortalState.Count == 0) return;
            foreach (var (placeholder, portal) in Ctx.PortalState)
            {
                if (portal.Target == null || !IsAtOrUnder(placeholder, element)) continue;
                var rows = RenderedChildrenOf(portal.Target);
                for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength && slot < rows.Count; slot++)
                {
                    AddRowAsRoot(rows[slot]);
                }
            }
        }

        // The components mounted inside the roots, and the top-level children of a Portal among them, which mount on
        // the Portal's target rather than inside any root.
        private void CollectHeldFibers()
        {
            List<ComponentFiber>? held = null;
            foreach (var root in _roots) Ctx.ComponentRegistry.CollectFibersIn(root, ref held);
            if (Ctx.PortalState.Count > 0)
            {
                foreach (var placeholder in Ctx.PortalState.Keys)
                {
                    if (IsUnderARoot(placeholder)) Ctx.ComponentRegistry.CollectInlineFibersOwnedByPortal(placeholder, ref held);
                }
            }
            if (held == null) return;
            foreach (var fiber in held)
            {
                if (!fiber.IsDisposed && _collected.Add(fiber)) _fibers.Add(fiber);
            }
        }

        private bool IsUnderARoot(VisualElement element)
        {
            var ve = element;
            while (ve != null && !_roots.Contains(ve)) ve = ve.hierarchy.parent;
            return ve != null;
        }

        private static bool IsAtOrUnder(VisualElement element, VisualElement subtree)
        {
            var ve = element;
            while (ve != null && !ReferenceEquals(ve, subtree)) ve = ve.hierarchy.parent;
            return ve != null;
        }

        // A fiber the walk reached no element for: one held by a Portal whose placeholder is no root's, and one
        // holding no row behind them. Taken down in the order collected; a fiber taken down already runs nothing.
        private void TakeDownTheRest()
        {
            foreach (var fiber in _fibers) TakeDown(fiber);
        }

        // The deleted row's place in React's tree: the outermost orphan holding it is the deletion's root, and an
        // element the container's owner rendered is one where no orphan holds it.
        private DeletionAnchor AnchorOfRow(ComponentFiber? owner)
        {
            var outermost = OutermostOrphanAt(owner);
            return DeletionAnchor.For(_container, outermost != null ? outermost.Parent : owner);
        }

        private ComponentFiber? OutermostOrphanAt(ComponentFiber? fiber)
        {
            ComponentFiber? outermost = null;
            for (var f = fiber; f != null && _orphans.Contains(f); f = f.Parent) outermost = f;
            return outermost;
        }

        protected override void TakeDown(ComponentFiber fiber)
        {
            if (!fiber.IsMounted || fiber.IsDisposed) return;
            var orphan = OutermostOrphanAt(fiber);
            var anchor = orphan != null ? DeletionAnchor.For(_container, orphan.Parent) : _rowAnchor;
            FiberEffects.CleanUpDeleted(fiber, anchor);
        }

        protected override void TakeDown(VisualElement element) => _cleaner.DetachRef(element);

        protected override bool IsRoot(VisualElement element) => _roots.Contains(element);

        protected override void Clear()
        {
            base.Clear();
            _cleaner = null!;
            _container = null;
            _rows.Clear();
            _roots.Clear();
            _orphans.Clear();
            _collected.Clear();
            _fibers.Clear();
            _rowAnchor = default;
            _registeredRoots = false;
        }
    }

    // Where a deletion's passive destroys run among the commit's other passive work: React runs a deleted subtree's
    // where its passive unmount walk enters the deleted fiber's parent — Owner where Element is null, and otherwise
    // the element Element, a host Owner rendered.
    internal readonly record struct DeletionAnchor(ComponentFiber? Owner, VisualElement? Element)
    {
        // A deleted fiber, or an element, whose rows sat in container, under parent. The parent is React's parent too
        // where it holds rows in the same container; otherwise the container is a host the parent rendered.
        internal static DeletionAnchor For(VisualElement? container, ComponentFiber? parent)
            => parent == null || container == null || ReferenceEquals(parent.MountPoint, container)
                ? new DeletionAnchor(parent, null)
                : new DeletionAnchor(parent, container);
    }

    // The passive destroys one deleted fiber left, queued on its context (ReconcilerContext.PendingDeletedPassiveCleanups)
    // for the passive drain. An error one throws starts its search at CatchFrom (ComponentBoundarySearch.PropagateFromDeletion).
    internal sealed class DeletedPassiveCleanup
    {
        internal DeletedPassiveCleanup(ComponentFiber fiber, ComponentFiber? catchFrom, List<Action> cleanups, DeletionAnchor anchor)
        {
            Fiber = fiber;
            CatchFrom = catchFrom;
            Cleanups = cleanups;
            Anchor = anchor;
        }

        internal ComponentFiber Fiber { get; }

        internal ComponentFiber? CatchFrom { get; }

        internal List<Action> Cleanups { get; }

        internal DeletionAnchor Anchor { get; }

        internal void Run()
        {
            foreach (var cleanup in Cleanups)
            {
                try
                {
                    cleanup();
                }
                catch (Exception exception)
                {
                    ComponentBoundarySearch.PropagateFromDeletion(CatchFrom, Fiber, exception);
                }
            }
        }
    }

    // Places a drain's deleted passive destroys among the cleanups of the fibers it staged, which run in post-order
    // (FiberEffects.RunPassivePhases), as React's passive unmount walk interleaves them: a deletion runs where the walk
    // enters its anchor, so behind each staged fiber whose whole subtree comes before the anchor, and ahead of the
    // rest. Two deletions take the order their anchors are entered in, and those under one anchor their own.
    internal static class DeletedPassivePlacement
    {
        // For each destroy, in the order they run, the index of the staged fiber it runs ahead of.
        internal static List<(DeletedPassiveCleanup Cleanup, int Before)> Place(
            ReconcilerContext ctx, List<DeletedPassiveCleanup> cleanups, List<ComponentFiber> staged)
        {
            var logical = LogicalElementTree.Rent(ctx);
            try
            {
                var ordered = new List<DeletedPassiveCleanup>(cleanups.Count);
                foreach (var cleanup in cleanups)
                {
                    var at = ordered.Count;
                    while (at > 0 && EnteredEarlier(logical, cleanup.Anchor, ordered[at - 1].Anchor)) at--;
                    ordered.Insert(at, cleanup);
                }
                var placed = new List<(DeletedPassiveCleanup Cleanup, int Before)>(ordered.Count);
                var before = 0;
                foreach (var cleanup in ordered)
                {
                    while (before < staged.Count && CompletesBefore(logical, staged[before], cleanup.Anchor)) before++;
                    placed.Add((cleanup, before));
                }
                return placed;
            }
            finally
            {
                logical.Return();
            }
        }

        // Whether fiber's whole subtree comes before the place the anchor is entered at.
        private static bool CompletesBefore(LogicalElementTree tree, ComponentFiber fiber, DeletionAnchor anchor)
        {
            var owner = anchor.Owner;
            if (owner == null || !owner.IsMounted) return false;
            if (anchor.Element == null) return FiberEffects.PrecedesInTreeOrder(fiber, owner);
            if (IsAtOrAbove(fiber, owner)) return false;
            if (IsAtOrAbove(owner, fiber)) return OutputCompletesBefore(tree, fiber, anchor.Element);
            return FiberEffects.PrecedesInTreeOrder(fiber, owner);
        }

        // Whether a is entered before b, strictly: a holds b, or comes earlier in the tree.
        private static bool EnteredEarlier(LogicalElementTree tree, DeletionAnchor a, DeletionAnchor b)
        {
            var ownerA = a.Owner;
            var ownerB = b.Owner;
            if (ownerA == null || ownerB == null || !ownerA.IsMounted || !ownerB.IsMounted) return false;
            if (ReferenceEquals(ownerA, ownerB))
            {
                if (ReferenceEquals(a.Element, b.Element) || b.Element == null) return false;
                if (a.Element == null) return true;
                if (tree.IsAtOrUnder(b.Element, a.Element)) return true;
                if (tree.IsAtOrUnder(a.Element, b.Element)) return false;
                return tree.Precedes(a.Element, b.Element);
            }
            if (IsAtOrAbove(ownerA, ownerB))
            {
                return a.Element == null || !OutputCompletesBefore(tree, ownerB, a.Element);
            }
            if (IsAtOrAbove(ownerB, ownerA))
            {
                return b.Element != null && OutputCompletesBefore(tree, ownerA, b.Element);
            }
            return FiberEffects.PrecedesInTreeOrder(ownerA, ownerB);
        }

        // Whether everything fiber rendered comes before element, a host one of fiber's ancestors rendered: fiber's
        // rows neither hold the element nor lie inside it, and end before it.
        private static bool OutputCompletesBefore(LogicalElementTree tree, ComponentFiber fiber, VisualElement element)
        {
            var mount = fiber.MountPoint;
            if (mount == null) return false;
            if (!fiber.IsInlineMounted) return !tree.IsAtOrUnder(mount, element) && tree.Precedes(mount, element);
            if (fiber.MountSlotCount < 0) return false;
            var rowParent = tree.RowParentOf(fiber);
            if (tree.IsAtOrUnder(rowParent, element)) return false;
            return tree.RowsBefore(rowParent, fiber.MountSlotStart + fiber.MountSlotCount, element);
        }

        private static bool IsAtOrAbove(ComponentFiber ancestor, ComponentFiber fiber)
        {
            ComponentFiber? f = fiber;
            while (f != null && !ReferenceEquals(f, ancestor)) f = f.Parent;
            return f != null;
        }
    }
}
