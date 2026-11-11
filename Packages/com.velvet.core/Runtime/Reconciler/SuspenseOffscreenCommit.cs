using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Applies what the Suspenses of a walk decided about their primaries once the walk has placed its rows: the
    // elements to hide or reveal and the components going offscreen or coming back. A hide follows React's Offscreen
    // mutation case: one pre-order walk of the hidden tree first (disappearLayoutEffects), in which a component's
    // layout cleanups run where the walk reaches the component and an element's ref detaches where the walk reaches
    // the element, and the display: none after it (hideOrUnhideAllChildren). A component is reached ahead of the
    // element its output starts at (RenderedChildrenCache.StartOf), and a Portal's children and an element
    // FiberZLayerCoordinator relocated are reached where they are declared.
    internal sealed class SuspenseOffscreenCommit
    {
        // An instance per Apply on the stack, so a cleanup that commits another walk takes an instance of its own.
        private static readonly Stack<SuspenseOffscreenCommit> s_pool = new();

        private ReconcilerContext _ctx = null!;
        private readonly Dictionary<VisualElement, int> _lastEntry = new();
        private readonly List<VisualElement> _hidden = new();
        private readonly HashSet<VisualElement> _hiddenSet = new();
        private readonly List<ComponentFiber> _hiddenFibers = new();
        private readonly HashSet<ComponentFiber> _hiding = new();
        private readonly List<(ComponentFiber Fiber, bool Hidden)> _afterTheWalk = new();
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _startingAt = new();
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _endingIn = new();
        private readonly Dictionary<VisualElement, List<ComponentFiber>> _endingInPortal = new();
        private readonly Stack<List<ComponentFiber>> _spareFiberLists = new();
        private readonly RenderedChildrenCache _rendered = new();
        private readonly HashSet<VisualElement> _visited = new();
        private readonly List<List<VisualElement>> _childrenAtDepth = new();
        private int _depth;

        internal static void Apply(ReconcilerContext ctx,
            List<(int Row, VisualElement Element, bool Hidden)>? displayChanges,
            List<(ComponentFiber Fiber, bool Hidden)>? offscreenChanges)
        {
            if (displayChanges == null && offscreenChanges == null) return;
            var commit = s_pool.Count > 0 ? s_pool.Pop() : new SuspenseOffscreenCommit();
            commit._ctx = ctx;
            try
            {
                commit.Run(displayChanges, offscreenChanges);
            }
            finally
            {
                commit.Clear();
                s_pool.Push(commit);
            }
        }

        private void Run(List<(int Row, VisualElement Element, bool Hidden)>? displayChanges,
            List<(ComponentFiber Fiber, bool Hidden)>? offscreenChanges)
        {
            // An element ends as its last entry leaves it: a Suspense of the walk can reveal an element that the
            // Suspense enclosing it, deciding later in the walk, hides.
            if (displayChanges != null)
            {
                for (var i = 0; i < displayChanges.Count; i++) _lastEntry[displayChanges[i].Element] = i;
                for (var i = 0; i < displayChanges.Count; i++)
                {
                    var (_, element, hidden) = displayChanges[i];
                    if (!hidden || _lastEntry[element] != i) continue;
                    _hidden.Add(element);
                    _hiddenSet.Add(element);
                    SuspenseHiddenElements.HoldForHide(element);
                }
            }
            // The walk takes a fiber's first hide out of the list's order, so what the list holds for that fiber
            // before the hide is applied ahead of the walk and what it holds after, behind it.
            if (offscreenChanges != null)
            {
                foreach (var (fiber, hidden) in offscreenChanges)
                {
                    if (_hiding.Contains(fiber))
                    {
                        _afterTheWalk.Add((fiber, hidden));
                    }
                    else if (!hidden)
                    {
                        FiberEffects.ShowLayoutEffects(fiber, _ctx);
                    }
                    else
                    {
                        _hiding.Add(fiber);
                        _hiddenFibers.Add(fiber);
                    }
                }
            }
            Disappear();
            foreach (var element in _hidden) SuspenseHiddenElements.HideHeld(element);
            // After the hides: a reveal attaches the refs below it short of an element a Suspense hides, and the walk
            // has just taken those of the elements hidden here off.
            if (displayChanges != null)
            {
                for (var i = 0; i < displayChanges.Count; i++)
                {
                    var (_, element, hidden) = displayChanges[i];
                    if (hidden || _lastEntry[element] != i) continue;
                    SuspenseHiddenElements.Reveal(element);
                    _ctx.AttachRefsHiddenUnder(element);
                }
            }
            foreach (var element in _hidden)
            {
                // UI Toolkit lets go of the focus of an element no longer displayed only at its next layout
                // pass, which SuspenseHiddenFocusPanelTests pins; the commit that hides it lets go of it here.
                var focused = element.focusController?.focusedElement as VisualElement;
                if (SuspenseHiddenElements.IsAtOrUnderHidden(focused)) focused!.Blur();
            }
            foreach (var (fiber, hidden) in _afterTheWalk)
            {
                if (hidden) FiberEffects.HideLayoutEffects(fiber);
                else FiberEffects.ShowLayoutEffects(fiber, _ctx);
            }
        }

        private void Disappear()
        {
            foreach (var fiber in _hiddenFibers) Place(fiber);
            foreach (var root in _hidden) Visit(root);
            // A fiber taken down above returns at once.
            foreach (var fiber in _hiddenFibers) FiberEffects.HideLayoutEffects(fiber);
        }

        private void Place(ComponentFiber fiber)
        {
            var mount = fiber.MountPoint;
            if (mount == null) return;
            var owning = fiber.OwningPortalPlaceholder;
            // A child of a Portal holding no row at the end of the Portal's rows is taken down where the walk leaves
            // them: the target's next row there belongs to something else, if the target holds one.
            if (owning != null
                && _ctx.PortalState.TryGetValue(owning, out var portal)
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
            AddInTreeOrder(_startingAt, _ctx.ZLayerPlaceholders.TryGetValue(start, out var real) ? real : start, fiber);
        }

        private bool IsAnotherPortalsRow(VisualElement? owning, VisualElement container, int slot)
        {
            foreach (var (placeholder, portal) in _ctx.PortalState)
            {
                if (!ReferenceEquals(portal.Target, container)) continue;
                if (slot < portal.SlotStart || slot >= portal.SlotStart + portal.SlotLength) continue;
                return !ReferenceEquals(placeholder, owning);
            }
            return false;
        }

        // The fibers taken down at one place, enclosing ones ahead of those inside them and siblings in their order.
        private void AddInTreeOrder(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at,
            ComponentFiber fiber)
        {
            if (!lists.TryGetValue(at, out var list))
            {
                list = _spareFiberLists.Count > 0 ? _spareFiberLists.Pop() : new List<ComponentFiber>();
                lists[at] = list;
            }
            var slot = list.Count;
            while (slot > 0 && PrecedesInPreOrder(fiber, list[slot - 1])) slot--;
            list.Insert(slot, fiber);
        }

        private static bool PrecedesInPreOrder(ComponentFiber a, ComponentFiber b)
        {
            for (var ancestor = b.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, a)) return true;
            }
            return FiberEffects.PrecedesInTreeOrder(a, b);
        }

        private void Visit(VisualElement element)
        {
            // MUTANT_SURVIVES(equivalent, guard removed): a second visit takes nothing down, since HideLayoutEffects
            // returns for a fiber it has hidden and DetachRefWhileHidden finds no ref left on the element.
            if (!_visited.Add(element)) return;
            TakeDown(_startingAt, element);
            _ctx.DetachRefWhileHidden(element);
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
                    // which the hide made a root of its own, at its Portal, relocated or not.
                    if (_hiddenSet.Contains(child) || FiberZLayerCoordinator.PlaceholderOf(_ctx, child) != null) continue;
                    if (_ctx.ZLayerPlaceholders.TryGetValue(child, out var real))
                    {
                        if (_hiddenSet.Contains(real)) continue;
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
            TakeDown(_endingIn, element);
        }

        // The rows GeneralPathReconciler.AddWithPortalChildren makes roots of. A Portal whose registered target is
        // missing has an entry with no target, and no rows.
        private void VisitPortalRows(VisualElement placeholder)
        {
            if (!_ctx.PortalState.TryGetValue(placeholder, out var portal)) return;
            if (portal.Target != null)
            {
                var rows = _rendered.Of(portal.Target);
                for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength && slot < rows.Count; slot++)
                {
                    var row = rows[slot];
                    Visit(_ctx.ZLayerPlaceholders.TryGetValue(row, out var real) ? real : row);
                }
            }
            TakeDown(_endingInPortal, placeholder);
        }

        private static void TakeDown(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at)
        {
            if (!lists.TryGetValue(at, out var fibers)) return;
            foreach (var fiber in fibers) FiberEffects.HideLayoutEffects(fiber);
        }

        private void Clear()
        {
            _ctx = null!;
            _lastEntry.Clear();
            _hidden.Clear();
            _hiddenSet.Clear();
            _hiddenFibers.Clear();
            _hiding.Clear();
            _afterTheWalk.Clear();
            ReturnLists(_startingAt);
            ReturnLists(_endingIn);
            ReturnLists(_endingInPortal);
            _rendered.Clear();
            _visited.Clear();
        }

        private void ReturnLists(Dictionary<VisualElement, List<ComponentFiber>> lists)
        {
            foreach (var list in lists.Values)
            {
                list.Clear();
                _spareFiberLists.Push(list);
            }
            lists.Clear();
        }
    }
}
