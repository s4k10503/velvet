using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins, by real GPU pixel readback, that <c>slice-[N]</c> nine-slices the background image: a corner keeps
    /// its size when the element grows, where a stretched image's corner grows with it.
    /// </summary>
    /// <remarks>
    /// The image is 30×30 texels with a red 10×10 square in each corner and blue elsewhere, point-filtered so a
    /// texel boundary lands on a pixel boundary. Four boxes share one frame: a sliced and an unsliced box at
    /// each of two sizes. The unsliced pair is the control: its corner grows with the box, which is both what
    /// says the image painted at all and how far a corner that failed to keep its size would move. The
    /// stylesheet is attached for <c>absolute</c> and <c>object-fill</c>; every other class is inline.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class NineSlicePlaybackTests
    {
        private const int Width = 400;
        private const int Height = 240;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private Texture2D _image;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            if (_image != null) Object.Destroy(_image);
            _image = null;
            yield return null;
        }

        private Texture2D CornerImage()
        {
            _image = new Texture2D(30, 30, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[30 * 30];
            for (var y = 0; y < 30; y++)
            {
                for (var x = 0; x < 30; x++)
                {
                    var corner = (x < 10 || x >= 20) && (y < 10 || y >= 20);
                    pixels[(y * 30) + x] = corner ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
                }
            }
            _image.SetPixels32(pixels);
            _image.Apply();
            return _image;
        }

        private VNode Box(string name, string geometry, string slice, Texture2D image)
            => V.Div(name: name, className: $"absolute object-fill {geometry} {slice}",
                styles: new StyleOverrides { BackgroundImage = new StyleBackground(image) });

        // The red run along the box's top-left corner, measured from its left edge two rows below its top edge,
        // and down its left edge two columns in from it.
        private static (int Across, int Down) CornerRun(Color32[] pixels, Rect box)
        {
            var left = Mathf.RoundToInt(box.xMin);
            var top = Mathf.RoundToInt(box.yMin);
            var across = 0;
            while (across < box.width && RenderTexturePixelReader.IsRedPixel(At(pixels, left + across, top + 2)))
            {
                across++;
            }
            var down = 0;
            while (down < box.height && RenderTexturePixelReader.IsRedPixel(At(pixels, left + 2, top + down)))
            {
                down++;
            }
            return (across, down);
        }

        // Readback rows run bottom-up while worldBound runs top-down, so the row index is mirrored.
        private static Color32 At(Color32[] pixels, int col, int row)
            => pixels[((Height - 1 - row) * Width) + col];

        [UnityTest]
        public IEnumerator Given_ASlicedBackground_When_TheBoxIsLarger_Then_ItsCornerKeepsItsSize()
        {
            // Arrange
            var image = CornerImage();
            _host = new RenderTexturePanelHost("NineSlice", Width, Height);
            VelvetStyleUtilities.AttachTo(_host.Root);
            const string small = "left-[10px] top-[10px] w-[90px] h-[60px]";
            const string large = "left-[10px] top-[90px] w-[150px] h-[120px]";
            const string plainSmall = "left-[200px] top-[10px] w-[90px] h-[60px]";
            const string plainLarge = "left-[200px] top-[90px] w-[150px] h-[120px]";

            // Act
            _mounted = V.Mount(_host.Root, V.Div(name: "frame", className: "w-[400px] h-[240px]", children: new[]
            {
                Box("slicedSmall", small, "slice-[10]", image),
                Box("slicedLarge", large, "slice-[10]", image),
                Box("plainSmall", plainSmall, string.Empty, image),
                Box("plainLarge", plainLarge, string.Empty, image),
            }));
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height));
            Rect Bound(string name) => _host.Root.Q<VisualElement>(name).worldBound;
            var slicedSmall = CornerRun(pixels, Bound("slicedSmall"));
            var slicedLarge = CornerRun(pixels, Bound("slicedLarge"));
            var plainSmallRun = CornerRun(pixels, Bound("plainSmall"));
            var plainLargeRun = CornerRun(pixels, Bound("plainLarge"));

            // Assert — the boxes are laid out at their declared sizes first, since every reading is taken from
            // their bounds. A corner that keeps its size moves by nothing as the box grows; one that is stretched
            // moves by what the unsliced corner moves, so half of that separates the two. The sliced corner is
            // also required to have painted at all.
            var geometry = Bound("slicedSmall").size == new Vector2(90f, 60f)
                && Bound("slicedLarge").size == new Vector2(150f, 120f)
                && Bound("plainSmall").size == new Vector2(90f, 60f)
                && Bound("plainLarge").size == new Vector2(150f, 120f);
            var acrossKept = 2 * Mathf.Abs(slicedLarge.Across - slicedSmall.Across)
                < Mathf.Abs(plainLargeRun.Across - plainSmallRun.Across);
            var downKept = 2 * Mathf.Abs(slicedLarge.Down - slicedSmall.Down)
                < Mathf.Abs(plainLargeRun.Down - plainSmallRun.Down);
            Assert.That((geometry, slicedSmall.Across > 0, acrossKept, downKept),
                Is.EqualTo((true, true, true, true)),
                $"sliced {slicedSmall} -> {slicedLarge}, unsliced {plainSmallRun} -> {plainLargeRun}");
        }
    }
}
