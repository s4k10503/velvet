using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Creates the <see cref="PanelSettings"/> a PlayMode fixture mounts on.
    /// </summary>
    /// <remarks>
    /// A PanelSettings with no themeStyleSheet makes UI Toolkit warn on every panel it creates; sixteen
    /// fixtures hand-rolling their own produced 139 of those in one run, which buries anything else a reader
    /// might notice in that log. The theme handed over is an empty one, matching what
    /// <c>PanelHostFactory</c> falls back to when the declaring panel carries none. It copies the
    /// declaring panel's theme when there is one, so a themed panel is not a shape Velvet never produces —
    /// what the empty theme buys here is a host whose measurements are the framework's own defaults rather
    /// than a theme's.
    /// Test-only. Must not be used from production code.
    /// </remarks>
    public static class TestPanelSettings
    {
        public static PanelSettings Create()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = EmptyTheme();
            return settings;
        }

        private static ThemeStyleSheet EmptyTheme()
        {
            var theme = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            theme.name = "VelvetTestEmptyTheme";
            theme.hideFlags = HideFlags.HideAndDontSave;
            return theme;
        }
    }
}
