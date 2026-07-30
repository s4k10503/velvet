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
    /// Pins, by real GPU pixel readback, that UI Toolkit scales a textured <c>mgc.Allocate</c> quad by the
    /// resolved opacity byte-for-byte the same as it scales <c>painter2D</c> output — in each context where
    /// the two could plausibly have diverged.
    /// </summary>
    /// <remarks>
    /// Velvet's drop shadow is a textured quad drawn beside a painter2D face in one
    /// <c>generateVisualContent</c>. A scheduler that scaled the shadow itself while an animation faded its
    /// caster would apply the same opacity twice; one did, on the premise that the quad ignores opacity.
    /// <c>ShadowFadeOpacityPlaybackTests</c> pins the consequence on the real paint. These cases pin the engine
    /// fact underneath it in the contexts that premise could have been about, so restoring such a multiplier
    /// means refuting a test rather than re-deriving a measurement.
    /// <para>
    /// Each case reads the two marks from ONE frame, so nothing about panel setup or colour space can move one
    /// without moving the other. The filter case carries its own control: red desaturates to gray only if the
    /// filter actually ran, and an achromatic probe would have read identical whether it ran or not.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class PaintOpacityParityPlaybackTests
    {
        private const int Size = 240;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private Texture2D _white;
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
            if (_white != null) Object.Destroy(_white);
            _white = null;
            yield return null;
        }

        // A flat white texture, so the quad's colour is its vertex tint alone and matches the painter2D fill
        // colour exactly at full opacity.
        private Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (var i = 0; i < 16; i++) pixels[i] = new Color32(255, 255, 255, 255);
                _white.SetPixels32(pixels);
                _white.Apply();
            }
            return _white;
        }

        private Color32 Average(RectInt panelRect)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            long r = 0, g = 0, b = 0;
            var n = 0;
            for (var y = panelRect.yMin; y < panelRect.yMax; y++)
            {
                for (var x = panelRect.xMin; x < panelRect.xMax; x++)
                {
                    var p = pixels[((Size - 1 - y) * Size) + x];
                    r += p.r; g += p.g; b += p.b; n++;
                }
            }
            return n == 0 ? default : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
        }

        // Left half: a textured quad. Right half: a painter2D fill. Same tint, same callback, same frame.
        private void PaintQuadAndFill(VisualElement element, Color tint)
        {
            var texture = White();
            element.generateVisualContent += mgc =>
            {
                var mesh = mgc.Allocate(4, 6, texture);
                mesh.SetNextVertex(new Vertex { position = new Vector3(0f, 0f, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 1f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(90f, 0f, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 1f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(90f, 60f, Vertex.nearZ), tint = tint, uv = new Vector2(1f, 0f) });
                mesh.SetNextVertex(new Vertex { position = new Vector3(0f, 60f, Vertex.nearZ), tint = tint, uv = new Vector2(0f, 0f) });
                mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
                mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);

                var painter = mgc.painter2D;
                painter.BeginPath();
                painter.MoveTo(new Vector2(110f, 0f));
                painter.LineTo(new Vector2(200f, 0f));
                painter.LineTo(new Vector2(200f, 60f));
                painter.LineTo(new Vector2(110f, 60f));
                painter.ClosePath();
                painter.fillColor = tint;
                painter.Fill();
            };
        }

        private static readonly RectInt QuadSample = new(35, 35, 30, 30);
        private static readonly RectInt FillSample = new(155, 35, 30, 30);

        private const string ProbeClasses = "w-[200px] h-[60px] mt-[20px] ml-[20px]";

        // Counts saturated green, which only the wrapper's overflow control child paints.
        private int GreenPixels()
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var n = 0;
            foreach (var p in pixels)
            {
                if (p.g > 140 && p.r < 90 && p.b < 90) n++;
            }
            return n;
        }

        // Sticks out BELOW the 160px-tall wrapper, inside the 240px panel, so it renders when the wrapper does
        // not clip and vanishes when it does. It never touches either mark's sample rect.
        private static void AddOverflowControl(VisualElement wrapper)
        {
            wrapper.Add(new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, left = 20, top = 170, width = 40, height = 40,
                    backgroundColor = new Color(0f, 1f, 0f, 1f),
                },
            });
        }

        // wrapperOpacity / probeOpacity are set inline so neither depends on the stylesheet resolving an
        // opacity utility; clipWrapper is what makes the wrapper a clipped opacity group.
        private IEnumerator MountProbe(string name, float wrapperOpacity, bool clipWrapper, string probeExtra,
            float probeOpacity, Color tint)
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            _host.Root.LoadBundledStyleUtilitiesForTest();
            _mounted = V.Mount(_host.Root, V.Div(name: "wrap", className: "w-[240px] h-[160px]",
                children: new[] { V.Div(name: "probe", className: ProbeClasses + probeExtra) }));
            var wrap = _host.Root.Q<VisualElement>("wrap");
            var probe = _host.Root.Q<VisualElement>("probe");
            wrap.style.opacity = wrapperOpacity;
            if (clipWrapper) wrap.style.overflow = Overflow.Hidden;
            probe.style.opacity = probeOpacity;
            AddOverflowControl(wrap);
            PaintQuadAndFill(probe, tint);
            probe.MarkDirtyRepaint();
            yield return WaitRealtime(0.9);
        }

        [UnityTest]
        public IEnumerator Given_AnAncestorAtHalfOpacity_When_ItsDescendantPaintsBothWays_Then_TheQuadAndTheFillMatch()
        {
            // Arrange — full opacity first, as the reference the halved reading is judged against.
            yield return MountProbe("AncestorOpaque", 1f, false, "", 1f, Color.white);
            var opaqueQuad = Average(QuadSample);

            // Act
            yield return MountProbe("AncestorHalf", 0.5f, false, "", 1f, Color.white);
            var quad = Average(QuadSample);
            var fill = Average(FillSample);

            // Assert — the ancestor's opacity reached the frame at all (so an inert wrapper would fail rather
            // than read as parity), and it reached both marks identically.
            Assert.That((quad.r < opaqueQuad.r, fill.r == quad.r), Is.EqualTo((true, true)),
                $"opaque={opaqueQuad.r} quad={quad.r} fill={fill.r}");
        }

        [UnityTest]
        public IEnumerator Given_AClippedOpacityGroup_When_ItsDescendantPaintsBothWays_Then_TheQuadAndTheFillMatch()
        {
            // Arrange — an ancestor that both clips and fades is the shape a group-opacity render target would
            // take, and the one context where a textured quad could have been composited on its own path. The
            // same wrapper WITHOUT its clip establishes that the control child renders at all, so the clipped
            // reading below distinguishes a clip that took from a control that was never visible.
            // Both control readings are taken at FULL opacity, differing only in the clip: read at 0.5 the
            // control would fall under the saturated-green threshold on its own, and an absent clip would
            // count zero for the wrong reason.
            yield return MountProbe("ClipGroupUnclipped", 1f, false, "", 1f, Color.white);
            var controlUnclipped = GreenPixels();
            yield return MountProbe("ClipGroupOpaque", 1f, true, "", 1f, Color.white);
            var controlClipped = GreenPixels();
            var opaqueQuad = Average(QuadSample);

            // Act
            yield return MountProbe("ClipGroupHalf", 0.5f, true, "", 1f, Color.white);
            var quad = Average(QuadSample);
            var fill = Average(FillSample);

            // Assert — the first two terms are what make this case about a CLIPPED group rather than a plain
            // faded one: without them the wrapper's overflow could be dropped and every remaining term would
            // still hold, leaving the context pinned in name only. Then the opacity reached the frame, and it
            // reached both marks identically.
            Assert.That(
                (controlUnclipped > 0, controlClipped == 0, quad.r < opaqueQuad.r, fill.r == quad.r),
                Is.EqualTo((true, true, true, true)),
                $"control unclipped={controlUnclipped} clipped={controlClipped}; "
                + $"opaque={opaqueQuad.r} quad={quad.r} fill={fill.r}");
        }

        [UnityTest]
        public IEnumerator Given_AFilteredElement_When_ItPaintsBothWays_Then_TheQuadAndTheFillMatch()
        {
            // Arrange / Act — RED marks under grayscale, at half self-opacity. Red is what makes the filter
            // observable: it desaturates to gray, so the green channel rising off zero is proof the filter ran.
            // A white probe would read identical filtered or not, and the case would pin nothing.
            yield return MountProbe("Grayscale", 1f, false, " grayscale-[1]", 0.5f, Color.red);
            var quad = Average(QuadSample);
            var fill = Average(FillSample);

            // Assert — the filter ran (a red mark reads a zero green channel until it does), the quad came
            // through it desaturated, and the fill matches the quad on both channels.
            Assert.That((quad.g > 0, quad.r == quad.g, fill.r == quad.r, fill.g == quad.g),
                Is.EqualTo((true, true, true, true)),
                $"quad=({quad.r},{quad.g},{quad.b}) fill=({fill.r},{fill.g},{fill.b})");
        }
    }
}
