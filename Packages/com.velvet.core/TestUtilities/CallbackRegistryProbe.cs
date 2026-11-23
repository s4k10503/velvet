using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Counts the callbacks registered on an element for one phase: the bubble-up one, which is where an
    /// <c>AttachToPanelEvent</c> hook and a synthetic-bubbling bridge's listeners land, or the trickle-down one. UI
    /// Toolkit exposes no count, so it is read from the element's callback registry by reflection.
    /// </summary>
    public static class CallbackRegistryProbe
    {
        public static int BubbleUpCallbackCount(VisualElement element) => CallbackCount(element, "m_BubbleUpCallbacks");

        public static int TrickleDownCallbackCount(VisualElement element) => CallbackCount(element, "m_TrickleDownCallbacks");

        private static int CallbackCount(VisualElement element, string phaseField)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(element);
            if (registry == null)
            {
                return 0;
            }
            var phase = registry.GetType()
                .GetField(phaseField, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);
            return (int)phase.GetType().GetProperty("Count")!.GetValue(phase);
        }
    }
}
