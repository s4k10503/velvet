using UnityEngine.UIElements;

namespace Velvet
{
    // CSS class manipulation utilities. Internal helpers used only within the Velvet runtime.
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
