using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class PreviewPanelScaleTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private PanelSettings _settings;

        [SetUp]
        public void SetUp() => _settings = ScriptableObject.CreateInstance<PanelSettings>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_settings);

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
    }

    internal sealed class PreviewPanelScaleWindowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PanelSettingsKey = "Velvet.Preview.PanelSettings";

        // ScaleWithScreenSize at a 1280x720 reference, matching width and height equally.
        private const string StarterPanelSettings = "Assets/VelvetStarterSample/StarterAppPanelSettings.asset";

        private VelvetPreviewWindow _window;
        private PanelSettings _settings;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            EditorPrefs.SetString("Velvet.Preview.Viewport", "Full");
            EditorPrefs.SetString("Velvet.Preview.Zoom", "100%");
            EditorPrefs.SetString(PanelSettingsKey, string.Empty);
            _settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StarterPanelSettings);
            Open();
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.SetString(PanelSettingsKey, string.Empty);
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private void Open()
        {
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.position = new Rect(0, 0, 640, 480);
            _window.Show();
            _window.RefreshStories(() => new List<VelvetPreviewStory>());
        }

        private void Choose(PanelSettings settings) =>
            _window.rootVisualElement.Query<ObjectField>().Where(f => f.objectType == typeof(PanelSettings)).First()
                .value = settings;

        private T Field<T>(string field) =>
            (T)typeof(VelvetPreviewWindow).GetField(field, Private)?.GetValue(_window);

        private void SetCustomViewport(int width, int height)
        {
            Field<IntegerField>("_viewportWidthField").SetValueWithoutNotify(width);
            Field<IntegerField>("_viewportHeightField").SetValueWithoutNotify(height);
            typeof(VelvetPreviewWindow).GetMethod("OnViewportFieldChanged", Private)?.Invoke(_window, null);
        }

        [Test]
        public void Given_AViewportTwiceThePanelsReference_When_ThePanelSettingsAreChosen_Then_TheCanvasLaysOutAtTheReferenceAndPaintsAtTwice()
        {
            // Arrange
            SetCustomViewport(2560, 1440);

            // Act
            Choose(_settings);

            // Assert
            var canvas = Field<VisualElement>("_canvas");
            var status = (string)typeof(VelvetPreviewWindow).GetMethod("DescribeViewport", Private)
                ?.Invoke(_window, new object[] { null });
            Assert.That(
                (canvas?.style.width.value.value, canvas?.style.scale.value.value.x,
                    status?.EndsWith("panel ×2", StringComparison.Ordinal)),
                Is.EqualTo(((float?)1280f, (float?)2f, (bool?)true)));
        }

        [Test]
        public void Given_TheFullViewport_When_ThePanelSettingsAreChosen_Then_TheStageIsTheScreenTheyScaleFor()
        {
            // Arrange
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
            var stage = Field<VisualElement>("_stage");
            var screen = new Vector2(stage.resolvedStyle.width, stage.resolvedStyle.height);
            var unitsPerPixel = PreviewPanelScale.Resolve(_settings, screen, Screen.dpi);

            // Act
            Choose(_settings);

            // Assert
            Assert.That(
                Field<VisualElement>("_canvas").style.width.value.value,
                Is.EqualTo(screen.x * unitsPerPixel).Within(1e-3f));
        }

        [Test]
        public void Given_ChosenPanelSettings_When_TheWindowReopens_Then_TheyAreStillChosen()
        {
            // Arrange
            Choose(_settings);
            _window.Close();

            // Act
            Open();

            // Assert
            Assert.That(Field<PanelSettings>("_panelSettings"), Is.SameAs(_settings));
        }
    }
}
