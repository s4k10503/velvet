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
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _startingAt = new();
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _endingIn = new();
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _endingInPortal = new();
        private readonly Stack<List<ComponentFiber>> _spareFiberLists = new();
        private readonly RenderedChildrenCache _rendered = new();
        private readonly HashSet<VisualElement> _visited = new();
        private readonly List<List<VisualElement>> _childrenAtDepth = new();
        private int _depth;

        protected abstract void TakeDown(ComponentFiber fiber);

        protected abstract void TakeDown(VisualElement element);

        // A root of the walk is visited as one, so where it is also the child of another root it is passed over there.
        protected abstract bool IsRoot(VisualElement element);

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

        // The fibers taken down at one place, enclosing ones ahead of those inside them and siblings in their order.
        private bool AddInTreeOrder(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at,
            ComponentFiber fiber)
        {
            var first = !lists.TryGetValue(at, out var list);
            if (first)
            {
                list = _spareFiberLists.Count > 0 ? _spareFiberLists.Pop() : new List<ComponentFiber>();
                lists[at] = list;
            }
            var slot = list!.Count;
            while (slot > 0 && PrecedesInPreOrder(fiber, list[slot - 1])) slot--;
            list.Insert(slot, fiber);
            return first;
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

        private void TakeDownFrom(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at)
        {
            if (!lists.TryGetValue(at, out var fibers)) return;
            foreach (var fiber in fibers) TakeDown(fiber);
        }

        protected virtual void Clear()
        {
            Ctx = null!;
            ReturnLists(_startingAt);
            ReturnLists(_endingIn);
            ReturnLists(_endingInPortal);
            _rendered.Clear();
            ClearAndTrim(_visited);
        }

        private void ReturnLists(Dictionary<VisualElement, List<ComponentFiber>> lists)
        {
            foreach (var list in lists.Values)
            {
                list.Clear();
                // MUTANT_SURVIVES(equivalent, boundary): a list not kept spare only makes a later walk allocate one.
                if (_spareFiberLists.Count < SparesKept) _spareFiberLists.Push(list);
            }
            ClearAndTrim(lists);
        }

        // An instance is pooled for the rest of the domain, so a walk far larger than the usual one gives back what
        // it grew to rather than leaving the pool holding it.
        private const int SparesKept = 64;

        protected static void ClearAndTrim<T>(HashSet<T> set)
        {
            // MUTANT_SURVIVES(equivalent, boundary): TrimExcess changes the capacity, which no walk reads.
            var trim = set.Count > SparesKept;
            set.Clear();
            // MUTANT_SURVIVES(equivalent): TrimExcess changes the capacity, which no walk reads.
            if (trim) set.TrimExcess();
        }

        protected static void ClearAndTrim<TKey, TValue>(Dictionary<TKey, TValue> table)
        {
            // MUTANT_SURVIVES(equivalent, boundary): TrimExcess changes the capacity, which no walk reads.
            var trim = table.Count > SparesKept;
            table.Clear();
            // MUTANT_SURVIVES(equivalent): TrimExcess changes the capacity, which no walk reads.
            if (trim) table.TrimExcess();
        }
    }
}
