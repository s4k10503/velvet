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
    /// <c>PanelHostFactory</c> gives a panel whose declaring settings carry none — a fixture that took
    /// Unity's default runtime theme instead would be measuring a panel Velvet never creates, and does move
    /// numbers: three <c>TextBalancePlaybackTests</c> cases fail under it.
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
