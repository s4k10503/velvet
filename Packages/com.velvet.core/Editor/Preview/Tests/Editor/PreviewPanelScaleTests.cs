using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class PreviewPanelScaleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private PanelSettings _settings;
        private VelvetPreviewWindow _window;
        private string _zoomBefore;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<PanelSettings>();

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.SetPanelSettings(null);
                Invoke("SetZoom", _zoomBefore);
                Invoke("SetViewport", "Full");
                _window.Close();
                _window = null;
            }

            Object.DestroyImmediate(_settings);
        }

        // Each case against PanelSettings' own resolution, which it keeps internal, so a Unity release that
        // changes the formula reddens here rather than leaving the window's scale silently off.
        [TestCase(PanelScaleMode.ConstantPixelSize, PanelScreenMatchMode.MatchWidthOrHeight, 0f, 2f, 0f, 1280)]
        [TestCase(PanelScaleMode.ConstantPhysicalSize, PanelScreenMatchMode.MatchWidthOrHeight, 0f, 1f, 192f, 1280)]
        [TestCase(PanelScaleMode.ConstantPhysicalSize, PanelScreenMatchMode.MatchWidthOrHeight, 0f, 1f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.MatchWidthOrHeight, 0.25f, 1f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.MatchWidthOrHeight, 1.5f, 1f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.Expand, 0f, 1f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.Shrink, 0f, 1.5f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.Shrink, 0f, 0f, 0f, 1280)]
        [TestCase(PanelScaleMode.ScaleWithScreenSize, PanelScreenMatchMode.Shrink, 0f, 1f, 0f, 0)]
        public void Given_PanelSettings_When_TheWindowResolvesTheirScale_Then_ItMatchesTheRuntimePanels(
            PanelScaleMode scaleMode, PanelScreenMatchMode matchMode, float match, float scale, float screenDpi,
            int referenceWidth)
        {
            // Arrange
            _settings.scaleMode = scaleMode;
            _settings.screenMatchMode = matchMode;
            _settings.match = match;
            _settings.scale = scale;
            _settings.referenceResolution = new Vector2Int(referenceWidth, 720);
            _settings.referenceDpi = 96f;
            _settings.fallbackDpi = 72f;
            var screen = new Vector2(1600f, 1200f);
            var runtime = (float)typeof(PanelSettings).GetMethod("ResolveScale", Private)!
                .Invoke(_settings, new object[] { new Rect(Vector2.zero, screen), screenDpi });

            // Act
            var resolved = PreviewPanelScale.Resolve(_settings, screen, screenDpi);

            // Assert
            Assert.That(resolved, Is.EqualTo(runtime).Within(1e-6f));
        }

        [Test]
        public void Given_PanelSettingsScalingWithAScreenTwiceTheirReference_When_ChosenInTheWindow_Then_TheCanvasLaysOutAtTheReferenceAndPaintsAtTwice()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _zoomBefore = EditorPrefs.GetString("Velvet.Preview.Zoom", "Fit");
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
            Invoke("SetZoom", "100%");
            _window.RefreshStories(() => new System.Collections.Generic.List<VelvetPreviewStory>());
            SetCustomViewport(1920, 1080);
            _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _settings.referenceResolution = new Vector2Int(960, 540);

            // Act
            _window.SetPanelSettings(_settings);

            // Assert
            var canvas = (VisualElement)typeof(VelvetPreviewWindow).GetField("_canvas", Private)?.GetValue(_window);
            Assert.That(
                (canvas?.style.width.value.value, canvas?.style.scale.value.value.x),
                Is.EqualTo(((float?)960f, (float?)2f)));
        }

        private void Invoke(string method, object argument) =>
            typeof(VelvetPreviewWindow).GetMethod(method, Private)?.Invoke(_window, new[] { argument });

        private void SetCustomViewport(int width, int height)
        {
            var w = (IntegerField)typeof(VelvetPreviewWindow).GetField("_viewportWidthField", Private)?.GetValue(_window);
            var h = (IntegerField)typeof(VelvetPreviewWindow).GetField("_viewportHeightField", Private)?.GetValue(_window);
            w?.SetValueWithoutNotify(width);
            h?.SetValueWithoutNotify(height);
            typeof(VelvetPreviewWindow).GetMethod("OnViewportFieldChanged", Private)?.Invoke(_window, null);
        }
    }
}
