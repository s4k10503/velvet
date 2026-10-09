#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The elements a Suspense keeps hidden with an inline display: none (GeneralPathReconciler.SetPrimaryHidden),
    // so that another writer of that inline display, AnchoredDriver, leaves the hide in place as React's
    // display: none !important outranks the element's own style. Held weakly: an element dropped while hidden
    // leaves with its entry, and FiberElementPoolReset releases one returned to the pool.
    internal static class SuspenseHiddenElements
    {
        private static readonly ConditionalWeakTable<VisualElement, object> s_hidden = new();
        private static readonly object s_marker = new();

        internal static void Hide(VisualElement element)
        {
            element.style.display = DisplayStyle.None;
            s_hidden.AddOrUpdate(element, s_marker);
        }

        internal static void Reveal(VisualElement element)
        {
            element.style.display = StyleKeyword.Null;
            s_hidden.Remove(element);
        }

        internal static bool IsHidden(VisualElement element) => s_hidden.TryGetValue(element, out _);

        internal static void Release(VisualElement element) => s_hidden.Remove(element);
    }
}
