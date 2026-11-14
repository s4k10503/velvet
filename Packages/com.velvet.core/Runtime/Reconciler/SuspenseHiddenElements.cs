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
        }

        private static readonly ConditionalWeakTable<VisualElement, Before> s_hidden = new();

        // An element two nested Suspenses hide keeps what it had before the first.
        internal static void Hide(VisualElement element)
        {
            if (!s_hidden.TryGetValue(element, out _)) s_hidden.Add(element, new Before { Display = element.style.display });
            element.style.display = DisplayStyle.None;
        }

        internal static void Reveal(VisualElement element)
        {
            // MUTANT_SURVIVES(unreachable, line removed): Reveal's one caller, SuspenseOffscreenCommit.Run, reveals after its own HideHeld took every hold it placed, so a hold here is an enclosing commit's, and the commit a hide's cleanup nests is an error boundary's swap, which unmounts the rows it replaces rather than revealing one.
            s_heldForHide.Remove(element);
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

        // The elements SuspenseOffscreenCommit hides once the cleanups ahead of its hide have run. Release drops the
        // hold of one returned to the pool meanwhile, which VNodePool resets as it takes it back, so the hide does not
        // reach the element's next consumer.
        private static readonly ConditionalWeakTable<VisualElement, object> s_heldForHide = new();
        private static readonly object s_hold = new();

        internal static void HoldForHide(VisualElement element) => s_heldForHide.GetValue(element, _ => s_hold);

        internal static bool IsHeldForHide(VisualElement element) => s_heldForHide.TryGetValue(element, out _);

        internal static void HideHeld(VisualElement element)
        {
            if (s_heldForHide.Remove(element)) Hide(element);
        }

        internal static void Release(VisualElement element)
        {
            s_hidden.Remove(element);
            s_heldForHide.Remove(element);
        }
    }
}
