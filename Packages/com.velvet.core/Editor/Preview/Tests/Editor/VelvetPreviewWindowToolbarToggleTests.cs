using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The preview window's inspection addons: the Outline toggle drives the overlay's active state, and the
    /// overlay is non-interactive (<see cref="PickingMode.Ignore"/>) so it can never steal a pointer event from
    /// the live story underneath.
    /// </summary>
    internal sealed class VelvetPreviewWindowInspectTests
    {
        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            // Start from a known default so the toggle's starting value is deterministic.
            EditorPrefs.SetBool("Velvet.Preview.Outline", false);
            // Cleared before the window is created so CreateGUI's RefreshStories cannot restore a persisted
            // selection (e.g. a developer having clicked an example story in the live window).
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
            // The window instance can be reused across tests (GetWindow finds the existing one), so its own
            // auto-selection from a previous run could still be sitting in _selected — null it explicitly.
            ResetSelection();
        }

        // Restores the pre-example baseline: no story selected. This fixture's assertions do not depend on canvas
        // sizing, but a null selection keeps it consistent with the other preview-window fixtures and out of the
        // way of whatever the registry happens to discover.
        private void ResetSelection()
        {
            var select = typeof(VelvetPreviewWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(select, Is.Not.Null, "VelvetPreviewWindow.Select must exist");
            select.Invoke(_window, new object[] { null });
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private VisualElement Overlay()
        {
            foreach (var element in _window.rootVisualElement.Query<VisualElement>().ToList())
            {
                if (element.GetType().Name == "PreviewInspectOverlay") return element;
            }

            return null;
        }

        private ToolbarToggle ToggleNamed(string label)
        {
            foreach (var toggle in _window.rootVisualElement.Query<ToolbarToggle>().ToList())
            {
                if (toggle.text == label) return toggle;
            }

            return null;
        }

        [Test]
        public void Given_TheOutlineToggle_When_TurnedOn_Then_TheOverlayOutlineIsEnabled()
        {
            // Arrange
            var overlay = Overlay();
            var outlineToggle = ToggleNamed("Outline");
            Assume.That(overlay, Is.Not.Null, "the stage must host the inspect overlay");
            Assume.That(outlineToggle, Is.Not.Null, "the toolbar must expose an Outline toggle");

            // Act
            outlineToggle.value = true;

            // Assert
            var prop = overlay.GetType().GetProperty("OutlineEnabled", BindingFlags.Instance | BindingFlags.Public);
            Assert.That((bool)prop.GetValue(overlay), Is.True);
        }

        [Test]
        public void Given_TheInspectOverlay_When_Built_Then_ItIgnoresPointerEvents()
        {
            // Arrange
            var overlay = Overlay();
            Assume.That(overlay, Is.Not.Null, "the stage must host the inspect overlay");

            // Act
            var picking = overlay.pickingMode;

            // Assert
            Assert.That(picking, Is.EqualTo(PickingMode.Ignore));
        }
    }

    /// <summary>
    /// The preview window's Dark toggle drives <see cref="VelvetTheme.IsDark"/> live (so a story's <c>dark:</c>
    /// variants re-evaluate), and closing the window restores whatever theme value was there before — the
    /// non-destructive contract that keeps a preview toggle from leaking into a running game.
    /// </summary>
    internal sealed class VelvetPreviewWindowThemeTests
    {
        private VelvetPreviewWindow _window;
        private bool _originalIsDark;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _originalIsDark = VelvetTheme.IsDark;
            // Start from a known editor-side default so the toggle's restore target is deterministic.
            EditorPrefs.SetBool("Velvet.Preview.Dark", false);
            // Cleared before the window is created (in ShowWindowAndGetDarkToggle) so CreateGUI's RefreshStories
            // cannot restore a persisted selection (e.g. a developer having clicked an example story in the live
            // window).
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }

            VelvetTheme.IsDark = _originalIsDark;
        }

        private ToolbarToggle ShowWindowAndGetDarkToggle()
        {
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
            // The window instance can be reused across tests (GetWindow finds the existing one), so its own
            // auto-selection from a previous run could still be sitting in _selected — null it explicitly. These
            // tests only assert on VelvetTheme.IsDark, but a null selection keeps this fixture consistent with the
            // other preview-window fixtures and out of the way of whatever the registry happens to discover.
            ResetSelection();
            // CreateGUI builds the toolbar lazily; force the visual tree to exist before querying it.
            _window.rootVisualElement.Q<ToolbarToggle>();
            foreach (var toggle in _window.rootVisualElement.Query<ToolbarToggle>().ToList())
            {
                if (toggle.text == "Dark") return toggle;
            }

            return null;
        }

        private void ResetSelection()
        {
            var select = typeof(VelvetPreviewWindow).GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(select, Is.Not.Null, "VelvetPreviewWindow.Select must exist");
            select.Invoke(_window, new object[] { null });
        }

        [Test]
        public void Given_ThePreviewWindow_When_TheDarkToggleIsTurnedOn_Then_VelvetThemeIsDark()
        {
            // Arrange
            VelvetTheme.IsDark = false;
            var darkToggle = ShowWindowAndGetDarkToggle();
            Assume.That(darkToggle, Is.Not.Null, "the window must expose a Dark toolbar toggle");

            // Act
            darkToggle.value = true;

            // Assert
            Assert.That(VelvetTheme.IsDark, Is.True);
        }

        [Test]
        public void Given_DarkToggledOn_When_TheWindowCloses_Then_ThePreviousThemeValueIsRestored()
        {
            // Arrange — a running game had dark mode ON before the preview window opened.
            VelvetTheme.IsDark = true;
            var darkToggle = ShowWindowAndGetDarkToggle();
            Assume.That(darkToggle, Is.Not.Null, "the window must expose a Dark toolbar toggle");
            darkToggle.value = false;                       // window drives the theme to its own (off) value
            Assume.That(VelvetTheme.IsDark, Is.False, "the toggle should have driven the theme off");

            // Act
            _window.Close();
            _window = null;

            // Assert
            Assert.That(VelvetTheme.IsDark, Is.True);       // restored to the pre-window value
        }
    }
}
