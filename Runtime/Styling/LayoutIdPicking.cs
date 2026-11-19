#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // A member drawn over the lead takes no pointer, as Framer gives it `pointer-events: none`. UI Toolkit's picking
    // mode belongs to each element rather than being inherited, so it is set across the member's subtree, walked
    // again on every pass for children mounted since, keeping each element's own mode.
    //
    // Each projection takes its own PickingHold on an element, never a mode of its own: a member nested in another is
    // walked by both, and one restoring what the other had already turned off would leave it off for good
    // (Given_TwoCardsWithTitlesSharingIds_When_TheFirstLeadsAgain_Then_ItsTitleTakesPointers).
    internal static class LayoutIdPicking
    {
        public static void Ignore(VisualElement element, LayoutIdProjection projection)
        {
            projection.Picking ??= new HashSet<VisualElement>();
            if (projection.Picking.Add(element))
            {
                PickingHold.Take(element);
            }
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                Ignore(element.hierarchy[i], projection);
            }
        }

        public static void Restore(LayoutIdProjection projection)
        {
            foreach (var element in projection.Picking ?? s_none)
            {
                PickingHold.Drop(element);
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
                foreach (var element in projection.Picking ?? s_none)
                {
                    if (element.FindCommonAncestor(torn) == torn) s_released.Add(element);
                }
                foreach (var element in s_released)
                {
                    projection.Picking!.Remove(element);
                    PickingHold.Drop(element);
                }
            }
        }

        private static readonly List<VisualElement> s_released = new();
        private static readonly HashSet<VisualElement> s_none = new();
    }
}
