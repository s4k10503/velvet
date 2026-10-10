using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application counts as on screen, where TanStack's focus manager reads
    /// <c>document.visibilityState</c>: <c>Application.isFocused</c> on every platform, the Editor included.
    /// </summary>
    internal static class ApplicationVisibility
    {
        // MUTANT_SURVIVES(unreachable, literal): the case reading the default compares it with Application.isFocused, which a literal equal to that reading passes.
        private static Func<bool> s_isFocused = static () => Application.isFocused;

        internal static bool IsVisible() => s_isFocused();
    }
}
