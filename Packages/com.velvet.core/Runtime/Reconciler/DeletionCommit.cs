using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // React's commitDeletionEffectsOnFiber over what one container's reconcile deletes, or over what leaves outside
    // one: the pre-order walk of PreOrderTeardown, which records a component's cleanups
    // (FiberEffects.CleanUpDeleted) and an element's ref detach where it reaches them, and RunPlan runs them in that
    // order. It runs ahead of the removals, which find neither left to run, or, where the pass records its writes, at its
    // owner's turn among them (Teardown).
    internal sealed class DeletionCommit : PreOrderTeardown
    {
        // An instance per walk on the stack, so a cleanup that commits another walk takes an instance of its own.
        private static readonly Stack<DeletionCommit> s_pool = new();

        private FiberElementCleaner _cleaner = null!;
        private VisualElement? _container;
        private int _slotStart;
        private OldSlotOccupants? _occupants;
        // Each with the component whose output held it (ChildListWrites.RemovalOwner).
        private readonly List<(int Index, VisualElement Row, ComponentFiber? Owner)> _removedRows = new();
        // The old leaves an orphan's output starts at, each with the element the walk takes the orphan down at.
        private readonly List<(int Index, VisualElement Start)> _orphanRows = new();
        // What the walk decides, which the removals after it read (FiberElementCleaner.CleanupElement).
        private DeletionPlan _deletion = null!;
        private readonly HashSet<VisualElement> _portalsUnderRoots = new();
        // The roots in the order they were added, which CollectHeldFibers walks, the Portals one of its rounds met, and
        // the rows it read of one of them.
        private readonly List<VisualElement> _rootsInOrder = new();
        private readonly List<VisualElement> _metPortals = new();
        private readonly List<VisualElement?> _portalRowsRead = new();
        // The rows of the Portals DetachPortals detached, each with the target it is a row of and the component whose
        // output held the removed row the Portal sat in, from the first row of each Portal to its last, which is the order
        // Queue records their removals in.
        private readonly List<(VisualElement Target, VisualElement Row, ComponentFiber? Owner)> _portalRows = new();
        private readonly HashSet<ComponentFiber> _orphans = new();
        private readonly HashSet<ComponentFiber> _collected = new();
        private readonly List<ComponentFiber> _fibers = new();
        private bool _startsInsideARow;
        // The removed rows registered for Teardown before their removals are recorded, in the order registered.
        private readonly List<(VisualElement Parent, VisualElement Element, VisualElement Poolable, VisualElement? DiscardedRoot,
            ComponentFiber? Owner)> _cleanups = new();
        // Where the deletion's teardown runs among the pass's recorded writes: the turn of the fiber or construct that owns it.
        private TurnAnchor? _anchor;
        private bool _deferred;
        private bool _queued;
        private bool _executed;
        private bool _walkOver;

        // A container reconcile's deletion: Begin, each old leaf it removes in order, Run, and End after the removals,
        // or where an abort skipped them. Occupants are the container's old leaves, the first at slotStart.
        //
        // A deletion that a pass records its writes for runs at its owner's turn (Queue): the nearest fiber or construct of
        // the walk that kept what it deleted, or the container's own where there is none. A container whose deletions that
        // wait for the apply have several owners holds one DeletionCommit for each, chained through NextGroup.
        internal static DeletionCommit Begin(ReconcilerContext ctx, FiberElementCleaner cleaner, VisualElement? container,
            int slotStart, OldSlotOccupants? occupants, TurnAnchor? anchor = null)
        {
            var commit = s_pool.Count > 0 ? s_pool.Pop() : new DeletionCommit();
            commit.Ctx = ctx;
            commit._cleaner = cleaner;
            commit._container = container;
            commit._slotStart = slotStart;
            commit._occupants = occupants;
            commit._anchor = anchor;
            commit._deletion = DeletionPlan.Rent();
            return commit;
        }

        // The deletion of the same container that runs at another owner's turn, if the container has one.
        internal DeletionCommit? NextGroup;
        // The orphans of the container that this deletion takes down where it is one of several (GeneralPathReconciler.TakeDownTheDeletion).
        internal readonly List<ComponentFiber> OwnOldFibers = new();
        // The fibers Run was given, which hold the orphans this deletion takes down.
        internal List<ComponentFiber>? OldFibers => _oldFibers;
        private List<ComponentFiber>? _oldFibers;
        internal DeletionPlan Plan => _deletion;

        internal void AddRemovedRow(int index, VisualElement row, ComponentFiber? owner) => _removedRows.Add((index, row, owner));

        // The old leaves AddRemovedRow named, in the order it named them.
        internal List<(int Index, VisualElement Row, ComponentFiber? Owner)> RemovedRows => _removedRows;

        // React deletes a parent's children in the order it reconciled them, so the rows are taken in theirs, each
        // removed row as a subtree, and an orphan whose row stays where that row is. The plan is built by BuildPlan, and
        // executed as soon as Settle says it does not wait, or queued for Teardown, and the removals read what it holds
        // of each root.
        internal void Settle(bool deferred)
        {
            _deferred = deferred;
            if (_deferred) DetachPortals();
            else Execute();
        }

        // Whether the deletion runs where the pass applies its recorded writes (Teardown) rather than where it is decided.
        // It keeps the immediate path where a fiber's unmount reconciles its own rows (a wrapper-mounted fiber with a
        // tree), where a Portal under a root has the container itself for its target, whose range follows the
        // container's own rows (HostCommitQueue.FollowOwnRows), and where a Portal's target is inside a root, which
        // takes its rows with it.
        internal bool CanDefer()
        {
            foreach (var placeholder in _portalsUnderRoots)
            {
                if (!Ctx.PortalState.TryGetValue(placeholder, out var portal)) continue;
                var target = portal.Target;
                if (target != null && (ReferenceEquals(target, _container) || IsUnderARoot(target))) return false;
            }
            foreach (var fiber in _fibers)
            {
                if (!fiber.IsInlineMounted && fiber.MountPoint != null && fiber.PreviousTree != null) return false;
            }
            return true;
        }

        // What CleanupPortal does to the model for each mounted Portal under a root, done where the deletion decides so a
        // later row of the walk reads the slots the removal leaves: the entry leaves PortalState, the ranges and tenants
        // behind its range move up, and its rows, which the plan holds for the cleanup of its placeholder, are recorded
        // as removed by Queue behind the Teardown. The rows are read before any range moves.
        private void DetachPortals()
        {
            foreach (var placeholder in _portalsUnderRoots)
            {
                if (!Ctx.PortalState.TryGetValue(placeholder, out var portal)) continue;
                var target = portal.Target;
                if (target == null) continue;
                var rendered = RenderedChildrenOf(target);
                var rows = new List<VisualElement>(portal.SlotLength);
                for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength; slot++)
                {
                    if (slot < rendered.Count) rows.Add(rendered[slot]);
                }
                _deletion.Portals[placeholder] = new DeletionPlan.DetachedPortal(target, rows);
            }
            foreach (var (placeholder, detached) in _deletion.Portals)
            {
                // The range as recorded, not as its rows place it (PortalRangeTable): the rows of the ranges removed before
                // this one are still in the model, and the shifts for them have moved the ranges behind.
                var portal = Ctx.PortalState.Stored[placeholder];
                Ctx.PortalState.Remove(placeholder);
                if (portal.SlotLength > 0 && Ctx.SharedReconcileDepth > 0) Ctx.PortalTargetsToRestyle.Add(detached.Target);
                Ctx.HostCommit.ShiftBehindPortalRange(detached.Target, placeholder, portal, -portal.SlotLength);
                var owner = OwnerOfTheRemovedRowHolding(placeholder);
                foreach (var row in detached.Rows) _portalRows.Add((detached.Target, row, owner));
            }
        }

        // The owner of the removed row element sits in, or of the Portal row it sits in, where DetachPortals has met that
        // Portal; null where neither is met. A relocated element stands beside its placeholder rather than under it, and
        // the placeholder is the row, so the walk goes on from there.
        private ComponentFiber? OwnerOfTheRemovedRowHolding(VisualElement element)
        {
            for (VisualElement? ve = element; ve != null; ve = ChildListWrites.ParentOf(ve))
            {
                ve = FiberZLayerCoordinator.PlaceholderOf(Ctx, ve) ?? ve;
                foreach (var (_, row, owner) in _removedRows)
                {
                    if (ReferenceEquals(row, ve)) return owner;
                }
                foreach (var (_, row, owner) in _portalRows)
                {
                    if (ReferenceEquals(row, ve)) return owner;
                }
            }
            return null;
        }

        // Whether the deletion's cleanups and disposals wait for Teardown, so the walk records the removals of its rows
        // (AddCleanup) instead of making them.
        internal bool DefersTeardown => _deferred;

        // Registers a removed row of the container for Teardown to clean up, where it is still the container's child, with
        // the component whose output held it.
        internal void AddCleanup(VisualElement parent, VisualElement element, ComponentFiber? owner)
        {
            if (ChildListWrites.PhysicalIndexOf(parent, element) < 0) return;
            var (poolable, discardedRoot) = _cleaner.PlanRemoval(element, _deletion);
            _cleanups.Add((parent, element, poolable, discardedRoot, owner));
        }

        internal List<(VisualElement Parent, VisualElement Element, VisualElement Poolable, VisualElement? DiscardedRoot,
            ComponentFiber? Owner)> Cleanups => _cleanups;

        // Hides the fibers the deletion disposes and queues Teardown at its owner's turn among the writes recorded so far,
        // ahead of the removals of the rows registered by AddCleanup and of the Portals' rows (DetachPortals), which follow
        // at once.
        internal void Queue()
        {
            if (!_deferred || _queued) return;
            _queued = true;
            Ctx.ComponentRegistry.HideFibers(_fibers);
            if (!ChildListWrites.RecordTeardown(this, _anchor)) Teardown();
            foreach (var (target, row, owner) in _portalRows) Ctx.HostCommit.RemoveRow(target, row, owner);
        }

        // Where the deletion's walk is over. One that was never queued is now, so its fibers do not wait for a teardown
        // that is not coming. The walk and Teardown each end the commit if they come last: an apply the walk's own
        // writes reach can run Teardown before the walk is over.
        internal void Complete()
        {
            _walkOver = true;
            if (!_deferred) End();
            else if (!_queued) Queue();
            else if (_executed) End();
        }

        // What the recorded writes up to this one made room for: the plan's take-downs, the cleanup of each registered
        // row, then the fibers' disposal. Each runs where the one before it threw, and the first exception is rethrown
        // after them. An error a cleanup throws is captured for its boundary (CommitErrors.Capture), as one thrown in
        // the layout commit is, which TeardownDepth selects.
        internal void Teardown()
        {
            // MUTANT_SURVIVES(equivalent, guard removed): Apply runs each op once, and Queue records the op only the first time it is asked.
            if (_executed) return;
            _executed = true;
            var ctx = Ctx;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? first = null;
            ctx.TeardownDepth++;
            try
            {
                try
                {
                    Execute();
                }
                catch (System.Exception exception)
                {
                    first ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                }
                foreach (var (_, element, poolable, discardedRoot, owner) in _cleanups)
                {
                    try
                    {
                        // The owner of what this cleanup removes: the removals it records, as a V.VirtualList inside the
                        // row clears its rows, and a relocated element it retires, which leaves after the pass
                        // (FiberElementCleaner.ReleaseRetiredZLayerReals).
                        using var removing = new ChildListWrites.RemovalOwnerScope(owner);
                        _cleaner.CleanupRemovedRow(element, poolable, discardedRoot, _deletion);
                    }
                    catch (System.Exception exception)
                    {
                        first ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                    }
                }
                // The fibers the rows' cleanups did not reach are disposed too, orphans first, since a hidden fiber nothing
                // disposes is registered nowhere and leaks its subscriptions.
                DisposeAll(_fibers, ref first);
            }
            finally
            {
                ctx.TeardownDepth--;
                if (_walkOver) End();
            }
            first?.Throw();
        }

        private void DisposeAll(List<ComponentFiber> fibers, ref System.Runtime.ExceptionServices.ExceptionDispatchInfo? first)
        {
            foreach (var fiber in fibers)
            {
                // MUTANT_SURVIVES(equivalent, guard removed): FiberRenderer.Dispose returns at a disposed fiber, and what DisposeFiber and Hide do after it remove by the fiber as a key.
                if (fiber.IsDisposed) continue;
                try
                {
                    Ctx.ComponentRegistry.DisposeAndRemove(fiber);
                }
                catch (System.Exception exception)
                {
                    first ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                }
            }
        }

        // The walk alone: what it reaches is recorded and nothing is taken down, though the fibers it reaches are marked
        // as being deleted (ComponentFiber.BeginDeletion).
        internal DeletionPlan BuildPlan(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers, int oldLength)
        {
            Collect(oldFibers, newFibers);
            return Order(oldLength);
        }

        // What the walk reaches, which CanDefer reads and the walk itself does not change: the orphans, the roots of the
        // removed rows and the fibers held under them.
        internal void Collect(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers)
        {
            _oldFibers = oldFibers;
            foreach (var fiber in oldFibers)
            {
                if (newFibers.Contains(fiber) || !_orphans.Add(fiber)) continue;
                _collected.Add(fiber);
                _fibers.Add(fiber);
            }
            foreach (var (_, row, _) in _removedRows) AddRowAsRoot(row);
            CollectHeldFibers();
        }

        // The walk over what Collect reached, in React's order.
        internal DeletionPlan Order(int oldLength)
        {
            ComponentFiber.BeginDeletion(_fibers);
            foreach (var fiber in _fibers) PlaceDeleted(fiber, oldLength);
            _orphanRows.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            var walksRows = WalksRows();
            var next = 0;
            foreach (var (index, row, _) in _removedRows)
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
            return _deletion;
        }

        internal void Execute() => RunPlan();

        internal void End()
        {
            Clear();
            s_pool.Push(this);
        }

        // An element leaving outside a container reconcile's deletion, or a Portal placeholder whose children leave
        // its target (FiberElementCleaner.ReleasePortalRangeForRetarget), for which it returns the fibers mounted under
        // the element.
        internal static List<ComponentFiber>? TakeDownLeaving(ReconcilerContext ctx, FiberElementCleaner cleaner,
            VisualElement element)
        {
            var commit = Begin(ctx, cleaner, ChildListWrites.ParentOf(element), slotStart: 0, occupants: null);
            try
            {
                commit.AddRowAsRoot(element);
                commit.CollectHeldFibers();
                var mounted = commit._deletion.Roots[element].Mounted;
                // With no fiber held below the element and no ref in the context, the walk has nothing to take down.
                if (commit._fibers.Count == 0 && ctx.RefCallbacks.Count == 0) return mounted;
                ComponentFiber.BeginDeletion(commit._fibers);
                foreach (var fiber in commit._fibers) commit.Place(fiber);
                commit.VisitRow(element);
                commit.TakeDownTheRest();
                commit.RunPlan();
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
            // Its second walk meets only Portals the first one met, which are taken once.
            if (_deletion.Roots.ContainsKey(element)) return;
            List<ComponentFiber>? mounted = null;
            Ctx.ComponentRegistry.CollectFibersIn(element, ref mounted);
            _deletion.Roots[element] = new DeletionPlan.PlannedRoot(mounted, _cleaner.PoolableOccupantOf(element));
            _rootsInOrder.Add(element);
        }

        // The fibers mounted inside the roots, and a Portal's under them: its rows, which mount on its target and
        // become roots too, reached at its placeholder (IsRoot), and its top-level children, which mount on the
        // target among the rows. A Portal inside those rows is found by the next round. The Portals are met by
        // walking the roots, so the cost is what the roots hold rather than every Portal placed. A round meets them in
        // tree order, which is the order DetachPortals lets their rows go in, as React takes the sibling Portals of a
        // deleted subtree.
        private void CollectHeldFibers()
        {
            List<ComponentFiber>? owned = null;
            // MUTANT_SURVIVES(equivalent, boundary): a walk over an empty table meets no placeholder.
            if (Ctx.PortalState.Count > 0)
            {
                for (var walked = 0; walked < _rootsInOrder.Count;)
                {
                    for (var end = _rootsInOrder.Count; walked < end; walked++) MeetPortalsUnder(_rootsInOrder[walked]);
                    foreach (var placeholder in _metPortals)
                    {
                        Ctx.ComponentRegistry.CollectInlineFibersOwnedByPortal(placeholder, ref owned);
                        if (!Ctx.PortalState.TryGetValue(placeholder, out var portal) || portal.Target == null) continue;
                        // By slot rather than through RenderedChildrenOf: a removal outside a deletion's plan makes a walk
                        // for each element it takes down (TakeDownLeaving), and building the target's whole list for each
                        // costs every row of every Portal sharing the target.
                        _portalRowsRead.Clear();
                        ChildListWrites.Rows(portal.Target, portal.SlotStart, portal.SlotStart + portal.SlotLength, _portalRowsRead);
                        foreach (var row in _portalRowsRead) AddRowAsRoot(row!);
                    }
                    _metPortals.Clear();
                }
            }
            foreach (var root in _deletion.Roots.Values) AddHeld(root.Mounted);
            AddHeld(owned);
        }

        // An element FiberZLayerCoordinator relocated sits where it sorted it in its parent's list, so it is met where its
        // placeholder stands instead, as PreOrderTeardown.Visit reaches it.
        private void MeetPortalsUnder(VisualElement element)
        {
            if (Ctx.PortalState.ContainsKey(element) && _portalsUnderRoots.Add(element)) _metPortals.Add(element);
            var count = ChildListWrites.HierarchyCount(element);
            for (var i = 0; i < count; i++)
            {
                var child = ChildListWrites.HierarchyChild(element, i);
                if (FiberZLayerCoordinator.PlaceholderOf(Ctx, child) != null) continue;
                MeetPortalsUnder(child);
                if (Ctx.ZLayerPlaceholders.TryGetValue(child, out var real)) MeetPortalsUnder(real);
            }
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
            while (ve != null && !_deletion.Roots.ContainsKey(ve)) ve = ChildListWrites.ParentOf(ve);
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
            Take(fiber);
        }

        protected override bool PlansTakeDowns => true;

        // The insertion and layout cleanups of a deletion Teardown runs count as a commit, as a layout commit's do, and the
        // passive ones run outside it.
        protected override void TakeDown(ComponentFiber fiber)
        {
            if (!_executed)
            {
                FiberEffects.CleanUpDeleted(fiber);
                return;
            }
            Ctx.EffectCommitDepth++;
            try
            {
                FiberEffects.CleanupAllInsertionEffects(fiber);
                FiberEffects.CleanupAllLayoutEffects(fiber);
            }
            finally
            {
                Ctx.EffectCommitDepth--;
            }
            FiberEffects.CleanupAllEffects(fiber);
        }

        protected override void TakeDown(VisualElement element) => _cleaner.DetachRef(element);

        protected override List<Step> Steps => _deletion.Steps;

        protected override bool IsRoot(VisualElement element) => _deletion.Roots.ContainsKey(element);

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
            _anchor = null;
            OwnOldFibers.Clear();
            NextGroup = null;
            _oldFibers = null;
            _removedRows.Clear();
            _orphanRows.Clear();
            _deletion.Release();
            // MUTANT_SURVIVES(equivalent): Begin assigns the plan before any read.
            // The reset only lets the pooled instance drop the plan it pointed at.
            _deletion = null!;
            ClearAndTrim(_portalsUnderRoots);
            ClearAndTrim(_rootsInOrder);
            ClearAndTrim(_metPortals);
            ClearAndTrim(_portalRowsRead);
            ClearAndTrim(_portalRows);
            ClearAndTrim(_orphans);
            ClearAndTrim(_collected);
            _fibers.Clear();
            _cleanups.Clear();
            // MUTANT_SURVIVES(equivalent): Run assigns the flag before anything reads it.
            _deferred = false;
            _queued = false;
            _executed = false;
            _walkOver = false;
            // MUTANT_SURVIVES(equivalent): a walk left flagged walks rows that need no walk (WalksRows).
            _startsInsideARow = false;
        }
    }
}
