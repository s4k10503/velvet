using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
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
            // CreateGUI builds the toolbar lazily; force the visual tree to exist before querying it.
            _window.rootVisualElement.Q<ToolbarToggle>();
            foreach (var toggle in _window.rootVisualElement.Query<ToolbarToggle>().ToList())
            {
                if (toggle.text == "Dark") return toggle;
            }

            return null;
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
