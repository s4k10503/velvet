using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The preview window's Viewport addon: selecting a fixed viewport sizes the mount canvas to that reference
    /// width and marks it a responsive scope (<c>@container</c>) so a mounted story's <c>sm:</c>/<c>md:</c>…
    /// evaluate against the simulated width; <b>Full</b> removes the marker and restores the fill behavior.
    /// </summary>
    /// <remarks>
    /// The viewport selection handler (<c>SetViewport</c>) and the mount canvas are private to the window; the
    /// tests reach them by reflection (the repo convention for verifying internals from a test assembly that
    /// lacks InternalsVisibleTo).
    /// </remarks>
    internal sealed class VelvetPreviewWindowViewportTests
    {
        private const string MobileViewport = "Mobile (375)";
        private const float MobileWidth = 375f;
        private const string FullViewport = "Full";

        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            EditorPrefs.SetString("Velvet.Preview.Viewport", FullViewport);
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

        private void SetViewport(string viewport)
        {
            var method = typeof(VelvetPreviewWindow).GetMethod(
                "SetViewport", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "VelvetPreviewWindow.SetViewport must exist");
            method.Invoke(_window, new object[] { viewport });
        }

        private VisualElement Canvas()
        {
            var field = typeof(VelvetPreviewWindow).GetField(
                "_canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "VelvetPreviewWindow._canvas must exist");
            return (VisualElement)field.GetValue(_window);
        }

        [Test]
        public void Given_AFixedViewport_When_Selected_Then_TheCanvasWidthBecomesThePresetWidth()
        {
            // Arrange / Act
            SetViewport(MobileViewport);

            // Assert
            Assert.That(Canvas().style.width.value.value, Is.EqualTo(MobileWidth));
        }

        [Test]
        public void Given_AFixedViewport_When_Selected_Then_TheCanvasIsMarkedAsAResponsiveScope()
        {
            // Arrange / Act
            SetViewport(MobileViewport);

            // Assert
            Assert.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.True);
        }

        [Test]
        public void Given_AFixedViewport_When_SwitchedBackToFull_Then_TheScopeMarkerIsRemoved()
        {
            // Arrange — a fixed viewport first marks the canvas.
            SetViewport(MobileViewport);
            Assume.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.True,
                "Precondition: the fixed viewport marked the canvas");

            // Act
            SetViewport(FullViewport);

            // Assert
            Assert.That(Canvas().ClassListContains(VelvetResponsive.ContainerClass), Is.False);
        }
    }
}
