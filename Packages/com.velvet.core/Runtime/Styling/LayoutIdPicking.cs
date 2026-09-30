#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // A member drawn over the lead takes no pointer, as Framer gives it `pointer-events: none`. UI Toolkit's picking
    // mode belongs to each element rather than being inherited, so it is set across the member's subtree, walked
    // again on every pass for children mounted since, keeping each element's own mode.
    internal static class LayoutIdPicking
    {
        public static void Ignore(VisualElement element, LayoutIdProjection projection)
        {
            projection.Picking ??= new Dictionary<VisualElement, PickingMode>();
            if (projection.Picking.TryAdd(element, element.pickingMode)) element.pickingMode = PickingMode.Ignore;
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                Ignore(element.hierarchy[i], projection);
            }
        }

        public static void Restore(LayoutIdProjection projection)
        {
            foreach (var entry in projection.Picking ?? s_none)
            {
                entry.Key.pickingMode = entry.Value;
            }
            projection.Picking = null;
        }

        // Hands back the mode of each element of a torn-down subtree that a member turned off, before the pool can
        // give that element to another Motion: the pool resets a control's own mode and not its internals'.
        public static void Release(VisualElement torn, ReconcilerContext ctx)
        {
            foreach (var projection in ctx.LayoutIdProjections.Values)
            {
                s_released.Clear();
                foreach (var entry in projection.Picking ?? s_none)
                {
                    if (ReferenceEquals(entry.Key, torn) || torn.Contains(entry.Key)) s_released.Add(entry.Key);
                }
                foreach (var element in s_released)
                {
                    element.pickingMode = projection.Picking![element];
                    projection.Picking.Remove(element);
                }
            }
        }

        private static readonly List<VisualElement> s_released = new();
        private static readonly Dictionary<VisualElement, PickingMode> s_none = new();
    }
}
