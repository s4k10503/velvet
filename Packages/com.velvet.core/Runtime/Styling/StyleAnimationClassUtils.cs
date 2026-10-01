using UnityEngine.UIElements;

namespace Velvet
{
    // The add/remove helpers for a class its writer puts on and takes off the live list itself — the
    // animation scheduler's and the presence exit's — rather than as a payload. The element's class
    // projection ranks such a class without owning it (StyleClassProjection.RawAdded).
    internal static class StyleAnimationClassUtils
    {
        internal static void AddClasses(VisualElement element, string[] classes)
        {
            foreach (var cls in classes)
            {
                CornerRadiusFit.TrackClass(element, cls);
                Add(element, cls);
            }
            ClipPathLayoutBox.SyncClasses(element);
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
                    Add(element, cls);
                }
                else
                {
                    element.RemoveFromClassList(cls);
                    StyleClassProjection.RawRemoved(element, cls);
                }
            }
            ClipPathLayoutBox.SyncClasses(element);
        }

        private static void Add(VisualElement element, string cls)
        {
            element.AddToClassList(cls);
            StyleClassProjection.RawAdded(element, cls, StyleLayerPriority.Base);
        }
    }
}
