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
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
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
}
