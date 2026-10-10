using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application counts as on screen, where TanStack's focus manager reads
    /// <c>document.visibilityState</c>: <c>Application.isFocused</c> on every platform, the Editor included.
    /// </summary>
    internal static class ApplicationVisibility
    {
        // MUTANT_SURVIVES(unreachable, literal): a case would have to move Application.isFocused, which a test cannot write, and every case that needs a reading replaces it through NetworkSignals.IsVisible.
        internal static bool IsVisible() => Application.isFocused;
    }
}
