using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Parser coverage for <see cref="StyleRingClass"/>: the <c>ring-*</c> / <c>outline-*</c>
    /// utilities resolved into a <see cref="RingSpec"/>. Unlike the whole-spec last-wins shadow parser, a ring
    /// is COMPOSITE — width, color, offset and inset are independent slots — so <c>ring-2 ring-red-500</c>
    /// keeps both. <c>ring-0</c> / <c>outline-none</c> resolve to no ring. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleRingClassTests
    {
        private static RingSpec Extract(params string[] classNames)
        {
            Assume.That(StyleRingClass.TryExtract(classNames, out var spec), Is.True,
                "Precondition: the class list resolves to a ring");
            return spec;
        }

        [Test]
        public void Given_BareRing_When_Extracted_Then_UsesDefaultThreePixelWidth()
        {
            // The DEFAULT ring is 3px.
            Assert.That(Extract("ring").Width, Is.EqualTo(3f));
        }

        [Test]
        public void Given_RingPreset_When_Extracted_Then_ResolvesWidth()
        {
            Assert.That(Extract("ring-2").Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_RingArbitraryWidth_When_Extracted_Then_ResolvesPixels()
        {
            Assert.That(Extract("ring-[6px]").Width, Is.EqualTo(6f));
        }

        [Test]
        public void Given_RingColorOnly_When_Extracted_Then_KeepsDefaultWidth()
        {
            // A color-only ring still implies the DEFAULT ring width (3px).
            Assert.That(Extract("ring-red-500").Width, Is.EqualTo(3f));
        }

        [Test]
        public void Given_RingWidthAndColor_When_Extracted_Then_ColorSlotIsComposite()
        {
            // ring-2 sets width, ring-red-500 sets color — both apply (composite, not last-spec-wins).
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            Assert.That(Extract("ring-2", "ring-red-500").Color, Is.EqualTo(red));
        }

        [Test]
        public void Given_TwoRingWidths_When_Extracted_Then_TheLastOneWins()
        {
            // Within ONE slot the cascade is still last-wins; composite means the slots are independent of
            // each other, not that an earlier value in the same slot survives.
            Assert.That(Extract("ring-2", "ring-4").Width, Is.EqualTo(4f));
        }

        [Test]
        public void Given_AVariantColorOverAWidthAndColorBase_When_Extracted_Then_OnlyTheColorSlotMoves()
        {
            // A variant contributes its classes after the base ones, so it routinely names ONE slot while the
            // base named others. Resolving the ring whole-spec — the shadow parser's rule — would let a
            // variant that mentions only a colour reset the width to the family default.
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            var spec = Extract("ring-2", "ring-red-500", "ring-blue-500");
            Assert.That((spec.Width, spec.Color), Is.EqualTo((2f, blue)));
        }

        [Test]
        public void Given_ADefaultColorRing_When_Extracted_Then_TheColorIsHalfAlpha()
        {
            // Tailwind's default ring color is blue-500 at 0.5 alpha, not full opacity.
            Assert.That(Extract("ring-2").Color.a, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_AnExplicitColorRing_When_Extracted_Then_TheColorStaysOpaque()
        {
            // Only the DEFAULT ring color is semi-transparent; an explicit ring-<color> is opaque.
            Assert.That(Extract("ring-2", "ring-red-500").Color.a, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Given_ABareOutline_When_Extracted_Then_TheColorStaysOpaque()
        {
            // The 0.5-alpha default is ring-only; a color-less outline keeps its opaque band color.
            Assert.That(Extract("outline").Color.a, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Given_RingZero_When_Extracted_Then_NoRing()
        {
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-0" }, out _), Is.False);
        }

        [Test]
        public void Given_RingThenRingZero_When_Extracted_Then_LaterZeroCancels()
        {
            // ring-2 then ring-0 in the cascade: the later width-0 cancels the ring.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-2", "ring-0" }, out _), Is.False);
        }

        [Test]
        public void Given_OutlineWithARingColorToken_When_Extracted_Then_TheOutlineDefaultWidthSurvives()
        {
            // A COLOR names no width, so it must not decide which family's default width applies. It used to:
            // the colour token re-flagged the spec as the ring family, silently widening a bare `outline`
            // from the outline default to the 3px ring default. Only reachable now that a colour can arrive
            // from a variant, which is why it is pinned rather than left as a latent parser quirk.
            Assert.That(Extract("outline", "ring-red-500").Width, Is.EqualTo(1f));
        }

        [Test]
        public void Given_RingInsetWithRing_When_Extracted_Then_InsetIsSet()
        {
            Assert.That(Extract("ring-2", "ring-inset").Inset, Is.True);
        }

        [Test]
        public void Given_RingInsetAlone_When_Extracted_Then_NoRing()
        {
            // ring-inset is only a modifier; with no ring width/color/bare it establishes no ring.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-inset" }, out _), Is.False);
        }

        [Test]
        public void Given_RingWithOffset_When_Extracted_Then_OffsetIsResolved()
        {
            Assert.That(Extract("ring-2", "ring-offset-4").Offset, Is.EqualTo(4f));
        }

        [Test]
        public void Given_OutlinePreset_When_Extracted_Then_ResolvesWidth()
        {
            Assert.That(Extract("outline-2").Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_OutlineNone_When_Extracted_Then_NoRing()
        {
            Assert.That(StyleRingClass.TryExtract(new[] { "outline-none" }, out _), Is.False);
        }

        [Test]
        public void Given_OutlineWithOffset_When_Extracted_Then_OffsetIsResolved()
        {
            Assert.That(Extract("outline-2", "outline-offset-4").Offset, Is.EqualTo(4f));
        }

        [Test]
        public void Given_RingArbitraryHexColor_When_Extracted_Then_ResolvesThatColor()
        {
            // ring-[#ff0000] — an arbitrary hex ring color.
            StyleColorValueParser.TryParseColor("#ff0000".AsSpan(), out var expected);
            Assert.That(Extract("ring-2", "ring-[#ff0000]").Color, Is.EqualTo(expected));
        }

        [Test]
        public void Given_UnrecognizedRingSuffix_When_ExtractedAlone_Then_NoRing()
        {
            // ring-foo is neither a width nor a color token, so it establishes no ring on its own.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-foo" }, out _), Is.False);
        }

        [Test]
        public void Given_PlainUtility_When_GateChecked_Then_NotARingClass()
        {
            Assert.That(StyleRingClass.HasRingClass(new[] { "bg-red-500", "p-4" }), Is.False);
        }

        [Test]
        public void Given_RingClass_When_GateChecked_Then_IsClaimed()
        {
            Assert.That(StyleRingClass.HasRingClass(new[] { "p-4", "ring-2" }), Is.True);
        }
    }

    /// <summary>
    /// Geometry coverage for the ring overlay against the USS-scale radius and the element's own edge: once
    /// the ringed element is LAID OUT (a real panel), the band must size, position and round to its resolved
    /// box. Outset (default): inflated by (offset + width) per side with outer radius = radius + offset +
    /// width. Inset (ring-inset): matches the box at its own radius. Off-panel there is no resolved size, so
    /// this needs a real <see cref="UnityEditor.EditorWindow"/> panel + forced layout (the spec's width/color
    /// are asserted off-panel in ClipPathWrapTests, and the arbitrary / per-corner radius forms in
    /// RingOverlayTests). GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class RingGeometryPanelTests : PanelTestBase
    {
        // The band is hosted as a reconciler-invisible SIBLING of the ringed element, so it is found by its
        // marker rather than at a fixed index.
        private static VisualElement RingOverlayIn(VisualElement host)
        {
            for (var i = 0; i < host.childCount; i++)
            {
                if (host[i].ClassListContains(RingOverlay.MarkerClass))
                {
                    return host[i];
                }
            }
            return null;
        }

        private VisualElement MountRinged(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return RingOverlayIn(_window.rootVisualElement);
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlayInflatesByTwicePerSideWidth()
        {
            // 100x40 card, ring-2, no offset: the band is 2px outside each edge → overlay is 100+4 wide.
            var overlay = MountRinged("w-[100px] h-[40px] ring-2");

            Assert.That(overlay?.resolvedStyle.width, Is.EqualTo((float?)104f));
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlaySitsTwoPixelsOutsideTheInnerLeftEdge()
        {
            // The band's left edge sits (offset + width) = 2px outside the element's own left. Asserted
            // RELATIVE to the element rather than against an absolute coordinate, so the case stays about the
            // band's offset from its target and not about where the parent happened to place that target.
            var overlay = MountRinged("w-[100px] h-[40px] ring-2");
            var element = _window.rootVisualElement.Q<VisualElement>("card");

            Assert.That(element.layout.x - overlay?.resolvedStyle.left, Is.EqualTo((float?)2f));
        }

        [Test]
        public void Given_Ring2WithRoundedLg_When_LaidOut_Then_OuterRadiusAddsTheRingWidth()
        {
            // rounded-lg = 8px inner radius; the outset band's outer corner = 8 + 0 + 2 = 10.
            var overlay = MountRinged("w-[100px] h-[40px] rounded-lg ring-2");

            Assert.That(overlay?.resolvedStyle.borderTopLeftRadius, Is.EqualTo((float?)10f));
        }

        [Test]
        public void Given_RingWithOffset_When_LaidOut_Then_BandInflatesByOffsetPlusWidth()
        {
            // ring-2 ring-offset-4: the band sits 4px out then a 2px stroke → overlay is 100 + 2*(4+2) = 112.
            var overlay = MountRinged("w-[100px] h-[40px] ring-2 ring-offset-4");

            Assert.That(overlay?.resolvedStyle.width, Is.EqualTo((float?)112f));
        }

        [Test]
        public void Given_InsetRing_When_LaidOut_Then_OverlayMatchesTheInnerBox()
        {
            // ring-inset: the band is drawn inside, so the overlay matches the inner box exactly (no inflation).
            var overlay = MountRinged("w-[100px] h-[40px] ring-2 ring-inset");

            Assert.That(overlay?.resolvedStyle.width, Is.EqualTo((float?)100f));
        }

        [Test]
        public void Given_InsetRingWithRoundedLg_When_LaidOut_Then_OuterRadiusEqualsTheInnerRadius()
        {
            // An inset band hugs the inner edge, so its corner radius is the inner radius (8px), NOT inflated.
            var overlay = MountRinged("w-[100px] h-[40px] rounded-lg ring-2 ring-inset");

            Assert.That(overlay?.resolvedStyle.borderTopLeftRadius, Is.EqualTo((float?)8f));
        }
    }
}
