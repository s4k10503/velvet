using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Shared scaffold for Styling fixtures that need a real <see cref="EditorWindow"/> panel so layout resolves
    /// and <c>resolvedStyle</c> / pointer-and-geometry events behave like production. It absorbs the boilerplate
    /// the panel fixtures duplicate: the headless guard, host-window creation/teardown, and the reflective
    /// "force a layout pass" helper.
    /// </summary>
    /// <remarks>
    /// Two sub-patterns are folded into one base through virtual hooks:
    /// <list type="bullet">
    /// <item><b>relational / geometry</b> (no stylesheet): override nothing but <see cref="WindowSize"/> if a
    /// specific size is required before the panel is shown.</item>
    /// <item><b>USS resolved-style</b> (bundled stylesheet attached): override <see cref="LoadStyleSheets"/> to add
    /// the sheet — it runs after the window is shown, the same point the USS fixtures attach it today.</item>
    /// </list>
    /// <see cref="_window"/> and <see cref="_mounted"/> keep the field names/types the fixtures already use, so a
    /// fixture migrates by deleting its own copies and inheriting these. Subclasses that need extra per-test setup
    /// override <see cref="SetUp"/>/<see cref="TearDown"/> and call <c>base</c>.
    /// </remarks>
    public abstract class PanelTestBase
    {
        /// <summary>The host window supplying the real panel. Created in <see cref="SetUp"/>, disposed in teardown.</summary>
        protected EditorWindow _window;

        /// <summary>The live mounted tree, disposed in teardown. Assigned by mount helpers / individual tests.</summary>
        protected MountedTree _mounted;

        /// <summary>
        /// Size applied to the window BEFORE it is shown. Defaults to 800x600. Override for a fixed pre-show size
        /// (e.g. a card laid out at a known box); USS fixtures that size after <c>Show()</c> instead do so in
        /// <see cref="LoadStyleSheets"/>.
        /// </summary>
        protected virtual Rect WindowSize => new Rect(0, 0, 800, 600);

        /// <summary>
        /// Hook run after the window is shown (default no-op). USS fixtures override this to attach the bundled
        /// <c>StyleUtilities.uss</c> to the panel root; it is the post-show seam where stylesheet loading lived.
        /// </summary>
        protected virtual void LoadStyleSheets() { }

        /// <summary>
        /// Guards headless first, then creates + shows the host window at <see cref="WindowSize"/> and runs
        /// <see cref="LoadStyleSheets"/>. Override and call <c>base.SetUp()</c> for additional per-test arrangement.
        /// </summary>
        [SetUp]
        public virtual void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");

            _window = ScriptableObject.CreateInstance<TestHostWindow>();
            _window.position = WindowSize;
            _window.Show();

            LoadStyleSheets();
        }

        /// <summary>
        /// Disposes the mounted tree and destroys the window, null-safe (so a headless-skipped setup tears down
        /// cleanly). Override and call <c>base.TearDown()</c> for additional cleanup.
        /// </summary>
        [TearDown]
        public virtual void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_window != null)
            {
                _window.Close();
                Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        /// <summary>
        /// Forces the panel through a layout/styles pass via reflection so <c>resolvedStyle</c> is populated; the
        /// EditMode batch player loop does not tick layout on its own. Invokes whichever of
        /// <c>UpdateForRepaint</c>/<c>ValidateLayout</c>/<c>ApplyStyles</c> the panel exposes.
        /// </summary>
        protected static void ForcePanelUpdate(IPanel panel)
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
        /// Mounts a single named leaf, forces a layout pass, and returns it resolved. Convenience for USS
        /// resolved-style fixtures (the dominant shape: mount a leaf, read its <c>resolvedStyle</c>).
        /// </summary>
        protected VisualElement MountAndResolve(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        /// <summary>Minimal host that supplies a real panel. Nested so each fixture need not declare its own.</summary>
        private sealed class TestHostWindow : EditorWindow { }
    }
}
