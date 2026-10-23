#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Maps a point or rect between two panels of one tree for a drag: the identity on one panel, and through
    // screen space (FiberCrossPanelPointerRouter.PanelToScreen, then RuntimePanelUtils.ScreenToPanel) between two
    // screen-space runtime panels, such as the main panel and a V.Portal(layer:) host. Any other pair has no
    // mapping here: an editor panel has no runtime screen mapping, and a V.WorldSpace host is left out because
    // the router's own note records that the screen conversion does not hold for it.
    internal static class DndPanelSpace
    {
        internal static bool CanMap(IPanel? from, IPanel? to, ReconcilerContext ctx)
        {
            if (from == null || to == null)
            {
                return false;
            }
            if (ReferenceEquals(from, to))
            {
                return true;
            }
            return from.contextType == ContextType.Player && to.contextType == ContextType.Player
                && !IsWorldSpace(from, ctx) && !IsWorldSpace(to, ctx);
        }

        internal static bool TryPoint(IPanel from, IPanel? to, Vector2 point, ReconcilerContext ctx, out Vector2 mapped)
        {
            if (!CanMap(from, to, ctx))
            {
                mapped = default;
                return false;
            }
            mapped = ReferenceEquals(from, to)
                ? point
                : RuntimePanelUtils.ScreenToPanel(to!, FiberCrossPanelPointerRouter.PanelToScreen(from, point));
            return true;
        }

        // The element's worldBound in to's space; the caller has checked CanMap.
        internal static Rect RectIn(VisualElement element, IPanel to, ReconcilerContext ctx)
        {
            var bound = element.worldBound;
            var from = element.panel;
            if (from == null || ReferenceEquals(from, to))
            {
                return bound;
            }
            TryPoint(from, to, bound.min, ctx, out var min);
            TryPoint(from, to, bound.max, ctx, out var max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool IsWorldSpace(IPanel panel, ReconcilerContext ctx)
        {
            foreach (var record in ctx.WorldSpaceBindings.Values)
            {
                if (record.Document != null && ReferenceEquals(record.Document.rootVisualElement?.panel, panel))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
