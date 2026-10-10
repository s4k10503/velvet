using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application counts as on screen, where TanStack's focus manager reads
    /// <c>document.visibilityState</c>: <c>Application.isFocused</c> on every platform, the Editor included.
    /// </summary>
    internal static class ApplicationVisibility
    {
        internal static bool IsVisible() => Application.isFocused;
    }
}
