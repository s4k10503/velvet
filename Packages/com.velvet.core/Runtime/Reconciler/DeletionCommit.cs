using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // React's commitDeletionEffectsOnFiber over what one container's reconcile deletes, or over what leaves outside
    // one: the pre-order walk of PreOrderTeardown, in which a component's cleanups run where the walk reaches
    // the component (FiberEffects.CleanUpDeleted), and an element's ref detaches where the walk reaches the element. It
    // runs ahead of the removals, which find neither left to run.
    internal sealed class DeletionCommit : PreOrderTeardown
    {
        // An instance per walk on the stack, so a cleanup that commits another walk takes an instance of its own.
        private static readonly Stack<DeletionCommit> s_pool = new();

        private FiberElementCleaner _cleaner = null!;
        private VisualElement? _container;
        private int _slotStart;
        private OldSlotOccupants? _occupants;
        private readonly List<(int Index, VisualElement Row)> _removedRows = new();
        // The old leaves an orphan's output starts at, each with the element the walk takes the orphan down at.
        private readonly List<(int Index, VisualElement Start)> _orphanRows = new();
        // Each root with the fibers mounted under it, which its removal disposes (FiberElementCleaner.CleanupElement).
        private readonly Dictionary<VisualElement, List<ComponentFiber>?> _roots = new();
        private readonly HashSet<VisualElement> _portalsUnderRoots = new();
        private readonly HashSet<ComponentFiber> _orphans = new();
        private readonly HashSet<ComponentFiber> _collected = new();
        private readonly List<ComponentFiber> _fibers = new();
        private bool _startsInsideARow;

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

        internal void AddRemovedRow(int index, VisualElement row) => _removedRows.Add((index, row));

        // The old leaves AddRemovedRow named, in the order it named them.
        internal List<(int Index, VisualElement Row)> RemovedRows => _removedRows;

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
            foreach (var (_, row) in _removedRows) AddRowAsRoot(row);
            CollectHeldFibers();
            ComponentFiber.BeginDeletion(_fibers);
            foreach (var fiber in _fibers) PlaceDeleted(fiber, oldLength);
            _orphanRows.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            var walksRows = WalksRows();
            var next = 0;
            foreach (var (index, row) in _removedRows)
            {
                for (; next < _orphanRows.Count && _orphanRows[next].Index <= index; next++)
                {
                    // MUTANT_SURVIVES(equivalent, boundary): an orphan starting at a removed row goes first either way.
                    // The row's visit takes down what starts at the row ahead of the row's ref, and a z-layer
                    // placeholder visited ahead of it holds no ref (FiberZLayerCoordinator.CreatePlaceholder).
                    if (_orphanRows[next].Index < index) TakeDownStartingAt(_orphanRows[next].Start);
                }
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
            s_pool.Push(this);
        }

        // An element leaving outside a container reconcile's deletion, or a Portal placeholder whose children leave
        // its target (FiberElementCleaner.ReleasePortalRangeForRetarget), for which it returns the fibers mounted under
        // the element.
        internal static List<ComponentFiber>? TakeDownLeaving(ReconcilerContext ctx, FiberElementCleaner cleaner,
            VisualElement element)
        {
            var commit = Begin(ctx, cleaner, element.hierarchy.parent, slotStart: 0, occupants: null);
            try
            {
                commit.AddRowAsRoot(element);
                commit.CollectHeldFibers();
                var mounted = commit._roots[element];
                // With no fiber held below the element and no ref in the context, the walk has nothing to take down.
                if (commit._fibers.Count == 0 && ctx.RefCallbacks.Count == 0) return mounted;
                ComponentFiber.BeginDeletion(commit._fibers);
                foreach (var fiber in commit._fibers) commit.Place(fiber);
                commit.VisitRow(element);
                // MUTANT_SURVIVES(equivalent): no EventLog reading tells a fiber this walk misses taken down here from
                // it taken down later in the same teardown (FiberElementCleaner.CleanupElement), which reaches it by
                // the list returned below or by the Portal it belongs to. Before that the teardown runs no other
                // cleanup and detaches no ref: the walk took down every other fiber and ref the element holds.
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
            // MUTANT_SURVIVES(equivalent, guard removed): a root collected again collects the same fibers.
            // Nothing is disposed while the roots are collected, and the new list replaces the entry where it stands.
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
                // MUTANT_SURVIVES(equivalent, clause removed): a fiber held twice is placed and taken down twice.
                // That happens to a Portal's child when the Portal's target is inside a removed element: its rows are
                // roots inside another root, and its children are held by both. The second CleanUpDeleted finds
                // nothing left (FiberEffects.CleanUpDeleted), and BeginDeletion and the placement keep the first.
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

        protected override void TakeDown(ComponentFiber fiber) => FiberEffects.CleanUpDeleted(fiber);

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
        }
    }
}
