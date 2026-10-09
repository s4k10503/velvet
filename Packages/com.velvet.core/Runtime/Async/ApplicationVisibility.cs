using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application is on screen, as TanStack's focus manager reads <c>document.visibilityState</c>.
    /// On a mobile platform a backgrounded application is the one that has lost focus; every other platform
    /// counts as visible.
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
