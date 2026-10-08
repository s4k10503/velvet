#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // LayoutIdPicking (a layoutId member drawn over its lead) and PointerEventsScope (a pointer-events-none subtree)
    // both turn picking off for a span and hand it back, and both take their holds here instead of writing
    // pickingMode themselves. The element's own mode is kept once, at the first hold, and the holds are counted: two
    // writers that each kept a mode of their own would record the other's Ignore as the element's, and whichever
    // released last would leave the element unpickable for good.
    internal static class PickingHold
    {
        private sealed class Hold
        {
            public PickingMode Own;
            public int Count;
        }

        private static readonly ConditionalWeakTable<VisualElement, Hold> s_holds = new();

        public static void Take(VisualElement element)
        {
            var hold = s_holds.GetOrCreateValue(element);
            if (hold.Count++ == 0) hold.Own = element.pickingMode;
            element.pickingMode = PickingMode.Ignore;
        }

        // Only for an element the caller took a hold on.
        public static void Drop(VisualElement element)
        {
            var hold = s_holds.GetOrCreateValue(element);
            if (--hold.Count == 0) element.pickingMode = hold.Own;
        }
    }
}
