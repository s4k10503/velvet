using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Shared reflection helper for EditMode fixtures that mount onto a real editor panel: the batchmode
    /// PlayerLoop never ticks layout on its own, so a fixture that reads <c>resolvedStyle</c> must force a
    /// layout/styles pass first. Previously duplicated across <c>PanelTestBase</c>,
    /// <c>FocusLossDuringCommitTests</c>, and the preview window's zoom/layout fixture.
    /// </summary>
    public static class EditorPanelTestHelpers
    {
        /// <summary>
        /// Invokes whichever of <c>UpdateForRepaint</c>/<c>ValidateLayout</c>/<c>ApplyStyles</c> the panel
        /// implementation exposes (the parameterless overload of each), so <c>resolvedStyle</c> reflects the
        /// current styles/layout without waiting for a PlayerLoop tick that batchmode never delivers.
        /// </summary>
        public static void ForcePanelUpdate(IPanel panel)
        {
            var t = panel.GetType();
            foreach (var name in new[] { "UpdateForRepaint", "ValidateLayout", "ApplyStyles" })
            {
                var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(panel, null);
                }
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Builds a <see cref="VelvetPreviewStory"/> directly via its internal constructor instead of through
    /// reflection — TestUtilities has <c>InternalsVisibleTo</c> from the Runtime assembly (where the story type
    /// lives), so a fixture that needs a story without going through <c>[VelvetPreview]</c> discovery can
    /// construct one plainly.
    /// </summary>
    public static class PreviewStoryTestFactory
    {
        public static VelvetPreviewStory Build(MethodInfo method, VelvetPreviewAttribute attribute) =>
            new(method, attribute);
    }
#endif
}
