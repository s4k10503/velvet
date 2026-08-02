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
    /// Reads back, by GPU pixel readback, what two nested elements carrying one background utility paint.
    /// </summary>
    /// <remarks>
    /// Measured in pixels because compositing happens at paint time: a translucent token that doubles up on
    /// a nested element leaves both elements' <c>resolvedStyle.backgroundColor</c> identical, so every
    /// reading short of the frame agrees while the two boxes are visibly different colours.
    /// <para>
    /// The bare backdrop is sampled beside them: without it, a run where the bundled sheet resolved nothing
    /// reads as two identical regions and passes.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class SemanticTokenIdempotencePlaybackTests
    {
        private const int Width = 120;
        private const int Height = 60;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;
        private bool _darkBefore;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            _darkBefore = VelvetTheme.IsDark;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ASurfaceInsideASurface_When_TheLightSetPaintsThem_Then_BothLandOneColour()
        {
            // Arrange
            VelvetTheme.IsDark = false;

            // Act
            yield return MountNestedSurfaces("IdempotenceLight");
            var reading = Read();

            // Assert
            Assert.That(
                (reading.Inner, reading.Painted),
                Is.EqualTo((reading.OuterOnly, true)),
                reading.Diagnostic);
        }

        [UnityTest]
        public IEnumerator Given_ASurfaceInsideASurface_When_TheDarkSetPaintsThem_Then_BothLandOneColour()
        {
            // Arrange
            VelvetTheme.IsDark = true;

            // Act
            yield return MountNestedSurfaces("IdempotenceDark");
            var reading = Read();

            // Assert
            Assert.That(
                (reading.Inner, reading.Painted),
                Is.EqualTo((reading.OuterOnly, true)),
                reading.Diagnostic);
        }

        // The outer box leaves a strip of panel bare to its right, and the inner one covers the left half of
        // the outer, so one frame holds all three regions: nested surface, single surface, no surface.
        private IEnumerator MountNestedSurfaces(string name)
        {
            _host = new RenderTexturePanelHost(name, Width, Height);
            _host.Root.style.backgroundColor = Color.black;
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(
                _host.Root,
                V.Div(
                    name: "outer",
                    className: "bg-surface w-[80px] h-[60px]",
                    children: new VNode[]
                    {
                        V.Div(name: "inner", className: "bg-surface w-[40px] h-[60px]"),
                    }));
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        private Reading Read()
        {
            var outer = _host.Root.Q<VisualElement>("outer").worldBound;
            var inner = _host.Root.Q<VisualElement>("inner").worldBound;
            Assume.That(
                (inner.width > 1f, outer.xMax - inner.xMax > 1f, Width - outer.xMax > 1f),
                Is.EqualTo((true, true, true)),
                $"Precondition: the three sample regions are laid out and distinct. outer={outer} inner={inner}");

            // One readback for all three samples: two reads could straddle a repaint and compare frames.
            var frame = RenderTexturePixelReader.ReadPixels(
                _host.TargetTexture, new RectInt(0, 0, Width, Height));
            var y = Mathf.RoundToInt(inner.center.y);
            var innerPixel = PixelAt(frame, Mathf.RoundToInt(inner.center.x), y);
            var outerOnlyPixel = PixelAt(frame, Mathf.RoundToInt((inner.xMax + outer.xMax) * 0.5f), y);
            var backdropPixel = PixelAt(frame, Mathf.RoundToInt((outer.xMax + Width) * 0.5f), y);

            return new Reading
            {
                Inner = Key(innerPixel),
                OuterOnly = Key(outerOnlyPixel),
                Painted = Key(outerOnlyPixel) != Key(backdropPixel),
                Diagnostic = $"inner={Key(innerPixel)} outerOnly={Key(outerOnlyPixel)} " +
                    $"backdrop={Key(backdropPixel)} outer={outer} innerBound={inner}",
            };
        }

        // Row index flipped the same way WrapperLessPaintOverflowClipPlaybackTests flips it.
        private static Color32 PixelAt(Color32[] frame, int x, int y) =>
            frame[((Height - 1 - Mathf.Clamp(y, 0, Height - 1)) * Width) + Mathf.Clamp(x, 0, Width - 1)];

        private static string Key(Color32 pixel) => $"{pixel.r},{pixel.g},{pixel.b}";

        private struct Reading
        {
            public string Inner;
            public string OuterOnly;
            public bool Painted;
            public string Diagnostic;
        }
    }
}
