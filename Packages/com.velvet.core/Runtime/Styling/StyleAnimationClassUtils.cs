using UnityEngine.UIElements;

namespace Velvet
{
    // Shared add/remove helpers so every class-toggling system (animation scheduler, gesture/variant
    // manipulators, drag-and-drop) treats a null class list the same way (RemoveClasses is a no-op) instead
    // of each call site null-checking independently.
    internal static class StyleAnimationClassUtils
    {
        internal static void AddClasses(VisualElement element, string[] classes)
        {
            foreach (var cls in classes)
            {
                element.AddToClassList(cls);
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
                    element.AddToClassList(cls);
                }
                else
                {
                    element.RemoveFromClassList(cls);
                }
            }
        }
    }
}
