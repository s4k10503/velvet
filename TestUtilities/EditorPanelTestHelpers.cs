using System;
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

        /// <summary>
        /// Pumps the panel's internal timer scheduler once — the same call a live panel issues once per
        /// frame — so an EditMode fixture can drive <c>schedule.Execute</c> items deterministically (the
        /// batchmode PlayerLoop never ticks them). The scheduler is internal engine surface, reached by
        /// walking the panel's type chain for its <c>scheduler</c> property.
        /// </summary>
        public static void DriveSchedulerOnce(IPanel panel)
        {
            var scheduler = FindPanelProperty(panel, "scheduler")?.GetValue(panel);
            if (scheduler == null)
            {
                throw new MissingMemberException(panel.GetType().FullName, "scheduler");
            }
            var update = scheduler.GetType().GetMethod(
                "UpdateScheduledEvents",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (update == null)
            {
                throw new MissingMethodException(scheduler.GetType().FullName, "UpdateScheduledEvents");
            }
            update.Invoke(scheduler, null);
        }

        /// <summary>
        /// Installs a fake clock on the panel: the scheduler and every scheduled item read time
        /// exclusively through the panel's own time function (double seconds), so an EditMode fixture
        /// can drive cadence deterministically regardless of machine load. Internal engine surface,
        /// like the scheduler above.
        /// </summary>
        public static void SetPanelTimeFunction(IPanel panel, Func<double> secondsClock)
        {
            var prop = FindPanelProperty(panel, "TimeSinceStartupFunc");
            if (prop == null)
            {
                throw new MissingMemberException(panel.GetType().FullName, "TimeSinceStartupFunc");
            }
            var del = Delegate.CreateDelegate(prop.PropertyType, secondsClock.Target, secondsClock.Method);
            prop.SetValue(panel, del);
        }

        // Walks the panel's type chain for an internal property: the engine members these helpers
        // reach (scheduler, time function) are internal AND inherited, which a plain GetProperty on
        // the concrete type misses.
        private static PropertyInfo FindPanelProperty(IPanel panel, string name)
        {
            for (var t = panel.GetType(); t != null; t = t.BaseType)
            {
                var prop = t.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (prop != null)
                {
                    return prop;
                }
            }
            return null;
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
