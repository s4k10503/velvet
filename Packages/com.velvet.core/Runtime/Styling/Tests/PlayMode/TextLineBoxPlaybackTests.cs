#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a text element Velvet creates draws its glyphs in its line box: half the leading below the
    /// box's top, as CSS places them, and centred in a button, as a browser centres a button's content. Each pixel case
    /// compares two renders of the same font at the same size, so the font's own metrics cancel and what is left is a
    /// difference of declared heights.
    /// </summary>
    [Timeout(600000)]
    internal sealed class TextLineBoxPlaybackTests
    {
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";
        private const int Width = 200;
        private const int Height = 120;

        private GameObject _host;
        private PanelSettings _settings;
        private RenderTexture _texture;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Close();
            yield return null;
        }

        private void Close()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_host != null)
            {
                Object.Destroy(_host);
                _host = null;
            }
            if (_settings != null)
            {
                Object.Destroy(_settings);
                _settings = null;
            }
            if (_texture != null)
            {
                _texture.Release();
                Object.Destroy(_texture);
                _texture = null;
            }
        }

        // Renders the node on the runtime theme, which gives the text its font, with the bundled sheet attached, and
        // returns how far below the named element's top its first dark row is.
        private IEnumerator InkTop(VNode node, float[] result)
        {
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Assume.That(theme, Is.Not.Null, "Precondition: the project's runtime theme is at " + ThemePath);
            _texture = new RenderTexture(Width, Height, 32);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = theme;
            _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _settings.targetTexture = _texture;
            _settings.clearColor = true;
            _settings.colorClearValue = Color.clear;
            _host = new GameObject(nameof(TextLineBoxPlaybackTests));
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(document.rootVisualElement);
            _mounted = V.Mount(document.rootVisualElement, node);
            yield return WaitRealtimeDraining(0.4, _texture);

            var box = document.rootVisualElement.Q<VisualElement>("text").worldBound;
            var pixels = RenderTexturePixelReader.ReadPixels(_texture, new RectInt(0, 0, Width, Height));
            result[0] = float.NaN;
            // The readback's first row is the bottom of the panel.
            for (var y = Mathf.Max(0, Mathf.FloorToInt(box.yMin) - 20); y < Height; y++)
            {
                var row = Height - 1 - y;
                for (var x = Mathf.CeilToInt(box.xMin); x < Mathf.FloorToInt(box.xMax); x++)
                {
                    var p = pixels[(row * Width) + x];
                    if (p.a > 200 && p.r < 100)
                    {
                        result[0] = y - box.yMin;
                        Close();
                        yield break;
                    }
                }
            }
            Close();
        }

        private static VNode Text(string className)
            => V.Label(name: "text", className: "w-[150px] bg-white text-black " + className, text: "HxgH");

        private IEnumerator Difference(VNode first, VNode second, float[] result)
        {
            var a = new float[1];
            var b = new float[1];
            yield return InkTop(first, a);
            yield return InkTop(second, b);
            result[0] = a[0] - b[0];
        }

        [UnityTest]
        public IEnumerator Given_TwoLineHeightsUnderTheDefaultAlignment_When_Rendered_Then_TheGlyphsMoveByHalfTheirDifference()
        {
            // Arrange & Act
            var difference = new float[1];
            yield return Difference(Text("text-base leading-8"), Text("text-base leading-6"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(4f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_TwoLineHeightsUnderAMiddleAlignment_When_Rendered_Then_TheGlyphsMoveByHalfTheirDifference()
        {
            // Arrange & Act
            var difference = new float[1];
            yield return Difference(Text("text-base leading-8 text-left"), Text("text-base leading-6 text-left"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(4f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ALineHeightBelowTheFontsAndOneAbove_When_Rendered_Then_TheGlyphsMoveByHalfTheirDifference()
        {
            // Arrange & Act — 40px is below the content height of a 36px font and 48px above it.
            var difference = new float[1];
            yield return Difference(Text("text-4xl leading-[48px] text-left"), Text("text-4xl leading-[40px] text-left"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(4f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ABoxTallerThanItsLineUnderAMiddleAlignment_When_Rendered_Then_TheGlyphsStayInTheFirstLineBox()
        {
            // Arrange & Act
            var difference = new float[1];
            yield return Difference(Text("text-base leading-8 text-left h-[80px]"), Text("text-base leading-8 text-left"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(0f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ALineStretchedByAFlexRow_When_Rendered_Then_TheGlyphsStayInTheFirstLineBox()
        {
            // Arrange & Act
            var difference = new float[1];
            var row = V.Div(className: "flex flex-row h-[80px]", children: new VNode[] { Text("text-base leading-8 text-left") });
            yield return Difference(row, Text("text-base leading-8 text-left"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(0f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ALineHeightBelowTheFontsUnderTheDefaultAlignment_When_Rendered_Then_TheGlyphsMoveByHalfTheDifference()
        {
            // Arrange & Act — 40px is below the content height of a 36px font and 48px above it.
            var difference = new float[1];
            yield return Difference(Text("text-4xl leading-[48px]"), Text("text-4xl leading-[40px]"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(4f).Within(1f));
        }

        // GREEN_ON_BASE(characterization): on the base both boxes are the font's height and both put the glyphs at
        // its top. A default alignment left upper-left, which cannot raise its glyphs, reads two pixels low here.
        [UnityTest]
        public IEnumerator Given_ALineHeightBelowTheFontsUnderTheDefaultAlignment_When_Rendered_Then_TheGlyphsSitWhereAMiddleAlignmentPutsThem()
        {
            // Arrange & Act
            var difference = new float[1];
            yield return Difference(Text("text-4xl leading-none"), Text("text-4xl leading-none text-left"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(0f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ThreeLinesUnderThePreflightLineHeight_When_Shown_Then_TheirAdvanceIsThePreflights()
        {
            // Arrange
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Assume.That(theme, Is.Not.Null, "Precondition: the project's runtime theme is at " + ThemePath);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = theme;
            _host = new GameObject(nameof(TextLineBoxPlaybackTests));
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            yield return null;
            VelvetStyleUtilities.AttachTo(document.rootVisualElement);

            // Act
            _mounted = V.Mount(document.rootVisualElement, V.Label(name: "text", className: "whitespace-pre text-[16px]", text: "a\nb\nc"));
            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;
            }

            // Assert
            Assert.That(RenderedTextProbe.Of(document.rootVisualElement.Q<Label>("text")), Does.Contain("<line-height=1.5em>"));
        }

        // GREEN_ON_BASE(characterization): on the base an upper-left label draws at the top of any box. Here it is
        // aligned middle-left, and a stretched box that left it unraised would centre it 24px lower.
        [UnityTest]
        public IEnumerator Given_ALineStretchedByAFlexRowUnderTheDefaultAlignment_When_Rendered_Then_TheGlyphsStayInTheFirstLineBox()
        {
            // Arrange & Act
            var difference = new float[1];
            var row = V.Div(className: "flex flex-row h-[80px]", children: new VNode[] { Text("text-base leading-8") });
            yield return Difference(row, Text("text-base leading-8"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(0f).Within(1f));
        }
    
        private static VNode TextButton(string className)
            => V.Button(name: "text", className: "w-[150px] border-0 bg-white text-black " + className, text: "HxgH");

        [UnityTest]
        public IEnumerator Given_TwoButtonHeights_When_Rendered_Then_TheGlyphsMoveByHalfTheirDifference()
        {
            // Arrange & Act — a browser centres a button's content in the button.
            var difference = new float[1];
            yield return Difference(TextButton("text-base h-[80px]"), TextButton("text-base h-[40px]"), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(20f).Within(1f));
        }

        [UnityTest]
        public IEnumerator Given_ALabelWithNoTextEffectInATallBox_When_Rendered_Then_TheGlyphsStayInTheFirstLineBox()
        {
            // Arrange & Act — no class the text effect pass reads, so nothing but the line box places the glyphs.
            var difference = new float[1];
            yield return Difference(Text("h-[80px]"), Text(string.Empty), difference);

            // Assert
            Assert.That(difference[0], Is.EqualTo(0f).Within(1f));
        }
    }
}
#endif
