#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The elements a Suspense keeps hidden with an inline display: none (GeneralPathReconciler.SetPrimaryHidden),
    // so that another writer of that inline display, AnchoredDriver, leaves the hide in place as React's
    // display: none !important outranks the element's own style. Each keeps the inline display it had before the
    // hide, which the reveal puts back: a Portal's placeholder is display: none of its own. Held weakly: an
    // element dropped while hidden leaves with its entry, and FiberElementPoolReset releases one returned to the
    // pool.
    internal static class SuspenseHiddenElements
    {
        private sealed class Before
        {
            internal StyleEnum<DisplayStyle> Display;
            internal ComponentFiber? Boundary;
        }

        private static readonly ConditionalWeakTable<VisualElement, Before> s_hidden = new();

        // An element two nested Suspenses hide keeps what it had, and the boundary, from the first hide it got.
        // boundary is the fiber rendering the Suspense, null for one no component renders.
        internal static void Hide(VisualElement element, ComponentFiber? boundary)
        {
            if (!s_hidden.TryGetValue(element, out _))
            {
                s_hidden.Add(element, new Before { Display = element.style.display, Boundary = boundary });
            }
            element.style.display = DisplayStyle.None;
        }

        internal static void Reveal(VisualElement element)
        {
            if (!s_hidden.TryGetValue(element, out var before)) return;
            element.style.display = before.Display;
            s_hidden.Remove(element);
        }

        internal static bool IsHidden(VisualElement element) => s_hidden.TryGetValue(element, out _);

        internal static bool IsAtOrUnderHidden(VisualElement? element)
        {
            for (var ve = element; ve != null; ve = ve.parent)
            {
                if (IsHidden(ve)) return true;
            }
            return false;
        }

        internal static VisualElement? NearestHiddenAtOrAbove(VisualElement? element)
        {
            for (var ve = element; ve != null; ve = ve.parent)
            {
                if (IsHidden(ve)) return ve;
            }
            return null;
        }

        // The boundary of the nearest hidden element at or above element; null when none is hidden.
        internal static ComponentFiber? BoundaryOfHiddenAtOrAbove(VisualElement? element)
        {
            for (var ve = element; ve != null; ve = ve.parent)
            {
                if (s_hidden.TryGetValue(ve, out var before)) return before.Boundary;
            }
            return null;
        }

        internal static void Release(VisualElement element) => s_hidden.Remove(element);
    }
}
