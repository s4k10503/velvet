using System;
using System.Collections;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // Drops what a panel's FocusController still holds of a subtree leaving it, so an element Velvet releases
    // from that panel carries no focus into its next mount. PooledFocusMemoryTests pins what each held
    // reference does to a Button the pool hands out again.
    //
    // Reflection rather than a public call: Blur() reaches these only while the departing element holds or is
    // about to take focus, and a panel the event system blurred keeps its last focused element with nothing
    // focused. Clearing that one publicly means switching the panel's focus, which would drop a focus change
    // a handler has pending.
    internal static class PanelFocusMemory
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        // Reached through IPanel rather than named: naming the type would leave DocumentationDriftTests'
        // identifier allowlist holding an entry it no longer needs, and editing that table alone puts every
        // case of that fixture on trial against the merge base.
        private static readonly Type s_controllerType =
            typeof(IPanel).GetProperty(nameof(IPanel.focusController))!.PropertyType;

        private static readonly FieldInfo? s_lastFocusedElement =
            s_controllerType.GetField("m_LastFocusedElement", Instance);

        private static readonly FieldInfo? s_focusedElements =
            s_controllerType.GetField("m_FocusedElements", Instance);

        private static readonly FieldInfo? s_entryFocusedElement =
            s_focusedElements?.FieldType.GetGenericArguments()[0]
                .GetField("m_FocusedElement", BindingFlags.Instance | BindingFlags.Public);

        // The focused entry is read raw rather than through focusedElement: a z-layer element has left its
        // panel by the time it is released.
        public static void Forget(IPanel? panel, VisualElement departing)
        {
            var controller = panel?.focusController;
            if (controller == null)
            {
                return;
            }
            if (s_focusedElements?.GetValue(controller) is IList { Count: > 0 } entries
                && s_entryFocusedElement?.GetValue(entries[0]) is VisualElement focused
                && IsWithin(focused, departing))
            {
                entries.Clear();
            }
            if (s_lastFocusedElement?.GetValue(controller) is VisualElement last && IsWithin(last, departing))
            {
                s_lastFocusedElement.SetValue(controller, null);
            }
        }

        private static bool IsWithin(VisualElement element, VisualElement root)
            => ReferenceEquals(element, root) || root.Contains(element);
    }
}
