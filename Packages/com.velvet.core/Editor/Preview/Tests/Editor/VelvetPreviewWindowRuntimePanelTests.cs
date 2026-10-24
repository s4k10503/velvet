using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The preview window shows a story through a runtime panel that renders into a texture: the texture holds
    /// what a runtime panel on the chosen <see cref="PanelSettings"/> renders, and a pointer on the canvas showing
    /// it reaches the story.
    /// </summary>
    internal sealed class VelvetPreviewWindowRuntimePanelTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PanelSettingsKey = "Velvet.Preview.PanelSettings";
        private const int FramesBeforeReadback = 8;

        // Milliseconds on the panel clock past any time a tick the window scheduled can be due.
        private const long FarFuture = 10_000_000_000L;

        // ScaleWithScreenSize at a 1280x720 reference: a 128x72 screen lays the panel out at a tenth of a pixel
        // per unit.
        private const string StarterPanelSettings = "Assets/VelvetStarterSample/StarterAppPanelSettings.asset";
        private const int ScreenWidth = 128;
        private const int ScreenHeight = 72;

        private const string MeasureTarget = "measure-target";

        private static int s_clicks;

        private VelvetPreviewWindow _window;

        // Half the panel's width in its units, so it covers the left half of the screen only at the settings' scale.
        private static VNode LeftHalfRed() => V.Div("w-[640px] h-[720px] bg-[#ff0000]");

        // Half of a 640x360 footprint's width.
        private static VNode HalfFootprintRed() => V.Div("w-[320px] h-[360px] bg-[#ff0000]");

        // The left half of the screen, drawn by a host panel sorted below the story's own.
        private static VNode BackgroundPortal() =>
            V.Div("w-[100px] h-[60px]", V.Portal(UILayer.Background, new VNode[] { V.Div("w-[50px] h-[60px] bg-[#ff0000]") }));

        private static VNode ClickTarget() => V.Button("w-[100px] h-[60px]", onClick: () => s_clicks++);

        // Off the canvas's origin, so a pointer read at the wrong offset misses it.
        private static VNode Measured() =>
            V.Div(className: "w-[40px] h-[20px] ml-[40px] mt-[20px]", name: MeasureTarget);

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel and a runtime panel's render");
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            EditorPrefs.SetString("Velvet.Preview.Viewport", "Full");
            EditorPrefs.SetString("Velvet.Preview.Zoom", "100%");
            EditorPrefs.SetString(PanelSettingsKey, string.Empty);
            s_clicks = 0;
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.position = new Rect(0, 0, 640, 480);
            _window.Show();
            _window.RefreshStories(() => new List<VelvetPreviewStory>());
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.SetString(PanelSettingsKey, string.Empty);
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        [UnityTest]
        public IEnumerator Given_ChosenPanelSettings_When_AStoryIsShown_Then_TheCanvasShowsWhatARuntimePanelOnThemRenders()
        {
            // Arrange
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StarterPanelSettings);
            using var game = new GamePanel(settings, ScreenWidth, ScreenHeight, Story(nameof(LeftHalfRed)));
            Choose(settings);
            SetCustomViewport(ScreenWidth, ScreenHeight);

            // Act
            Select(Story(nameof(LeftHalfRed)));
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var expected = game.Read();
            var shown = ReadShown();

            // Assert
            Assert.That(
                (IsRedAt(expected, ScreenWidth / 4, ScreenHeight / 2, ScreenWidth),
                    IsRedAt(expected, ScreenWidth * 3 / 4, ScreenHeight / 2, ScreenWidth),
                    CountDiffering(shown, expected)),
                Is.EqualTo((true, false, 0)));
        }

        // The footprint's panel takes the scale the settings resolve for the screen, a tenth, rather than the one
        // they would resolve for a 64x36 texture, a twentieth, which would leave the red a quarter wide.
        [UnityTest]
        public IEnumerator Given_ChosenPanelSettings_When_AStoryWithAnExplicitSizeIsShown_Then_ItIsAtTheScreensScale()
        {
            // Arrange
            Choose(AssetDatabase.LoadAssetAtPath<PanelSettings>(StarterPanelSettings));
            SetCustomViewport(ScreenWidth, ScreenHeight);

            // Act
            Select(Story(nameof(HalfFootprintRed), width: 640, height: 360));
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var shown = ReadShown();

            // Assert
            Assert.That(
                (shown.Length, IsRedAt(shown, 24, 18, 64), IsRedAt(shown, 48, 18, 64)),
                Is.EqualTo((64 * 36, true, false)));
        }

        [UnityTest]
        public IEnumerator Given_AStoryWithABackgroundPortal_When_ItIsShown_Then_TheCanvasShowsThePortal()
        {
            // Arrange
            SetCustomViewport(100, 60);

            // Act
            Select(Story(nameof(BackgroundPortal)));
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var shown = ReadShown();

            // Assert
            Assert.That(
                (shown.Length, IsRedAt(shown, 25, 30, 100), IsRedAt(shown, 75, 30, 100)),
                Is.EqualTo((100 * 60, true, false)));
        }

        // GREEN_ON_BASE(characterization): a click on the canvas over a story's button ran its handler on the base.
        // There the story was mounted on the canvas itself; here the canvas sends the click on to the runtime panel.
        [UnityTest]
        public IEnumerator Given_AShownButton_When_TheCanvasIsClickedOverIt_Then_ItsHandlerRuns()
        {
            // Arrange
            SetCustomViewport(100, 60);
            Select(Story(nameof(ClickTarget)));
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var at = CanvasCenter();

            // Act
            SendToWindow(PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, at)));
            SendToWindow(PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, at)));

            // Assert
            Assert.That(s_clicks, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): Measure picked the story element under the pointer on the base.
        // There it picked on the window's own panel; here it picks on the runtime panel at the mapped position.
        [UnityTest]
        public IEnumerator Given_Measure_When_ThePointerMovesOverAStoryElement_Then_ThatElementIsMeasured()
        {
            // Arrange
            SetCustomViewport(100, 60);
            Select(Story(nameof(Measured)));
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var overlay = (PreviewInspectOverlay)typeof(VelvetPreviewWindow).GetField("_overlay", Private)
                ?.GetValue(_window);
            overlay.MeasureEnabled = true;
            var at = CanvasCenter();

            // Act
            SendToWindow(PointerMoveEvent.GetPooled(Mouse(EventType.MouseMove, at)));

            // Assert
            var measured = (VisualElement)typeof(PreviewInspectOverlay).GetField("_measured", Private)
                ?.GetValue(overlay);
            Assert.That(measured?.name, Is.EqualTo(MeasureTarget));
        }

        // GREEN_ON_BASE(characterization): the base already shows the panel's texture on the canvas unfiltered.
        [Test]
        public void Given_AShownPanel_When_ItsTextureIsShownOnTheCanvas_Then_ItIsSampledWithoutFiltering()
        {
            // Arrange
            var panel = Field<VelvetPreviewPanel>("_panel");

            // Act
            var filtering = panel.Texture.filterMode;

            // Assert
            Assert.That(filtering, Is.EqualTo(FilterMode.Point));
        }

        // GREEN_ON_BASE(characterization): the base already replaces a panel a scene change destroyed when its tick
        // runs. Disposing the panel is how the test destroys it.
        [Test]
        public void Given_ADestroyedPanel_When_TheWindowsTickRuns_Then_TheWindowShowsANewPanel()
        {
            // Arrange
            var destroyed = Field<VelvetPreviewPanel>("_panel");
            destroyed.Dispose();

            // Act
            RunScheduledTick();

            // Assert
            Assert.That(ReferenceEquals(Field<VelvetPreviewPanel>("_panel"), destroyed), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already puts the panel's texture back on a canvas that lost it
        // when the tick runs.
        [Test]
        public void Given_ACanvasWithoutTheTexture_When_TheTickRuns_Then_TheCanvasShowsItAgain()
        {
            // Arrange
            SetCustomViewport(100, 60);
            SettleLayout();
            ClearCanvasBackground();

            // Act
            typeof(VelvetPreviewWindow).GetMethod("TickPanel", Private)?.Invoke(_window, null);
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                Field<VisualElement>("_canvas").resolvedStyle.backgroundImage.renderTexture,
                Is.SameAs(Field<VelvetPreviewPanel>("_panel").Texture));
        }

        // GREEN_ON_BASE(characterization): the base already puts the panel's texture back on a canvas that lost it
        // when the canvas is sized again. The panel's clock is held still so the tick, which does the same, does not
        // run in between.
        [Test]
        public void Given_ACanvasWithoutTheTexture_When_ItIsSizedAgain_Then_TheCanvasShowsItAgain()
        {
            // Arrange
            SetCustomViewport(100, 60);
            HoldPanelClock();
            SettleLayout();
            ClearCanvasBackground();

            // Act
            SetCustomViewport(100, 60);
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                Field<VisualElement>("_canvas").resolvedStyle.backgroundImage.renderTexture,
                Is.SameAs(Field<VelvetPreviewPanel>("_panel").Texture));
        }

        // GREEN_ON_BASE(characterization): the base already leaves a panel for a story without a size at the scaling
        // its settings resolve for the texture, and pins the scale only for a story with a footprint.
        [Test]
        public void Given_ChosenPanelSettings_When_AStoryWithoutASizeIsShown_Then_ThePanelKeepsTheSettingsScaleMode()
        {
            // Arrange
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StarterPanelSettings);
            Choose(settings);
            SetCustomViewport(ScreenWidth, ScreenHeight);

            // Act
            Select(Story(nameof(LeftHalfRed)));

            // Assert
            var copy = (PanelSettings)typeof(VelvetPreviewPanel).GetField("_settings", Private)
                ?.GetValue(Field<VelvetPreviewPanel>("_panel"));
            Assert.That(
                (settings.scaleMode, copy.scaleMode),
                Is.EqualTo((PanelScaleMode.ScaleWithScreenSize, PanelScaleMode.ScaleWithScreenSize)));
        }

        // Fires the tick the window schedules on its panel once, by putting the panel's clock past the time it is due.
        private void RunScheduledTick()
        {
            var panel = _window.rootVisualElement.panel;
            UseFrameFakeClockHost.Ms = FarFuture;
            EditorPanelTestHelpers.SetPanelTimeFunction(panel, UseFrameFakeClockHost.ReadFakeClock);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
        }

        // A clock that never reaches the time the tick is due, so no layout pass runs it.
        private void HoldPanelClock()
        {
            UseFrameFakeClockHost.Ms = 0;
            EditorPanelTestHelpers.SetPanelTimeFunction(
                _window.rootVisualElement.panel, UseFrameFakeClockHost.ReadFakeClock);
        }

        // Two passes: the second takes up the geometry events the first raises.
        private void SettleLayout()
        {
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private void ClearCanvasBackground()
        {
            Field<VisualElement>("_canvas").style.backgroundImage = new StyleBackground(StyleKeyword.None);
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        private static VelvetPreviewStory Story(string method, int width = 0, int height = 0) =>
            PreviewStoryTestFactory.Build(
                typeof(VelvetPreviewWindowRuntimePanelTests).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic),
                new VelvetPreviewAttribute
                {
                    Name = method, Group = nameof(VelvetPreviewWindowRuntimePanelTests), Width = width, Height = height,
                });

        private void Choose(PanelSettings settings) =>
            _window.rootVisualElement.Query<ObjectField>().Where(f => f.objectType == typeof(PanelSettings)).First()
                .value = settings;

        private void SetCustomViewport(int width, int height)
        {
            Field<IntegerField>("_viewportWidthField").SetValueWithoutNotify(width);
            Field<IntegerField>("_viewportHeightField").SetValueWithoutNotify(height);
            typeof(VelvetPreviewWindow).GetMethod("OnViewportFieldChanged", Private)?.Invoke(_window, null);
        }

        private void Select(VelvetPreviewStory story) =>
            typeof(VelvetPreviewWindow).GetMethod("Select", Private)?.Invoke(_window, new object[] { story });

        private T Field<T>(string field) =>
            (T)typeof(VelvetPreviewWindow).GetField(field, Private)?.GetValue(_window);

        // Empty when the canvas shows no texture.
        private Color32[] ReadShown()
        {
            // resolvedStyle, as ShownTextureSize in PreviewPanelScaleWindowTests reads it.
            var texture = Field<VisualElement>("_canvas")?.resolvedStyle.backgroundImage.renderTexture;
            return texture == null
                ? Array.Empty<Color32>()
                : RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, texture.width, texture.height));
        }

        private Vector2 CanvasCenter()
        {
            EditorPanelTestHelpers.ForcePanelUpdate(_window.rootVisualElement.panel);
            return Field<VisualElement>("_canvas").worldBound.center;
        }

        private void SendToWindow(EventBase evt)
        {
            using (evt)
            {
                _window.rootVisualElement.panel.visualTree.SendEvent(evt);
            }
        }

        private static Event Mouse(EventType type, Vector2 at) =>
            new() { type = type, mousePosition = at, button = 0, clickCount = 1 };

        private static bool IsRedAt(Color32[] pixels, int x, int y, int width)
        {
            var index = y * width + x;
            return index < pixels.Length && RenderTexturePixelReader.IsRedPixel(pixels[index]);
        }

        // Every pixel when the two differ in size.
        private static int CountDiffering(Color32[] shown, Color32[] expected)
        {
            if (shown.Length != expected.Length) return Math.Max(shown.Length, expected.Length);
            var differing = 0;
            for (var i = 0; i < shown.Length; i++)
            {
                if (!shown[i].Equals(expected[i])) differing++;
            }

            return differing;
        }

        // The game's own runtime panel on the same settings, rendering into a texture it clears to transparent.
        private sealed class GamePanel : IDisposable
        {
            private readonly GameObject _host;
            private readonly PanelSettings _settings;
            private readonly RenderTexture _texture;
            private readonly MountedTree _mounted;

            public GamePanel(PanelSettings settings, int width, int height, VelvetPreviewStory story)
            {
                _texture = new RenderTexture(width, height, 24);
                _settings = UnityEngine.Object.Instantiate(settings);
                _settings.targetTexture = _texture;
                _settings.clearColor = true;
                _settings.colorClearValue = Color.clear;
                _host = new GameObject(nameof(GamePanel));
                var document = _host.AddComponent<UIDocument>();
                document.panelSettings = _settings;
                VelvetStyleUtilities.AttachTo(document.rootVisualElement);
                _mounted = V.Mount(document.rootVisualElement, story.Build());
            }

            public Color32[] Read() =>
                RenderTexturePixelReader.ReadPixels(_texture, new RectInt(0, 0, _texture.width, _texture.height));

            public void Dispose()
            {
                _mounted.Dispose();
                UnityEngine.Object.DestroyImmediate(_host);
                UnityEngine.Object.DestroyImmediate(_settings);
                _texture.Release();
                UnityEngine.Object.DestroyImmediate(_texture);
            }
        }
    }
}
