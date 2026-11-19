using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Counts the callbacks registered on an element for the bubble-up phase, which is where an
    /// <c>AttachToPanelEvent</c> hook and a synthetic-bubbling bridge's listeners land. UI Toolkit exposes no
    /// count, so it is read from the element's callback registry by reflection.
    /// </summary>
    public static class CallbackRegistryProbe
    {
        public static int BubbleUpCallbackCount(VisualElement element)
        {
            var registry = typeof(CallbackEventHandler)
                .GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(element);
            if (registry == null)
            {
                return 0;
            }
            var bubbleUp = registry.GetType()
                .GetField("m_BubbleUpCallbacks", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);
            return (int)bubbleUp.GetType().GetProperty("Count")!.GetValue(bubbleUp);
        }
    }
}
