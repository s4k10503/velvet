#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Maps a point or rect between two panels of one tree for a drag: the identity on one panel, and through
    // screen space between two runtime panels. A V.WorldSpace panel does not take the screen conversions the other
    // runtime panels do (FiberCrossPanelPointerRouter records why), so it is reached through Camera.main, with
    // its coordinates taken as its root document's local x and y, the space V.Anchored places an element in
    // there. An editor panel has no runtime screen mapping.
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
                && (Camera.main != null || (WorldSpaceDocument(from, ctx) == null && WorldSpaceDocument(to, ctx) == null));
        }

        internal static bool TryPoint(IPanel from, IPanel? to, Vector2 point, ReconcilerContext ctx, out Vector2 mapped)
        {
            mapped = default;
            if (!CanMap(from, to, ctx))
            {
                return false;
            }
            if (ReferenceEquals(from, to))
            {
                mapped = point;
                return true;
            }
            return TryToScreen(from, point, ctx, out var screen) && TryFromScreen(to!, screen, ctx, out mapped);
        }

        // The element's box without the translate, scale and rotate of its own, placed by its ancestors, in to's
        // space; false where a corner has no place there.
        internal static bool TryRectIn(VisualElement element, IPanel to, ReconcilerContext ctx, out Rect rect)
        {
            rect = default;
            var bound = LayoutRect(element);
            var from = element.panel;
            if (from == null || ReferenceEquals(from, to))
            {
                rect = bound;
                return true;
            }
            if (!TryPoint(from, to, bound.min, ctx, out var min))
            {
                return false;
            }
            if (!TryPoint(from, to, bound.max, ctx, out var max))
            {
                return false;
            }
            rect = Rect.MinMaxRect(
                Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return true;
        }

        internal static Rect LayoutRect(VisualElement element)
            => element.hierarchy.parent is { } parent ? parent.LocalToWorld(element.layout) : element.worldBound;

        private static bool TryToScreen(IPanel panel, Vector2 point, ReconcilerContext ctx, out Vector2 screen)
        {
            screen = default;
            var document = WorldSpaceDocument(panel, ctx);
            if (document == null)
            {
                screen = FiberCrossPanelPointerRouter.PanelToScreen(panel, point);
                return true;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var projected = camera.WorldToScreenPoint(document.transform.TransformPoint(point.x, point.y, 0f));
            screen = new Vector2(projected.x, projected.y);
            return projected.z > 0f;
        }

        // Where the camera's ray through the screen point crosses the panel's plane; none where the ray runs along
        // the plane or meets it behind the camera.
        private static bool TryFromScreen(IPanel panel, Vector2 screen, ReconcilerContext ctx, out Vector2 mapped)
        {
            mapped = default;
            var document = WorldSpaceDocument(panel, ctx);
            if (document == null)
            {
                mapped = RuntimePanelUtils.ScreenToPanel(panel, screen);
                return true;
            }
            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }
            var ray = camera.ScreenPointToRay(screen);
            var plane = new Plane(document.transform.forward, document.transform.position);
            if (!plane.Raycast(ray, out var enter))
            {
                return false;
            }
            var local = document.transform.InverseTransformPoint(ray.GetPoint(enter));
            mapped = new Vector2(local.x, local.y);
            return true;
        }

        private static UIDocument? WorldSpaceDocument(IPanel panel, ReconcilerContext ctx)
        {
            foreach (var record in ctx.WorldSpaceBindings.Values)
            {
                if (record.Document != null && ReferenceEquals(record.Document.rootVisualElement?.panel, panel))
                {
                    return record.Document;
                }
            }
            return null;
        }
    }
}
