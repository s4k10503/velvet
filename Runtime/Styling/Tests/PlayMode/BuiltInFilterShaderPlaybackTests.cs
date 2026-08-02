using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the shader-backed brightness-* / saturate-* utilities actually MOVE pixels on a real
    /// runtime panel — not just that they compose the right struct. Real GPU pixel readback (as the
    /// SceneView / Particles playback specs do), so it verifies the custom-filter shaders run.
    /// </summary>
    /// <remarks>
    /// Custom-filter passes need a real GPU to execute, so these are interactive/GPU-run verification (the
    /// same posture as the other playback specs); a GPU-less runner's first drawing frames also stall while
    /// the GL stack warms, so the budget is raised. Nothing about the verified behavior depends on the
    /// budget.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class BuiltInFilterShaderPlaybackTests
    {
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
            DisposePanel();
            yield return null;
        }

        private void DisposePanel()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // Mounts a solid fill carrying the given filter token and advances real frames so the panel repaints.
        private IEnumerator MountFilteredFill(string filterToken, string fillColor = "#404040")
        {
            _host = new RenderTexturePanelHost("FilterPanel", 100, 100);
            _mounted = V.Mount(_host.Root, V.Div(className: $"w-[100px] h-[100px] bg-[{fillColor}] {filterToken}"));
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);
        }

        // The gap between a pixel's widest and narrowest colour channel — how far its colour sits from gray.
        private static int ChannelSpread(Color32 c)
            => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));

        // Averages a block at the panel's center (the solid fill), smoothing readback/AA noise.
        private Color32 SampleCenterAverage(int size)
        {
            var half = size / 2;
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(50 - half, 50 - half, size, size));
            long r = 0, g = 0, b = 0;
            foreach (var p in pixels)
            {
                r += p.r;
                g += p.g;
                b += p.b;
            }
            var n = Mathf.Max(pixels.Length, 1);
            return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        [UnityTest]
        public IEnumerator Given_BrightnessAboveOne_When_Rendered_Then_ThePixelIsBrighterThanIdentityBrightness()
        {
            // Arrange — identity brightness renders the base gray. Detecting a strict increase at brightness-[2]
            // requires BOTH the parser to accept N>1 without clamping AND the shader to multiply the factor
            // unclamped — either alone still renders identity gray, so this pixel and the brightened one below
            // would read identical.
            yield return MountFilteredFill("brightness-[1]");
            var identity = SampleCenterAverage(20);
            Assume.That(identity.r, Is.GreaterThan(10), "Precondition: the base fill rendered a non-black pixel");
            DisposePanel();

            // Act — the same fill at 2x brightness.
            yield return MountFilteredFill("brightness-[2]");
            var brightened = SampleCenterAverage(20);

            // Assert
            Assert.That(brightened.r, Is.GreaterThan(identity.r));
        }

        [UnityTest]
        public IEnumerator Given_SaturateZero_When_Rendered_Then_ThePixelDesaturatesToGray()
        {
            // Arrange & Act — a saturated red fill fully desaturated collapses its channels toward a common
            // luminance gray. A deterministic endpoint that sanity-checks the shader's luma lerp.
            _host = new RenderTexturePanelHost("FilterPanel", 100, 100);
            _mounted = V.Mount(_host.Root, V.Div(className: "w-[100px] h-[100px] bg-[#c02020] saturate-[0]"));
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);
            var c = SampleCenterAverage(20);
            Assume.That((int)c.r + c.g + c.b, Is.GreaterThan(0), "Precondition: the fill rendered a non-black pixel");

            // Assert — the channels sit within a tight band of one another.
            Assert.That(ChannelSpread(c), Is.LessThan(12));
        }

        [UnityTest]
        public IEnumerator Given_SaturateAboveOne_When_Rendered_Then_ThePixelChannelsSpreadWiderThanIdentity()
        {
            // Arrange — a partially saturated fill at identity saturation keeps its native channel spread.
            // Over-saturation pushes the channels APART from their common luminance, which requires BOTH the
            // parser to accept N>1 and the shader's luma lerp to run unclamped past full desaturation — a lerp
            // that only pulls channels together toward gray could never widen the spread this way.
            yield return MountFilteredFill("saturate-[1]", "#a06060");
            var identity = SampleCenterAverage(20);
            var identitySpread = ChannelSpread(identity);
            Assume.That(identitySpread, Is.GreaterThan(0), "Precondition: the base fill rendered a non-gray pixel");
            DisposePanel();

            // Act — the same fill over-saturated.
            yield return MountFilteredFill("saturate-[2]", "#a06060");
            var oversaturated = SampleCenterAverage(20);

            // Assert
            Assert.That(ChannelSpread(oversaturated), Is.GreaterThan(identitySpread));
        }
    }
}
