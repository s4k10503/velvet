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
    /// The runtime paint proof for <c>overline</c>: UI Toolkit's rich text has no overline tag, so the
    /// decoration is realised as a <c>generateVisualContent</c> PAINT (<see cref="TextOverlinePainter"/> /
    /// <see cref="TextOverlineBinding"/>) rather than a string rewrite. An EditMode fixture
    /// (<c>StyleTextEffectPanelTests</c>) already pins that the binding gets attached/detached at the right
    /// times; this instead proves something actually reaches the mesh, on a real ticking runtime panel, by
    /// reading back pixels (mirroring the SceneView / Particles / built-in-filter playback specs).
    /// </summary>
    /// <remarks>
    /// The test string ("moon") is deliberately all x-height glyphs — no ascenders, descenders, or dots — so
    /// its own ink never reaches the TOP band of the label's resolved line box regardless of the exact font
    /// metrics (see <see cref="CountRedPixelsInTopBand"/> for the margin reasoning). That band is also where
    /// <see cref="TextOverlinePainter"/> places its rule. Both the glyph ink and the rule share the SAME
    /// inline <c>text-[#ff0000]</c> color (the painter reads <c>resolvedStyle.color</c>), so the established
    /// <see cref="RenderTexturePixelReader.IsRedPixel"/> heuristic — built for exactly this saturated-red
    /// test-color convention — detects either one without a bespoke threshold.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class TextOverlinePlaybackTests
    {
        private const string GlyphOnlyText = "moon";
        private const string LabelClasses = "text-[48px] text-[#ff0000]";

        // A themeless RenderTexture panel supplies neither a font (an empty runtime theme measures every
        // label 0 tall) nor wrap behavior, so both are supplied inline through the ref — the one channel
        // that reaches a bare panel (mirrors LeadingLineHeightPlaybackTests' identical incantation).
        private static readonly System.Func<VisualElement, System.Action> s_enableTextMeasurement = el =>
        {
            el.style.whiteSpace = WhiteSpace.Normal;
            el.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(
                UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf")));
            return null;
        };

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

        // Mounts a single Label, with or without `overline`, and lets a real panel tick layout + a first
        // paint. A generous realtime wait (not a fixed frame count) absorbs a first-run text-shaping /
        // glyph-atlas warm-up, the same posture BuiltInFilterShaderPlaybackTests takes for its own
        // first-draw GPU warm-up.
        private IEnumerator MountLabel(bool overline)
        {
            _host = new RenderTexturePanelHost("OverlinePanel", 260, 140);
            var classNames = overline ? "overline " + LabelClasses : LabelClasses;
            _mounted = V.Mount(_host.Root, V.Label(
                name: "lbl", className: classNames, text: GlyphOnlyText, refCallback: s_enableTextMeasurement));
            yield return WaitRealtime(0.5);
        }

        // Counts red pixels in the TOP band (the top 20% of the label's own resolved height) of the ONLY
        // label in the panel. That fraction is chosen with margin on both sides for typical sans-serif
        // metrics: TextOverlinePainter places its rule ~15% of the FONT size below the content box's top —
        // roughly 12% of a ~1.2x-font-size line box — comfortably inside a 20% band; "moon"'s x-height ink
        // starts only around 40% down from the same top (line box top -> baseline is ~0.95em for a typical
        // sans-serif, x-height is ~0.5em, leaving ~0.45em, i.e. ~37% of a 1.2em line box, of clear space
        // above it) — comfortably outside a 20% band. Either estimate would have to be off by roughly double
        // for the two bands to collide.
        private int CountRedPixelsInTopBand()
        {
            var label = _host.Root.Q<Label>("lbl");
            var textureWidth = _host.TargetTexture.width;
            var textureHeight = _host.TargetTexture.height;
            var bandHeight = Mathf.Max(4f, label.layout.height * 0.2f);
            var x = Mathf.Clamp(Mathf.RoundToInt(label.layout.x), 0, textureWidth);
            // Clamped to the texture bounds defensively — the panel is sized with generous margin around
            // the expected text width, but ReadPixels cannot tolerate a region reaching past the texture.
            var bandWidth = Mathf.Clamp(Mathf.RoundToInt(label.layout.width), 1, textureWidth - x);
            // ReadPixels is bottom-origin (see SceneViewPlaybackTests): the label sits at the panel's
            // top-left (the ONLY element mounted, under default non-absolute flow), so its top band (small
            // panel-Y) maps to the texture's TOP rows — the HIGHEST y-from-bottom values.
            var yFromBottom = Mathf.Clamp(Mathf.RoundToInt(textureHeight - (label.layout.y + bandHeight)), 0, textureHeight - 1);
            var bandHeightPx = Mathf.Clamp(Mathf.RoundToInt(bandHeight), 1, textureHeight - yFromBottom);
            var region = new RectInt(x, yFromBottom, bandWidth, bandHeightPx);
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, region);
            var count = 0;
            foreach (var p in pixels)
            {
                if (RenderTexturePixelReader.IsRedPixel(p)) count++;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator Given_OverlineLabel_When_Rendered_Then_TopBandShowsMoreRedThanTheNoOverlineSibling()
        {
            // Arrange & Act — baseline: identical text/font/color with no overline class at all.
            yield return MountLabel(overline: false);
            var baselineLabel = _host.Root.Q<Label>("lbl");
            Assume.That(baselineLabel != null && baselineLabel.layout.height > 0f, Is.True,
                "Precondition: the baseline label mounted and resolved a real layout height");
            var baseline = CountRedPixelsInTopBand();
            // A generous margin, not strict zero: the band is chosen to sit clear of "moon"'s x-height ink
            // (see the type remarks), but a strict-zero precondition would turn a stray anti-aliased pixel
            // into an Inconclusive rather than a clean pass, without making the final comparison below any
            // less meaningful either way.
            Assume.That(baseline, Is.LessThan(5),
                $"Precondition: the no-overline label's x-height-only glyphs stay clear of the top band (got {baseline} red pixels)");
            DisposePanel();

            // Act — the identical label with only `overline` added.
            yield return MountLabel(overline: true);

            // Assert
            Assert.That(CountRedPixelsInTopBand(), Is.GreaterThan(baseline));
        }
    }
}
