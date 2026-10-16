using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// What a <c>drop-shadow-*</c> utility paints, read back from a real runtime panel: that it follows what the
    /// element draws, how its colour blends, how far its blur reaches, and that the element stays drawn over it.
    /// </summary>
    /// <remarks>
    /// The panel carries no stylesheet, so every class here is an arbitrary value written inline. The shadow
    /// sits on a 20px caster that paints nothing itself, 20px in from the panel's top-left corner on a white
    /// backdrop, and its blue ink child overflows it 30px down and right. Samples are taken from measured
    /// bounds: past the ink, the shadow is the filter's alone, and the caster's own box shadows nothing.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class DropShadowFilterPlaybackTests
    {
        private const int Width = 160;
        private const int Height = 140;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
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
            yield return null;
        }

        private IEnumerator Mount(string shadow, int ink = 20, VNode? beside = null)
        {
            _host = new RenderTexturePanelHost("DropShadowPanel", Width, Height);
            _host.Root.style.backgroundColor = Color.white;
            _mounted = V.Mount(_host.Root, V.Div(
                className: $"w-[{Width}px] h-[{Height}px] p-[20px]",
                children: new[]
                {
                    V.Div(name: "caster", className: $"w-[20px] h-[20px] {shadow}",
                        children: new VNode?[] { V.Div(name: "ink", className: $"ml-[30px] mt-[30px] w-[{ink}px] h-[{ink}px] shrink-[0] bg-[#0000ff]") }),
                    beside,
                }));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        private Rect Bounds(string name) => _host.Root.Q<VisualElement>(name).worldBound;

        private Color32[] Frame() => RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height));

        // Row index flipped the same way WrapperLessPaintOverflowClipPlaybackTests flips it.
        private static Color32 PixelAt(Color32[] frame, float x, float y)
        {
            var column = Mathf.Clamp(Mathf.FloorToInt(x), 0, Width - 1);
            var row = Mathf.Clamp(Mathf.FloorToInt(y), 0, Height - 1);
            return frame[((Height - 1 - row) * Width) + column];
        }

        private static string Classify(Color32 pixel)
        {
            if (pixel.r > 200 && pixel.g > 200 && pixel.b > 200) return "white";
            if (pixel.b > 200 && pixel.r < 60 && pixel.g < 60) return "blue";
            if (pixel.r > 200 && pixel.g < 60 && pixel.b < 60) return "red";
            return $"{pixel.r},{pixel.g},{pixel.b}";
        }

        [UnityTest]
        public IEnumerator Given_AHardOffsetShadow_When_Rendered_Then_ItFollowsTheInkBelowRightAndTheInkStaysOverIt()
        {
            // Arrange — the shadow is the ink shifted 10px right and down, so the ink's centre lies inside it, the
            // corner past the ink's bottom-right holds the shadow alone, and the caster's own box shadows nothing.
            yield return Mount("drop-shadow-[10px_10px_0_#ff0000]");
            var ink = Bounds("ink");
            var caster = Bounds("caster");

            // Act
            var frame = Frame();
            var reading = (Classify(PixelAt(frame, ink.center.x, ink.center.y)),
                Classify(PixelAt(frame, ink.xMax + 5f, ink.yMax + 5f)),
                Classify(PixelAt(frame, caster.xMax + 5f, caster.yMax + 5f)));

            // Assert
            Assert.That(reading, Is.EqualTo(("blue", "red", "white")), $"ink={ink} caster={caster}");
        }

        [UnityTest]
        public IEnumerator Given_AHalfTransparentShadow_When_Rendered_Then_ItBlendsLikeThatColourPaintedAsABackground()
        {
            // Arrange — the reference follows the caster in the column, clear of the ink and its shadow. No channel
            // of the colour is at 0 or 1, where over this white backdrop a straight and a premultiplied colour land
            // alike.
            yield return Mount("drop-shadow-[10px_10px_0_rgba(160,96,32,0.5)]",
                beside: V.Div(name: "reference", className: "ml-[100px] w-[20px] h-[20px] bg-[rgba(160,96,32,0.5)]"));
            var ink = Bounds("ink");
            var reference = Bounds("reference");

            // Act
            var frame = Frame();
            var shadow = PixelAt(frame, ink.xMax + 5f, ink.yMax + 5f);
            var painted = PixelAt(frame, reference.center.x, reference.center.y);

            // Assert — a reference that painted nothing would match a shadow that painted nothing, so it fails instead.
            var expected = Classify(painted) == "white" ? new[] { -1, -1, -1 } : new int[] { painted.r, painted.g, painted.b };
            Assert.That(new int[] { shadow.r, shadow.g, shadow.b }, Is.EqualTo(expected).Within(3),
                $"ink={ink} reference={reference}");
        }

        [UnityTest]
        public IEnumerator Given_ABlurredShadow_When_Rendered_Then_ItsEdgesFollowAGaussianWhoseDeviationIsTheThirdLength()
        {
            // Arrange — a black shadow straight under a 40px ink, read outward from its right and bottom edges.
            const float deviation = 6f;
            yield return Mount($"drop-shadow-[0_0_{deviation}px_#000000]", ink: 40);
            var ink = Bounds("ink");
            var distances = new[] { 2f, 5f, 8f, 11f };

            // Act
            var frame = Frame();
            var right = distances.Select(d => (int)PixelAt(frame, ink.xMax + d, ink.center.y).r);
            var below = distances.Select(d => (int)PixelAt(frame, ink.center.x, ink.yMax + d).r);

            // Assert — the pixel centre sits half a pixel past each distance, and the ink's extent along the edge
            // leaves the other axis's factor at erf(half that extent / (σ√2)).
            var along = Erf(ink.width / 2f / (deviation * Mathf.Sqrt(2f)));
            var expected = distances.Select(d => (int)Math.Round(255.0 * (1.0 - along * Tail((d + 0.5) / deviation)))).ToArray();
            Assert.That(right.Concat(below).ToArray(), Is.EqualTo(expected.Concat(expected).ToArray()).Within(6),
                $"ink={ink}");
        }

        [UnityTest]
        public IEnumerator Given_AnOverflowHiddenCaster_When_Rendered_Then_ItsOwnShadowStillLandsOutsideIt()
        {
            // Arrange — overflow clips what the caster's content draws, not the filter applied over it.
            _host = new RenderTexturePanelHost("DropShadowPanel", Width, Height);
            _host.Root.style.backgroundColor = Color.white;
            _mounted = V.Mount(_host.Root, V.Div(
                className: $"w-[{Width}px] h-[{Height}px] p-[20px]",
                children: new VNode?[]
                {
                    V.Div(name: "caster", className: "w-[40px] h-[40px] bg-[#0000ff] drop-shadow-[10px_10px_0_#ff0000]",
                        refCallback: element =>
                        {
                            element.style.overflow = Overflow.Hidden;
                            return () => { };
                        }),
                }));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            var caster = Bounds("caster");

            // Act
            var shadow = Classify(PixelAt(Frame(), caster.xMax + 5f, caster.yMax + 5f));

            // Assert
            Assert.That(shadow, Is.EqualTo("red"), $"caster={caster}");
        }

        // The standard normal's upper tail, 1 - Φ(z).
        private static double Tail(double z) => 0.5 * (1.0 - Erf(z / Math.Sqrt(2.0)));

        // Abramowitz and Stegun 7.1.26, within 1.5e-7.
        private static double Erf(double x)
        {
            var sign = Math.Sign(x);
            x = Math.Abs(x);
            var t = 1.0 / (1.0 + 0.3275911 * x);
            var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return sign * y;
        }
    }
}
