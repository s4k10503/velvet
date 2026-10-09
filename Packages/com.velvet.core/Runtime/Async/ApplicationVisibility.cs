using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application counts as on screen, where TanStack's focus manager reads
    /// <c>document.visibilityState</c>: <c>Application.isFocused</c> on a mobile platform, and visible on every
    /// other.
    /// </summary>
    internal static class ApplicationVisibility
    {
        internal static bool IsVisible()
        {
            // MUTANT_SURVIVES(unreachable, guard removed): the Editor suites run off a mobile platform, where the reading never consults isFocused.
            if (Application.isMobilePlatform) return Application.isFocused;
            return true;
        }
    }
}
