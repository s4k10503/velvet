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

        internal static void RemoveClasses(VisualElement element, string[]? classes)
        {
            if (classes == null) return;
            foreach (var cls in classes)
            {
                element.RemoveFromClassList(cls);
            }
        }
    }
}
