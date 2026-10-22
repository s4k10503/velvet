using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that each shader-backed paint puts its pixels on a runtime panel. Every case drives a
    /// <c>UIDocument</c>-backed panel — the one a player has — and reads the frame back off its render texture.
    /// <para>
    /// Every class used here is either an arbitrary value or a family Velvet realises in C#, so no stylesheet
    /// is attached and none is needed: the fixture measures the paint, never the cascade.
    /// </para>
    /// <para>
    /// Whether those shaders survive into a player is not what this measures;
    /// <c>BundledShaderPlayerInclusionTests</c> is the case that answers it.
    /// </para>
    /// </summary>
    [Timeout(600000)]
    internal sealed class ShaderBackedPaintRuntimeTests
    {
        private const int PanelSize = 100;

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

        // Mounts an opaque backdrop covering the whole frame with `child` painted over it, then advances real
        // frames so the panel repaints. The backdrop makes every count attributable: an unpainted region reads
        // as the backdrop rather than as whatever the render texture last held.
        private IEnumerator MountOverBackdrop(string name, VNode child)
        {
            _host = new RenderTexturePanelHost(name, PanelSize, PanelSize);
            _mounted = V.Mount(_host.Root, V.Div(
                className: $"w-[{PanelSize}px] h-[{PanelSize}px] bg-[#0000ff] p-[30px]",
                children: new[] { child }));
            return WaitRealtimeDraining(0.6, _host.TargetTexture);
        }

        private int CountPixels(Func<Color32, bool> predicate)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(
                _host.TargetTexture, new RectInt(0, 0, PanelSize, PanelSize));
            var count = 0;
            foreach (var p in pixels)
            {
                if (predicate(p))
                {
                    count++;
                }
            }
            return count;
        }

        // The blue backdrop. Counted beside a paint asserted as PRESENT, so that "the paint is missing" and
        // "the panel drew nothing at all" cannot report as the same failure.
        private int CountBackdrop() => CountPixels(p => p.b > 200 && p.r < 40 && p.g < 40);

        // Red-dominant rather than near-#ff0000: a blue backdrop cannot produce a red-dominant pixel by any
        // blend, and the loose band survives whatever anti-aliasing the runner's GPU applies.
        private int CountRedDominant() => CountPixels(p => p.r > 120 && p.b < 140);

        // saturate-[0] writes the fill's luminance into all three channels, and a luminance is a weighted
        // mean, so it lands strictly inside #c02020's own 0x20..0xc0 channel range — a bound derived from the
        // declared fill rather than from a measured pixel. The only other thing on the frame is the parent's
        // blue fill, which is not neutral, so this counts what the element painted through the filter and
        // nothing else — which the backdrop count cannot: that fill survives the element painting nothing.
        private int CountDesaturatedFill() => CountPixels(p => Mathf.Abs(p.r - p.g) < 12
            && Mathf.Abs(p.r - p.b) < 12 && p.r > 0x20 && p.r < 0xc0);

        // GREEN_ON_BASE(refactor): only the case and its panel are renamed for the box-shadow pipeline's rename.
        [UnityTest]
        public IEnumerator Given_ABoxShadowCaster_When_RenderedOnARuntimePanel_Then_TheShadowPaintsItsColour()
        {
            // Arrange — a caster the same colour as the backdrop, so the only thing that can put red on the
            // frame is the shadow. Offset rather than spread, so the halo lands clear of the caster's own box.
            yield return MountOverBackdrop("BoxShadowPanel", V.Div(
                className: "w-[40px] h-[40px] bg-[#0000ff] shadow-[10px_10px_0px_0px_#ff0000]"));

            // Act
            var backdrop = CountBackdrop();
            var shadow = CountRedDominant();

            // Assert
            Assert.That((backdrop > 0, shadow > 0), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base already shears a shadow on a caster skewed on y alone with its face.
        // Have FiberBoxShadowApplier pass `default` for the caster's skew and the upright shadow's level edge reddens it.
        [UnityTest]
        public IEnumerator Given_ABoxShadowCasterSkewedOnY_When_RenderedOnARuntimePanel_Then_TheShadowTiltsTheWayTheFaceDoes()
        {
            // Arrange — a green face over the blue backdrop with a red shadow 14px below it. A 20deg y shear drops
            // the bottom edge of both by tan(20deg) ≈ 0.36px a column, about 11px between the sampled columns, 5px in
            // from each side of the box; an upright shadow's bottom edge is level there.
            yield return MountOverBackdrop("SkewedBoxShadowPanel", V.Div(name: "caster",
                className: "w-[40px] h-[30px] bg-[#00ff00] skew-y-[20deg] shadow-[0px_14px_10px_0px_#ff0000]"));
            var box = CasterBox();
            var pixels = RenderTexturePixelReader.ReadPixels(
                _host.TargetTexture, new RectInt(0, 0, PanelSize, PanelSize));

            // Act
            var ratio = TiltRatio(pixels, (int)box.xMin + 5, (int)box.xMin + 35);

            // Assert
            Assert.That(ratio, Is.EqualTo(1f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator Given_ABoxShadowCasterSkewedOnBothAxesByOneClass_When_RenderedOnARuntimePanel_Then_TheShadowTiltsTheWayTheFaceDoes()
        {
            // Arrange — skew-[20deg] is skewX(20deg) skewY(20deg). The bottom edge of the face and of the shadow
            // falls by tan θ / (1 + tan² θ) ≈ 0.32px a column, about 9px between the sampled columns, 15px and 42px
            // right of the box's left side: the face's bottom edge spans 3px to 48px there, and the shadow's, which
            // its offset also carries 5px right, 8px to 53px. A shadow sheared by the x angle alone has a level
            // bottom edge from 11px to 51px, and an upright one ends at 40px, short of the second column.
            yield return MountOverBackdrop("BothAxesSkewedBoxShadowPanel", V.Div(name: "caster",
                className: "w-[40px] h-[30px] bg-[#00ff00] skew-[20deg] shadow-[0px_14px_10px_0px_#ff0000]"));
            var box = CasterBox();
            var pixels = RenderTexturePixelReader.ReadPixels(
                _host.TargetTexture, new RectInt(0, 0, PanelSize, PanelSize));

            // Act
            var ratio = TiltRatio(pixels, (int)box.xMin + 15, (int)box.xMin + 42);

            // Assert
            Assert.That(ratio, Is.EqualTo(1f).Within(0.5f));
        }

        private Rect CasterBox()
            => UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(_host.Root, "caster").worldBound;

        // How far the shadow's bottom edge drops between the two columns, over how far the face's does; NaN where the
        // face's does not drop, so a face that was never sheared cannot read as a shadow that matched it. The
        // shadow's bottom edge lies below the face, so the face covers neither reading of it.
        private static float TiltRatio(Color32[] pixels, int left, int right)
        {
            Func<Color32, bool> face = p => p.g > 200 && p.r < 60 && p.b < 60;
            Func<Color32, bool> shadow = p => p.r > 120 && p.g < 100 && p.b < 140;
            var faceTilt = BottomRow(pixels, right, face) - BottomRow(pixels, left, face);
            var shadowTilt = BottomRow(pixels, right, shadow) - BottomRow(pixels, left, shadow);
            return faceTilt > 0 ? (float)shadowTilt / faceTilt : float.NaN;
        }

        // The lowest row on the panel, counted down from its top, at which column x holds a pixel the predicate
        // takes; -1 for none. The readback runs bottom-up, as the inset case's indexing reads it.
        private static int BottomRow(Color32[] pixels, int x, Func<Color32, bool> predicate)
        {
            var bottom = -1;
            for (var row = 0; row < PanelSize; row++)
            {
                if (predicate(pixels[((PanelSize - 1 - row) * PanelSize) + x]))
                {
                    bottom = row;
                }
            }
            return bottom;
        }

        [UnityTest]
        public IEnumerator Given_AnInsetShadowOnAnElementUnderEightPixelsTall_When_RenderedOnARuntimePanel_Then_NothingIsPaintedBelowIt()
        {
            // Arrange — a 4px caster is under the 8px floor of a bake texture, and an inset layer with a 2px
            // spread covers its whole padding box in red. The rows are derived from the caster's measured box.
            yield return MountOverBackdrop("ThinInsetShadowPanel", V.Div(
                name: "caster", className: "w-[40px] h-[4px] shadow-[inset_0px_0px_0px_2px_#ff0000]"));
            var box = UnityEngine.UIElements.UQueryExtensions
                .Q<UnityEngine.UIElements.VisualElement>(_host.Root, "caster").worldBound;
            var pixels = RenderTexturePixelReader.ReadPixels(
                _host.TargetTexture, new RectInt(0, 0, PanelSize, PanelSize));

            // Act
            var inside = 0;
            var below = 0;
            for (var row = 0; row < PanelSize; row++)
            {
                for (var col = 0; col < PanelSize; col++)
                {
                    var p = pixels[((PanelSize - 1 - row) * PanelSize) + col];
                    if (p.r > 120 && p.b < 140)
                    {
                        inside += row >= box.yMin && row < box.yMax ? 1 : 0;
                        below += row >= box.yMax + 1f ? 1 : 0;
                    }
                }
            }

            // Assert — the red inside the caster gates the count below it, so a frame with no shadow at all
            // cannot read as one that stayed inside.
            Assert.That(inside > 0 ? below : -1, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Given_ASkewedGradientElement_When_RenderedOnARuntimePanel_Then_TheSilhouettePaintsIt()
        {
            // Arrange — a skewed element carrying a gradient paints ONLY through the baked silhouette: the
            // solid-fill branch is skipped whenever a gradient is present, so an unavailable shader leaves the
            // element entirely invisible rather than flat-filled.
            yield return MountOverBackdrop("SkewedGradientPanel", V.Div(
                className: "w-[40px] h-[40px] skew-x-12 bg-gradient-to-r from-[#ff0000] to-[#00ff00]"));

            // Act
            var backdrop = CountBackdrop();
            var gradient = CountRedDominant();

            // Assert
            Assert.That((backdrop > 0, gradient > 0), Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ABrightenedFill_When_RenderedOnARuntimePanel_Then_ThePixelExceedsTheDeclaredFill()
        {
            // Arrange — brightness-[2] over a mid grey. Only the custom-filter pass can lift the fill above its
            // declared 0x40, and the blue backdrop cannot blend into a neutral grey at all.
            yield return MountOverBackdrop("BrightnessPanel", V.Div(
                className: "w-[40px] h-[40px] bg-[#404040] brightness-[2]"));

            // Act
            var backdrop = CountBackdrop();
            var brightened = CountPixels(p => p.r > 0x60 && Mathf.Abs(p.r - p.g) < 16 && Mathf.Abs(p.r - p.b) < 16);

            // Assert
            Assert.That((backdrop > 0, brightened > 0), Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ADesaturatedFill_When_RenderedOnARuntimePanel_Then_TheFillsOwnColourIsGone()
        {
            // Arrange — saturate-[0] collapses a saturated red to its luminance grey. Asserted as the ABSENCE
            // of the declared fill, which is what a filter that never ran leaves on the frame.
            yield return MountOverBackdrop("SaturatePanel", V.Div(
                className: "w-[40px] h-[40px] bg-[#c02020] saturate-[0]"));

            // Act
            var desaturated = CountDesaturatedFill();
            var unfiltered = CountPixels(p => Mathf.Abs(p.r - 0xc0) < 12 && Mathf.Abs(p.g - 0x20) < 12
                && Mathf.Abs(p.b - 0x20) < 12);

            // Assert
            Assert.That((desaturated > 0, unfiltered == 0), Is.EqualTo((true, true)));
        }
    }
}
