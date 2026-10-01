using UnityEngine.UIElements;

namespace Velvet
{
    // The add/remove helpers for a class its writer puts on and takes off the live list itself — the
    // animation scheduler's and the presence exit's — rather than as a payload. The element's class
    // projection ranks such a class without owning it (StyleClassProjection.RawAdded), and a container that
    // gives way to a class of the element's own (a divider, a space margin) re-applies after each add or
    // remove.
    internal static class StyleAnimationClassUtils
    {
        // priority is StyleLayerPriority.Animation for the classes a running tween play shows, and the base one
        // for a class that rests on the element.
        internal static void AddClasses(VisualElement element, string[] classes, long priority = StyleLayerPriority.Base)
        {
            foreach (var cls in classes)
            {
                CornerRadiusFit.TrackClass(element, cls);
                Add(element, cls, priority);
            }
            ClipPathLayoutBox.SyncClasses(element);
            StyleArbitraryValueResolver.NotifyClassesChanged(element);
        }

        // Ranks classes a play leaves on the element as the element's own from now on.
        internal static void Rest(VisualElement element, string[] classes)
        {
            foreach (var cls in classes)
            {
                StyleClassProjection.RawAdded(element, cls, StyleLayerPriority.Base);
            }
        }

        // Removes each class, except one kept names, which is added instead: a play may have removed it
        // before the caller that keeps it recorded it as present.
        internal static void RemoveClasses(VisualElement element, string[]? classes, string[]? kept = null)
        {
            if (classes == null) return;
            foreach (var cls in classes)
            {
                if (kept != null && System.Array.IndexOf(kept, cls) >= 0)
                {
                    Add(element, cls, StyleLayerPriority.Base);
                }
                else
                {
                    element.RemoveFromClassList(cls);
                    StyleClassProjection.RawRemoved(element, cls);
                }
            }
            ClipPathLayoutBox.SyncClasses(element);
            StyleArbitraryValueResolver.NotifyClassesChanged(element);
        }

        private static void Add(VisualElement element, string cls, long priority)
        {
            element.AddToClassList(cls);
            StyleClassProjection.RawAdded(element, cls, priority);
        }
    }
}
