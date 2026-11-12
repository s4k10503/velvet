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
        private int _slotStart;
        private OldSlotOccupants? _occupants;
        private readonly List<(int Index, VisualElement Row, ComponentFiber? Owner)> _removedRows = new();
        // The old leaves an orphan's output starts at, each with the element the walk takes the orphan down at.
        private readonly List<(int Index, VisualElement Start)> _orphanRows = new();
        // Each root with the fibers mounted under it, which its removal disposes (FiberElementCleaner.CleanupElement).
        private readonly Dictionary<VisualElement, List<ComponentFiber>?> _roots = new();
        private readonly HashSet<VisualElement> _portalsUnderRoots = new();
        private readonly HashSet<ComponentFiber> _orphans = new();
        private readonly HashSet<ComponentFiber> _collected = new();
        private readonly List<ComponentFiber> _fibers = new();
        private bool _startsInsideARow;
        private DeletionAnchor _rowAnchor;

        // A container reconcile's deletion: Begin, each old leaf it removes in order, Run, and End after the removals,
        // or where an abort skipped them. Occupants are the container's old leaves, the first at slotStart.
        internal static DeletionCommit Begin(ReconcilerContext ctx, FiberElementCleaner cleaner, VisualElement? container,
            int slotStart, OldSlotOccupants? occupants)
        {
            var commit = s_pool.Count > 0 ? s_pool.Pop() : new DeletionCommit();
            commit.Ctx = ctx;
            commit._cleaner = cleaner;
            commit._container = container;
            commit._slotStart = slotStart;
            commit._occupants = occupants;
            return commit;
        }

        internal void AddRemovedRow(int index, VisualElement row, ComponentFiber? owner)
            => _removedRows.Add((index, row, owner));

        // The old leaves AddRemovedRow named, in the order it named them.
        internal List<(int Index, VisualElement Row, ComponentFiber? Owner)> RemovedRows => _removedRows;

        // React deletes a parent's children in the order it reconciled them, so the rows are taken in theirs, each
        // removed row as a subtree, and an orphan whose row stays where that row is.
        internal void Run(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers, int oldLength)
        {
            foreach (var fiber in oldFibers)
            {
                if (newFibers.Contains(fiber) || !_orphans.Add(fiber)) continue;
                _collected.Add(fiber);
                _fibers.Add(fiber);
            }
            foreach (var (_, row, _) in _removedRows) AddRowAsRoot(row);
            CollectHeldFibers();
            ComponentFiber.BeginDeletion(_fibers);
            foreach (var fiber in _fibers) PlaceDeleted(fiber, oldLength);
            _orphanRows.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            var walksRows = WalksRows();
            var next = 0;
            foreach (var (index, row, owner) in _removedRows)
            {
                for (; next < _orphanRows.Count && _orphanRows[next].Index <= index; next++)
                {
                    // MUTANT_SURVIVES(equivalent, boundary): an orphan starting at a removed row goes first either way.
                    // The row's visit takes down what starts at the row ahead of the row's ref, a z-layer placeholder
                    // visited ahead of it holds no ref (FiberZLayerCoordinator.CreatePlaceholder), and an orphan reads
                    // no row anchor.
                    if (_orphanRows[next].Index < index) TakeDownStartingAt(_orphanRows[next].Start);
                }
                _rowAnchor = AnchorOfRow(owner);
                if (walksRows) VisitRow(row);
                else TakeDownStartingAt(row);
            }
            for (; next < _orphanRows.Count; next++) TakeDownStartingAt(_orphanRows[next].Start);
            TakeDownTheRest();
            // MUTANT_SURVIVES(equivalent): without the entries the removals walk these rows again
            // (FiberElementCleaner.CleanupElement), find no ref installed and no effect left to run on them, and
            // collect the same fibers to dispose.
            foreach (var (root, mounted) in _roots) Ctx.RemovalsTakenDown[root] = mounted;
        }

        // A row a walk registered and an abort kept from its removal would otherwise skip the walk of its next one.
        internal void End()
        {
            foreach (var root in _roots.Keys) Ctx.RemovalsTakenDown.Remove(root);
            Clear();
            // MUTANT_SURVIVES(equivalent): an instance not handed back only makes the next Begin build a new one.
            s_pool.Push(this);
        }

        // An element leaving outside a container reconcile's deletion, for which it returns the fibers mounted under
        // the element. Without a component held below it, the element teardown that follows
        // (FiberElementCleaner.CleanupElementCore) reaches its refs in this walk's order.
        internal static List<ComponentFiber>? TakeDownLeaving(ReconcilerContext ctx, FiberElementCleaner cleaner,
            VisualElement element)
        {
            var commit = Begin(ctx, cleaner, element.hierarchy.parent, slotStart: 0, occupants: null);
            try
            {
                commit.AddRowAsRoot(element);
                commit.CollectHeldFibers();
                var mounted = commit._roots[element];
                if (commit._fibers.Count == 0) return mounted;
                ComponentFiber.BeginDeletion(commit._fibers);
                foreach (var fiber in commit._fibers) commit.Place(fiber);
                commit._rowAnchor = DeletionAnchor.For(commit._container, ctx.FiberStack.Current);
                commit.VisitRow(element);
                commit.TakeDownTheRest();
                return mounted;
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

        private void AddRoot(VisualElement element)
        {
            if (_roots.ContainsKey(element)) return;
            List<ComponentFiber>? mounted = null;
            Ctx.ComponentRegistry.CollectFibersIn(element, ref mounted);
            _roots[element] = mounted;
        }

        // The fibers mounted inside the roots, and a Portal's under them: its rows, which mount on its target and
        // become roots too, reached at its placeholder (IsRoot), and its top-level children, which mount on the
        // target among the rows. A Portal inside those rows is found by the next round.
        private void CollectHeldFibers()
        {
            List<ComponentFiber>? owned = null;
            // MUTANT_SURVIVES(equivalent, boundary): a round over an empty table reaches no placeholder and ends.
            var reached = Ctx.PortalState.Count > 0;
            while (reached)
            {
                reached = false;
                foreach (var (placeholder, portal) in Ctx.PortalState)
                {
                    if (_portalsUnderRoots.Contains(placeholder) || !IsUnderARoot(placeholder)) continue;
                    _portalsUnderRoots.Add(placeholder);
                    reached = true;
                    Ctx.ComponentRegistry.CollectInlineFibersOwnedByPortal(placeholder, ref owned);
                    if (portal.Target == null) continue;
                    var rows = RenderedChildrenOf(portal.Target);
                    for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength && slot < rows.Count; slot++)
                    {
                        AddRowAsRoot(rows[slot]);
                    }
                }
            }
            foreach (var mounted in _roots.Values) AddHeld(mounted);
            AddHeld(owned);
        }

        private void AddHeld(List<ComponentFiber>? held)
        {
            if (held == null) return;
            foreach (var fiber in held)
            {
                if (!fiber.IsDisposed && _collected.Add(fiber)) _fibers.Add(fiber);
            }
        }

        private bool IsUnderARoot(VisualElement element)
        {
            var ve = element;
            while (ve != null && !_roots.ContainsKey(ve)) ve = ve.hierarchy.parent;
            return ve != null;
        }

        // An orphan mounted in the container starts at the old leaf at its slot, and one whose slot holds none of the
        // container's old leaves is taken down after the walk. Every other fiber is placed by its mount.
        private void PlaceDeleted(ComponentFiber fiber, int oldLength)
        {
            if (_occupants == null || !ReferenceEquals(fiber.MountPoint, _container) || !_orphans.Contains(fiber))
            {
                _startsInsideARow = true;
                Place(fiber);
                return;
            }
            var index = fiber.MountSlotStart - _slotStart;
            var row = index >= 0 && index < oldLength ? _occupants.Occupant(index) : null;
            if (row == null) return;
            var start = Ctx.ZLayerPlaceholders.TryGetValue(row, out var real) ? real : row;
            if (PlaceAt(start, fiber)) _orphanRows.Add((index, start));
        }

        // A removed row goes unwalked only where nothing in it can be taken down after the fibers that start at the row
        // itself: no fiber is placed inside a row, and the context holds no ref, no relocated element and no Portal.
        private bool WalksRows()
        {
            // MUTANT_SURVIVES(equivalent, boundary): walking a row that needs no walk takes down nothing more.
            // It detaches no ref, and takes down only the fibers that start at the row, as the row's own takedown does.
            if (_startsInsideARow || Ctx.RefCallbacks.Count > 0) return true;
            // MUTANT_SURVIVES(equivalent, boundary): walking a row that needs no walk takes down nothing more.
            return Ctx.ZLayerPlaceholders.Count > 0 || Ctx.PortalState.Count > 0;
        }

        // A fiber the walk reached no element for, an orphan holding no row behind the container's last one among them,
        // taken down after the walk with any of the deletion's fibers above it ahead of it. A fiber taken down already
        // runs nothing.
        private void TakeDownTheRest()
        {
            foreach (var fiber in _fibers) TakeDownAncestorsFirst(fiber);
        }

        private void TakeDownAncestorsFirst(ComponentFiber fiber)
        {
            if (fiber.Parent != null && _collected.Contains(fiber.Parent)) TakeDownAncestorsFirst(fiber.Parent);
            TakeDown(fiber);
        }

        // The deleted row's place in React's tree: the outermost orphan holding it is the deletion's root, and a row no
        // orphan holds is a deletion of its own, under the component that emitted it.
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
            var orphan = OutermostOrphanAt(fiber);
            var anchor = orphan != null ? DeletionAnchor.For(_container, orphan.Parent) : _rowAnchor;
            FiberEffects.CleanUpDeleted(fiber, anchor);
        }

        protected override void TakeDown(VisualElement element) => _cleaner.DetachRef(element);

        protected override bool IsRoot(VisualElement element) => _roots.ContainsKey(element);

        protected override void Clear()
        {
            base.Clear();
            // MUTANT_SURVIVES(equivalent): Begin assigns all three before any read.
            // The resets only let the pooled instance drop what it pointed at.
            _cleaner = null!;
            // MUTANT_SURVIVES(equivalent): Begin assigns the container before any read.
            _container = null;
            // MUTANT_SURVIVES(equivalent): Begin assigns the occupants before any read.
            _occupants = null;
            _removedRows.Clear();
            _orphanRows.Clear();
            ClearAndTrim(_roots);
            ClearAndTrim(_portalsUnderRoots);
            ClearAndTrim(_orphans);
            ClearAndTrim(_collected);
            _fibers.Clear();
            // MUTANT_SURVIVES(equivalent): a walk left flagged walks rows that need no walk (WalksRows).
            _startsInsideARow = false;
            // MUTANT_SURVIVES(equivalent): a walk assigns it before it takes down any fiber that reads it.
            _rowAnchor = default;
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
        internal DeletedPassiveCleanup(ComponentFiber fiber, ComponentFiber? catchFrom, List<Action> cleanups,
            DeletionAnchor anchor, Reconciler? pass)
        {
            Fiber = fiber;
            CatchFrom = catchFrom;
            Cleanups = cleanups;
            Anchor = anchor;
            Pass = pass;
        }

        internal ComponentFiber Fiber { get; }

        internal ComponentFiber? CatchFrom { get; }

        internal List<Action> Cleanups { get; }

        internal DeletionAnchor Anchor { get; }

        // ReconcilerContext.CurrentPass as the fiber was deleted.
        internal Reconciler? Pass { get; }

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
                // MUTANT_SURVIVES(equivalent): a tree kept from its pool only makes the next Rent build a new one.
                // That one reads the context the same way.
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
