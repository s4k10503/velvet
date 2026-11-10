using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a Suspense that hides its committed primary takes down, in the order React's disappearLayoutEffects
    // takes it: one pre-order walk of the hidden tree, in which a component's layout cleanups run where the walk
    // reaches the component and an element's ref detaches where the walk reaches the element. A component is
    // reached ahead of the element its output starts at (LogicalElementTree.StartOf), and a Portal's rows and an
    // element FiberZLayerCoordinator relocated where their placeholders sit.
    internal static class SuspenseDisappearWalk
    {
        // roots are the hidden primaries' outermost elements, those of the Portals among them included, and fibers
        // the components going offscreen. A fiber whose output starts at no element the walk reaches is taken down
        // after the walk.
        internal static void Run(ReconcilerContext ctx, List<VisualElement> roots, List<ComponentFiber> fibers)
        {
            var startingAt = new Dictionary<VisualElement, List<ComponentFiber>>();
            var endingIn = new Dictionary<VisualElement, List<ComponentFiber>>();
            // In tree order, so the components starting at one element are taken down outermost first and siblings
            // in their order.
            foreach (var fiber in InTreeOrder(fibers))
            {
                var start = LogicalElementTree.StartOf(fiber, out var endOf);
                if (start != null) Add(startingAt, start, fiber);
                else if (endOf != null) Add(endingIn, endOf, fiber);
            }
            var walk = new Walk(ctx, startingAt, endingIn);
            foreach (var root in roots) walk.Visit(root);
            // A fiber taken down above returns at once.
            foreach (var fiber in fibers) FiberEffects.HideLayoutEffects(fiber);
        }

        private static void Add(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at, ComponentFiber fiber)
        {
            if (!lists.TryGetValue(at, out var list))
            {
                list = new List<ComponentFiber>();
                lists[at] = list;
            }
            list.Add(fiber);
        }

        // A pre-order walk of the trees the fibers are in, down each parent's child list.
        private static List<ComponentFiber> InTreeOrder(List<ComponentFiber> fibers)
        {
            var wanted = new HashSet<ComponentFiber>(fibers);
            var tops = new List<ComponentFiber>();
            foreach (var fiber in fibers)
            {
                var top = fiber;
                while (top.Parent != null) top = top.Parent;
                if (!tops.Contains(top)) tops.Add(top);
            }
            var ordered = new List<ComponentFiber>(fibers.Count);
            var pending = new List<ComponentFiber>();
            var children = new List<ComponentFiber>();
            foreach (var top in tops)
            {
                pending.Add(top);
                while (pending.Count > 0)
                {
                    var fiber = pending[pending.Count - 1];
                    pending.RemoveAt(pending.Count - 1);
                    if (wanted.Contains(fiber)) ordered.Add(fiber);
                    children.Clear();
                    for (var child = fiber.Child; child != null; child = child.Sibling) children.Add(child);
                    for (var i = children.Count - 1; i >= 0; i--) pending.Add(children[i]);
                }
            }
            return ordered;
        }

        private sealed class Walk
        {
            private readonly ReconcilerContext _ctx;
            private readonly Dictionary<VisualElement, List<ComponentFiber>> _startingAt;
            private readonly Dictionary<VisualElement, List<ComponentFiber>> _endingIn;
            private readonly HashSet<VisualElement> _visited = new();
            private readonly HashSet<VisualElement> _relocated = new();

            internal Walk(ReconcilerContext ctx, Dictionary<VisualElement, List<ComponentFiber>> startingAt,
                Dictionary<VisualElement, List<ComponentFiber>> endingIn)
            {
                _ctx = ctx;
                _startingAt = startingAt;
                _endingIn = endingIn;
                foreach (var real in ctx.ZLayerPlaceholders.Values) _relocated.Add(real);
            }

            internal void Visit(VisualElement element)
            {
                // MUTANT_SURVIVES(equivalent, guard removed): a second visit takes nothing down, since HideLayoutEffects
                // returns for a fiber it has hidden and DetachRefWhileHidden finds no ref left on the element.
                if (!_visited.Add(element)) return;
                TakeDown(_startingAt, element);
                _ctx.DetachRefWhileHidden(element);
                // Copied first: a cleanup can change the children.
                foreach (var child in new List<VisualElement>(element.hierarchy.Children()))
                {
                    // Reached at its placeholder instead, which holds the slot it was declared at.
                    if (_relocated.Contains(child)) continue;
                    Visit(child);
                    if (_ctx.ZLayerPlaceholders.TryGetValue(child, out var real)) Visit(real);
                    VisitPortalRows(child);
                }
                TakeDown(_endingIn, element);
            }

            // The rows GeneralPathReconciler.AddWithPortalChildren makes roots of, read as it reads them: an entry with
            // no target holds no slot.
            private void VisitPortalRows(VisualElement placeholder)
            {
                if (!_ctx.PortalState.TryGetValue(placeholder, out var portal)) return;
                for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength; slot++)
                {
                    if (!LogicalChildSlots.TryGetPhysical(portal.Target!, slot, out var physical)) break;
                    Visit(portal.Target![physical]);
                }
            }

            private static void TakeDown(Dictionary<VisualElement, List<ComponentFiber>> lists, VisualElement at)
            {
                if (!lists.TryGetValue(at, out var fibers)) return;
                foreach (var fiber in fibers) FiberEffects.HideLayoutEffects(fiber);
            }
        }
    }
}
