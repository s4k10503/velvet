#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // A member drawn over the lead takes no pointer, as Framer gives it `pointer-events: none`. UI Toolkit's picking
    // mode belongs to each element rather than being inherited, so it is set across the member's subtree, walked
    // again on every pass for children mounted since, keeping each element's own mode.
    //
    // The own mode is kept once per element rather than per projection, with the projections holding it counted: a
    // member nested in another is walked by both, and one restoring what the other had already turned off would
    // leave it off for good (Given_TwoCardsWithTitlesSharingIds_When_TheFirstLeadsAgain_Then_ItsTitleTakesPointers).
    internal static class LayoutIdPicking
    {
        private sealed class Hold
        {
            public PickingMode Own;
            public int Count;
        }

        private static readonly ConditionalWeakTable<VisualElement, Hold> s_holds = new();

        public static void Ignore(VisualElement element, LayoutIdProjection projection)
        {
            projection.Picking ??= new HashSet<VisualElement>();
            if (projection.Picking.Add(element))
            {
                var hold = s_holds.GetOrCreateValue(element);
                if (hold.Count++ == 0) hold.Own = element.pickingMode;
                element.pickingMode = PickingMode.Ignore;
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
                Drop(element);
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
                    Drop(element);
                }
            }
        }

        private static void Drop(VisualElement element)
        {
            var hold = s_holds.GetOrCreateValue(element);
            if (--hold.Count == 0) element.pickingMode = hold.Own;
        }

        private static readonly List<VisualElement> s_released = new();
        private static readonly HashSet<VisualElement> s_none = new();
    }
}
