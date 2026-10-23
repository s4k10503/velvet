using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Measures a <c>backdrop-*</c> utility on a screen-space overlay panel, which draws into whatever target is bound
    /// when the overlays render. The engine's overlay render is run here with a render texture bound, since batch mode
    /// does not run a <see cref="WaitForEndOfFrame"/> to read the screen after. The stage and the box span the target's
    /// full height, so the readback's row order does not decide a sample.
    /// </summary>
    [Timeout(600000)]
    internal sealed class BackdropFilterOverlayPlaybackTests
    {
        private GameObject _docGo;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_docGo != null) UnityEngine.Object.Destroy(_docGo);
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ABackdropBlurOnAnOverlayPanel_When_TheOverlayRenders_Then_TheEdgeBlursOnlyInsideTheBox()
        {
            // Arrange — red and blue halves meeting at the screen's middle, under a 60px box astride it.
            var width = Screen.width;
            var height = Screen.height;
            var edge = width / 2;
            _docGo = new GameObject(nameof(BackdropFilterOverlayPlaybackTests));
            var document = _docGo.AddComponent<UIDocument>();
            _settings = TestPanelSettings.Create();
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            document.panelSettings = _settings;
            yield return null;
            _mounted = V.Mount(document.rootVisualElement, V.Div(name: "stage", className: $"w-[{width}px] h-[{height}px]",
                children: new VNode[]
                {
                    V.Div(className: $"w-[{edge}px] h-[{height}px] bg-[#ff0000]"),
                    V.Div(className: $"w-[{width - edge}px] h-[{height}px] bg-[#0000ff]"),
                    V.Div(name: "glass", className: $"w-[60px] h-[{height}px] backdrop-blur-md"),
                }));
            document.rootVisualElement.Q<VisualElement>("stage").style.flexDirection = FlexDirection.Row;
            var glass = document.rootVisualElement.Q<VisualElement>("glass").style;
            glass.position = Position.Absolute;
            glass.left = edge - 30f;
            glass.top = 0f;
            yield return PlayModeRealtimeTestHelpers.WaitRealtime(0.5);

            // Act
            var row = RenderOverlayInto(document.rootVisualElement.panel, width, height);
            var red = row[edge - 60];
            var inside = row[edge + 1];
            var blue = row[edge + 60];

            // Assert — the halves read as themselves outside the box, which says the readback holds the panel.
            Assert.That((red.r > 200 && red.b < 20, inside.r > 60 && inside.b > 60, blue.b > 200 && blue.r < 20),
                Is.EqualTo((true, true, true)), $"red={red} inside={inside} blue={blue} screen={width}x{height}");
        }

        // The middle row of what the overlay draws into a cleared target of the screen's size, through the call the
        // player loop renders each overlay panel with.
        private static Color32[] RenderOverlayInto(IPanel panel, int width, int height)
        {
            var render = typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
                ?.GetMethod("RenderPanel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("UIElementsRuntimeUtility", "RenderPanel");
            var target = new RenderTexture(width, height, 24);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                render.Invoke(null, new object[] { panel, true });
                return RenderTexturePixelReader.ReadPixels(target, new RectInt(0, height / 2, width, 1));
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}
