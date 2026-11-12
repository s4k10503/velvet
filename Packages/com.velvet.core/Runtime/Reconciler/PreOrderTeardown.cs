using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The pre-order walk React's commit takes over a subtree it takes down — disappearLayoutEffects for a tree a
    // Suspense hides (SuspenseOffscreenCommit), commitDeletionEffectsOnFiber for a deleted one (DeletionCommit): a
    // component is reached ahead of the element its output starts at (RenderedChildrenCache.StartOf), enclosing ones
    // ahead of those inside them, then the element itself, then what is inside it. A Portal's children and an element
    // FiberZLayerCoordinator relocated are reached where they are declared.
    internal abstract class PreOrderTeardown
    {
        protected ReconcilerContext Ctx = null!;
        // Each place the walk takes fibers down at maps to the first of them, chained through _placed, so a walk
        // placing one fiber at each of many rows takes no list per row.
        private readonly Dictionary<VisualElement, int> _startingAt = new();
        private readonly Dictionary<VisualElement, int> _endingIn = new();
        private readonly Dictionary<VisualElement, int> _endingInPortal = new();
        private readonly List<Placed> _placed = new();
        private readonly RenderedChildrenCache _rendered = new();
        private readonly HashSet<VisualElement> _visited = new();
        private readonly List<List<VisualElement>> _childrenAtDepth = new();
        private int _depth;

        protected abstract void TakeDown(ComponentFiber fiber);

        protected abstract void TakeDown(VisualElement element);

        // A root of the walk is visited as one, so where it is also the child of another root it is passed over there.
        protected abstract bool IsRoot(VisualElement element);

        // Whether the walk goes on past what starts at the element it is visiting, into the element and below it.
        protected virtual bool WalksOn() => true;

        protected List<VisualElement> RenderedChildrenOf(VisualElement container) => _rendered.Of(container);

        protected void Place(ComponentFiber fiber)
        {
            var mount = fiber.MountPoint;
            if (mount == null) return;
            var owning = fiber.OwningPortalPlaceholder;
            // A child of a Portal holding no row at the end of the Portal's rows is taken down where the walk leaves
            // them: the target's next row there belongs to something else, if the target holds one.
            if (owning != null
                && Ctx.PortalState.TryGetValue(owning, out var portal)
                && ReferenceEquals(portal.Target, mount)
                && fiber.MountSlotStart >= portal.SlotStart + portal.SlotLength)
            {
                AddInTreeOrder(_endingInPortal, owning, fiber);
                return;
            }
            var rendered = _rendered.Of(mount);
            var slot = fiber.MountSlotStart;
            // A fiber holding no row in front of another Portal's row in its container sits behind the container's
            // own children, where the walk ends the container: that row is reached at its Portal.
            if (slot < 0 || slot >= rendered.Count || (fiber.MountSlotCount == 0 && IsAnotherPortalsRow(owning, mount, slot)))
            {
                AddInTreeOrder(_endingIn, mount, fiber);
                return;
            }
            // At the element a placeholder stands for, which is what the walk reaches: as a root, as a Portal's row,
            // or right after the placeholder.
            var start = rendered[slot];
            PlaceAt(Ctx.ZLayerPlaceholders.TryGetValue(start, out var real) ? real : start, fiber);
        }

        // Whether fiber is the first placed at start.
        protected bool PlaceAt(VisualElement start, ComponentFiber fiber) => AddInTreeOrder(_startingAt, start, fiber);

        private bool IsAnotherPortalsRow(VisualElement? owning, VisualElement container, int slot)
        {
            foreach (var (placeholder, portal) in Ctx.PortalState)
            {
                if (!ReferenceEquals(portal.Target, container)) continue;
                if (slot < portal.SlotStart || slot >= portal.SlotStart + portal.SlotLength) continue;
                return !ReferenceEquals(placeholder, owning);
            }
            return false;
        }

        private struct Placed
        {
            public ComponentFiber Fiber;
            public int Next;
        }

        // The fibers taken down at one place, enclosing ones ahead of those inside them and siblings in their order:
        // each goes behind the last one already there that it does not precede.
        private bool AddInTreeOrder(Dictionary<VisualElement, int> chains, VisualElement at, ComponentFiber fiber)
        {
            var index = _placed.Count;
            var first = !chains.TryGetValue(at, out var head);
            var after = -1;
            for (var i = first ? -1 : head; i >= 0; i = _placed[i].Next)
            {
                if (!PrecedesInPreOrder(fiber, _placed[i].Fiber)) after = i;
            }
            _placed.Add(new Placed { Fiber = fiber, Next = after >= 0 ? _placed[after].Next : first ? -1 : head });
            if (after >= 0) SetNext(after, index);
            else chains[at] = index;
            return first;
        }

        private void SetNext(int at, int next)
        {
            var placed = _placed[at];
            placed.Next = next;
            _placed[at] = placed;
        }

        private static bool PrecedesInPreOrder(ComponentFiber a, ComponentFiber b)
        {
            for (var ancestor = b.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, a)) return true;
            }
            return FiberEffects.PrecedesInTreeOrder(a, b);
        }

        protected void Visit(VisualElement element)
        {
            // MUTANT_SURVIVES(equivalent, guard removed): a second visit takes nothing down, since each TakeDown returns
            // for a fiber or an element it has taken down already.
            if (!_visited.Add(element)) return;
            TakeDownStartingAt(element);
            if (!WalksOn()) return;
            TakeDown(element);
            if (_depth == _childrenAtDepth.Count) _childrenAtDepth.Add(new List<VisualElement>());
            var children = _childrenAtDepth[_depth++];
            try
            {
                // Copied first: a cleanup can change the children.
                var hierarchy = element.hierarchy;
                for (var i = 0; i < hierarchy.childCount; i++) children.Add(hierarchy[i]);
                foreach (var child in children)
                {
                    // Reached where it is declared instead: a relocated element at its placeholder, and a Portal's row,
                    // which the walk made a root of its own, at its Portal, relocated or not.
                    if (IsRoot(child) || FiberZLayerCoordinator.PlaceholderOf(Ctx, child) != null) continue;
                    if (Ctx.ZLayerPlaceholders.TryGetValue(child, out var real))
                    {
                        if (IsRoot(real)) continue;
                        // MUTANT_SURVIVES(equivalent): a z-layer placeholder takes nothing down when visited.
                        // It is created empty and holds no ref (FiberZLayerCoordinator.CreatePlaceholder), and a fiber
                        // starting at it is placed at the element it stands for (Place, DeletionCommit.PlaceDeleted).
                        Visit(child);
                        Visit(real);
                        continue;
                    }
                    Visit(child);
                }
            }
            finally
            {
                children.Clear();
                // MUTANT_SURVIVES(equivalent): an unbalanced depth hands each later visit a list of its own.
                // Each is cleared as this one is, so the walk differs only in the lists it holds.
                _depth--;
            }
            VisitPortalRows(element);
            TakeDownEndingIn(element);
        }

        // The rows GeneralPathReconciler.AddWithPortalChildren makes roots of. A Portal whose registered target is
        // missing has an entry with no target, and no rows.
        private void VisitPortalRows(VisualElement placeholder)
        {
            if (!Ctx.PortalState.TryGetValue(placeholder, out var portal)) return;
            if (portal.Target != null)
            {
                var rows = _rendered.Of(portal.Target);
                for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength && slot < rows.Count; slot++)
                {
                    var row = rows[slot];
                    Visit(Ctx.ZLayerPlaceholders.TryGetValue(row, out var real) ? real : row);
                }
            }
            TakeDownFrom(_endingInPortal, placeholder);
        }

        // What starts at an element the walk does not descend into, a row a deletion keeps among them.
        protected void TakeDownStartingAt(VisualElement element) => TakeDownFrom(_startingAt, element);

        protected void TakeDownEndingIn(VisualElement container) => TakeDownFrom(_endingIn, container);

        private void TakeDownFrom(Dictionary<VisualElement, int> chains, VisualElement at)
        {
            if (!chains.TryGetValue(at, out var head)) return;
            for (var i = head; i >= 0; i = _placed[i].Next) TakeDown(_placed[i].Fiber);
        }

        protected virtual void Clear()
        {
            // MUTANT_SURVIVES(equivalent): DeletionCommit.Begin and SuspenseOffscreenCommit.Apply assign it first.
            // The reset only lets the pooled instance drop the context it pointed at.
            Ctx = null!;
            ClearAndTrim(_startingAt);
            ClearAndTrim(_endingIn);
            ClearAndTrim(_endingInPortal);
            ClearAndTrim(_placed);
            _rendered.Clear();
            ClearAndTrim(_visited);
        }

        // An instance is pooled for the rest of the domain, so a table ClearAndTrim clears gives back its capacity
        // once a walk has filled it past this many entries, and keeps it for the next walk otherwise.
        private const int CapacityKept = 4096;

        protected static void ClearAndTrim<T>(List<T> list)
        {
            // MUTANT_SURVIVES(equivalent, boundary): TrimExcess changes the capacity, which no walk reads.
            var trim = list.Count > CapacityKept;
            list.Clear();
            // MUTANT_SURVIVES(equivalent): TrimExcess changes the capacity, which no walk reads.
            if (trim) list.TrimExcess();
        }

        protected static void ClearAndTrim<T>(HashSet<T> set)
        {
            // MUTANT_SURVIVES(equivalent, boundary): TrimExcess changes the capacity, which no walk reads.
            var trim = set.Count > CapacityKept;
            set.Clear();
            // MUTANT_SURVIVES(equivalent): TrimExcess changes the capacity, which no walk reads.
            if (trim) set.TrimExcess();
        }

        protected static void ClearAndTrim<TKey, TValue>(Dictionary<TKey, TValue> table)
        {
            // MUTANT_SURVIVES(equivalent, boundary): TrimExcess changes the capacity, which no walk reads.
            var trim = table.Count > CapacityKept;
            table.Clear();
            // MUTANT_SURVIVES(equivalent): TrimExcess changes the capacity, which no walk reads.
            if (trim) table.TrimExcess();
        }
    }
}
