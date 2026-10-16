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
        // A class list that resolves to NO ring resolves here to a spec no assertion can match, so a parser
        // that stopped extracting fails every case that comes through here instead of skipping it. An Assume
        // would instead gate the very component this fixture exists to pin, and an inconclusive case is not
        // a failure to the runner — which is why CI ends on scripts/test_quality/assert_no_inconclusive.py.
        // No substitute value reaches a case that asserts the parser said NO, since that broken return value
        // IS what such a case expects. Each of those instead compares its negative against a class list the
        // working parser RESOLVES, in the same assertion.
        private static readonly RingBand NoBand = new(
            float.NaN, float.NaN, new Color(float.NaN, float.NaN, float.NaN, float.NaN), currentColor: false);

        private static readonly RingSpec NoRing = new(
            hasRing: false, NoBand, hasOffsetFill: false, new Color(float.NaN, float.NaN, float.NaN, float.NaN),
            inset: false, NoBand);

        private static RingSpec Extract(params string[] classNames)
            => StyleRingClass.TryExtract(classNames, out var spec) ? spec : NoRing;

        [Test]
        public void Given_BareRing_When_Extracted_Then_UsesTheOnePixelDefaultWidth()
        {
            // Tailwind v4's --default-ring-width.
            Assert.That(Extract("ring").Ring.Width, Is.EqualTo(1f));
        }

        [Test]
        public void Given_RingPreset_When_Extracted_Then_ResolvesWidth()
        {
            Assert.That(Extract("ring-2").Ring.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_RingArbitraryWidth_When_Extracted_Then_ResolvesPixels()
        {
            Assert.That(Extract("ring-[6px]").Ring.Width, Is.EqualTo(6f));
        }

        [Test]
        public void Given_RingColorOnly_When_Extracted_Then_NoRingIsDrawn()
        {
            // A ring colour writes only --tw-ring-color; the control is the same colour beside a bare ring.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-red-500" }, out _), StyleRingClass.TryExtract(new[] { "ring", "ring-red-500" }, out _)),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_RingWidthAndColor_When_Extracted_Then_ColorSlotIsComposite()
        {
            // ring-2 sets width, ring-red-500 sets color — both apply (composite, not last-spec-wins).
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            Assert.That(Extract("ring-2", "ring-red-500").Ring.Color, Is.EqualTo(red));
        }

        [Test]
        public void Given_TwoRingWidths_When_Extracted_Then_TheLastOneWins()
        {
            // Within ONE slot the cascade is still last-wins; composite means the slots are independent of
            // each other, not that an earlier value in the same slot survives.
            Assert.That(Extract("ring-2", "ring-4").Ring.Width, Is.EqualTo(4f));
        }

        [Test]
        public void Given_AVariantColorOverAWidthAndColorBase_When_Extracted_Then_OnlyTheColorSlotMoves()
        {
            // A variant contributes its classes after the base ones, so it routinely names ONE slot while the
            // base named others. Resolving the ring whole-spec — the shadow parser's rule — would let a
            // variant that mentions only a colour reset the width to the family default.
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            // The element's own classes are the first two; the variant's colour is appended after them.
            StyleRingClass.TryExtract(new[] { "ring-2", "ring-red-500", "ring-blue-500" }, 2, out var spec);
            Assert.That((spec.Ring.Width, spec.Ring.Color), Is.EqualTo((2f, blue)));
        }

        [Test]
        public void Given_ADefaultColorRing_When_Extracted_Then_ItTakesTheTextColour()
        {
            // Tailwind v4's ring is var(--tw-ring-color, currentcolor).
            Assert.That(Extract("ring-2").Ring.CurrentColor, Is.True);
        }

        [Test]
        public void Given_ARingColourWithAnOpacityModifier_When_Extracted_Then_ItsAlphaIsScaled()
        {
            // color-mix(in oklab, <colour> 50%, transparent), as Tailwind writes ring-red-500/50.
            Assert.That(Extract("ring-2", "ring-red-500/50").Ring.Color.a, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ABareOutline_When_Extracted_Then_ItTakesTheTextColour()
        {
            // outline-color's initial value is the text colour.
            Assert.That(Extract("outline").Outline.CurrentColor, Is.True);
        }

        [Test]
        public void Given_RingZero_When_Extracted_Then_NoRing()
        {
            Assert.That((StyleRingClass.TryExtract(new[] { "ring-0" }, out _), Extract("ring-1").Ring.Width),
                Is.EqualTo((false, 1f)));
        }

        [Test]
        public void Given_RingThenRingZero_When_Extracted_Then_OnlyAVariantZeroCancels()
        {
            // Among the element's own classes Tailwind emits ring-0 before ring-2, so ring-2 stands; a variant's
            // ring-0, appended after them, cancels it.
            Assert.That(
                (Extract("ring-2", "ring-0").Ring.Width, StyleRingClass.TryExtract(new[] { "ring-2", "ring-0" }, 1, out _)),
                Is.EqualTo((2f, false)));
        }

        [Test]
        public void Given_OutlineNoneWrittenBeforeAWidth_When_Extracted_Then_NoOutlineIsDrawn()
        {
            // Tailwind emits outline-none after outline-2; the control is the width alone.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "outline-none", "outline-2" }, out _), Extract("outline-2").Outline.Width),
                Is.EqualTo((false, 2f)));
        }

        [Test]
        public void Given_TwoRingColours_When_Extracted_Then_TheOneTailwindEmitsLastWins()
        {
            // ring-blue-500 is emitted before ring-red-500, so red wins written either way.
            VelvetPalette.TryResolveColorToken("red-500", out var red);
            Assert.That(Extract("ring-2", "ring-red-500", "ring-blue-500").Ring.Color, Is.EqualTo(red));
        }

        [Test]
        public void Given_OutlineWithARingColorToken_When_Extracted_Then_TheOutlineKeepsItsOwnColour()
        {
            // A ring colour is --tw-ring-color, which an outline does not read; the control is an outline colour,
            // which it does.
            var ringColoured = Extract("outline", "ring-red-500");
            var outlineColoured = Extract("outline", "outline-red-500");
            Assert.That(
                (ringColoured.Outline.Width, ringColoured.Outline.CurrentColor, outlineColoured.Outline.CurrentColor),
                Is.EqualTo((1f, true, false)));
        }

        [Test]
        public void Given_RingInsetWithRing_When_Extracted_Then_InsetIsSet()
        {
            Assert.That(Extract("ring-2", "ring-inset").Inset, Is.True);
        }

        [Test]
        public void Given_RingInsetAlone_When_Extracted_Then_NoRing()
        {
            // ring-inset is only a modifier; with no ring width/color/bare it establishes no ring. The
            // control is the flag it sets beside a width, so a parser that dropped the token entirely —
            // which also establishes no ring — does not satisfy the case.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-inset" }, out _), Extract("ring-2", "ring-inset").Inset),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_RingWithOffset_When_Extracted_Then_OffsetIsResolved()
        {
            Assert.That(Extract("ring-2", "ring-offset-4").Ring.Offset, Is.EqualTo(4f));
        }

        [Test]
        public void Given_OutlinePreset_When_Extracted_Then_ResolvesWidth()
        {
            Assert.That(Extract("outline-2").Outline.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_OutlineNone_When_Extracted_Then_NoRing()
        {
            Assert.That(
                (Extract("outline").Outline.Width, StyleRingClass.TryExtract(new[] { "outline-none" }, out _)),
                Is.EqualTo((1f, false)));
        }

        [Test]
        public void Given_OutlineWithOffset_When_Extracted_Then_OffsetIsResolved()
        {
            Assert.That(Extract("outline-2", "outline-offset-4").Outline.Offset, Is.EqualTo(4f));
        }

        [Test]
        public void Given_RingArbitraryHexColor_When_Extracted_Then_ResolvesThatColor()
        {
            // ring-[#ff0000] — an arbitrary hex ring color.
            StyleColorValueParser.TryParseColor("#ff0000".AsSpan(), out var expected);
            Assert.That(Extract("ring-2", "ring-[#ff0000]").Ring.Color, Is.EqualTo(expected));
        }

        [Test]
        public void Given_UnrecognizedRingSuffix_When_ExtractedAlone_Then_NoRing()
        {
            // ring-foo is neither a width nor a color token, so it establishes no ring on its own. The
            // control puts it beside a width, where it must be IGNORED rather than cancel what the rest of
            // the list resolved.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-foo" }, out _), Extract("ring-2", "ring-foo").Ring.Width),
                Is.EqualTo((false, 2f)));
        }

        // A ring is box-shadow and an outline is outline, two properties the browser draws together, each with its
        // own width, colour and offset.

        [Test]
        public void Given_ARingAndAnOutline_When_Extracted_Then_EachKeepsItsOwnWidth()
        {
            var spec = Extract("ring-4", "outline-2");
            Assert.That((spec.Ring.Width, spec.Outline.Width), Is.EqualTo((4f, 2f)));
        }

        [Test]
        public void Given_ARingOffsetAndAnOutlineOffset_When_Extracted_Then_EachKeepsItsOwn()
        {
            var spec = Extract("ring-2", "ring-offset-4", "outline", "outline-offset-1");
            Assert.That((spec.Ring.Offset, spec.Outline.Offset), Is.EqualTo((4f, 1f)));
        }

        [Test]
        public void Given_ARejectedRingColourBesideAnOutline_When_Extracted_Then_TheOutlineStays()
        {
            // The rejected colour discards the box-shadow, which the outline is not part of.
            var spec = Extract("ring", "ring-[5PX]", "outline-2");
            Assert.That((spec.HasRing, spec.Outline.Width), Is.EqualTo((false, 2f)));
        }

        // The offset fill is --tw-ring-offset-shadow, a spread of the offset width in --tw-ring-offset-color, whose
        // initial value is #fff.

        [Test]
        public void Given_ARingOffsetNamingNoColour_When_Extracted_Then_ItIsFilledWhite()
        {
            var spec = Extract("ring-2", "ring-offset-2");
            Assert.That((spec.OffsetSpread, spec.OffsetColor), Is.EqualTo((2f, Color.white)));
        }

        [Test]
        public void Given_ARingOffsetColour_When_Extracted_Then_TheFillTakesIt()
        {
            StyleColorValueParser.TryParseColor("#00ff00".AsSpan(), out var green);
            Assert.That(Extract("ring-2", "ring-offset-2", "ring-offset-[#00ff00]").OffsetColor, Is.EqualTo(green));
        }

        [Test]
        public void Given_ARingWithAnOffset_When_Extracted_Then_TheRingSpreadSpansTheOffset()
        {
            // calc(<width> + var(--tw-ring-offset-width)): the fill covers the inner part of the ring's spread.
            Assert.That(Extract("ring-2", "ring-offset-4").RingSpread, Is.EqualTo(6f));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Extracted_Then_OnlyTheOffsetFillIsDrawn()
        {
            // The box-shadow Tailwind writes for shadow-* lists var(--tw-ring-offset-shadow) too; the control is
            // the offset alone, which declares no box-shadow.
            var spec = Extract("shadow-lg", "ring-offset-2");
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-offset-2" }, out _), spec.OffsetSpread, spec.RingSpread),
                Is.EqualTo((false, 2f, 0f)));
        }

        [Test]
        public void Given_RingZeroWithAnOffset_When_Extracted_Then_TheRingSpreadIsTheOffset()
        {
            // ring-0 still writes --tw-ring-shadow, a spread of 0px plus the offset width, under the offset fill.
            var spec = Extract("ring-0", "ring-offset-2");
            Assert.That((spec.RingSpread, spec.OffsetSpread), Is.EqualTo((2f, 2f)));
        }

        // ring-<n>, outline-<n>, ring-offset-<n> and outline-offset-<n> take any integer Tailwind generates.

        [TestCase("ring-3")]
        [TestCase("ring-12")]
        public void Given_ARingWidthOffTheOldScale_When_Extracted_Then_ItIsThatManyPixels(string utility)
        {
            Assert.That(Extract(utility).Ring.Width, Is.EqualTo(float.Parse(utility.Substring("ring-".Length))));
        }

        [Test]
        public void Given_AnOutlineWidthOffTheOldScale_When_Extracted_Then_ItIsThatManyPixels()
        {
            Assert.That(Extract("outline-3").Outline.Width, Is.EqualTo(3f));
        }

        [Test]
        public void Given_OffsetsOffTheOldScale_When_Extracted_Then_EachIsThatManyPixels()
        {
            var spec = Extract("ring-2", "ring-offset-3", "outline", "outline-offset-5");
            Assert.That((spec.Ring.Offset, spec.Outline.Offset), Is.EqualTo((3f, 5f)));
        }

        [Test]
        public void Given_AWidthWithALeadingZero_When_Extracted_Then_ItIsNoUtility()
        {
            // Tailwind generates ring-2 and not ring-02; the control is ring-2.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-02" }, out _), StyleRingClass.TryExtract(new[] { "ring-2" }, out _)),
                Is.EqualTo((false, true)));
        }

        [TestCase("-outline-offset-2")]
        [TestCase("outline-offset-[-2px]")]
        public void Given_ANegativeOutlineOffset_When_Extracted_Then_TheOutlineMovesInside(string utility)
        {
            Assert.That(Extract("outline-2", utility).Outline.Offset, Is.EqualTo(-2f));
        }

        [Test]
        public void Given_ANegatedOutlineOffset_When_GateChecked_Then_ItIsARingClass()
        {
            Assert.That(
                (StyleRingClass.HasRingClass(new[] { "-outline-offset-2" }), StyleRingClass.HasRingClass(new[] { "-mt-2" })),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_PlainUtility_When_GateChecked_Then_NotARingClass()
        {
            // The control is the same list with one ring token appended: what the gate owes is a
            // DISCRIMINATION, and a gate hardwired to false answers the plain list correctly.
            Assert.That(
                (StyleRingClass.HasRingClass(new[] { "bg-red-500", "p-4" }),
                    StyleRingClass.HasRingClass(new[] { "bg-red-500", "p-4", "ring-2" })),
                Is.EqualTo((false, true)));
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
        // The overlay is hosted as a reconciler-invisible SIBLING of the ringed element, so it is found by its
        // marker rather than at a fixed index; the ring's band is its child of that name.
        private static VisualElement RingBandIn(VisualElement host)
        {
            for (var i = 0; i < host.childCount; i++)
            {
                if (host[i].ClassListContains(RingOverlay.MarkerClass))
                {
                    return host[i].Q<VisualElement>("velvet-ring-band");
                }
            }
            return null;
        }

        private VisualElement MountRinged(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return RingBandIn(_window.rootVisualElement);
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlayInflatesByTwicePerSideWidth()
        {
            // 100x40 card, ring-2, no offset: the band is 2px outside each edge → the band is 100+4 wide.
            var band = MountRinged("w-[100px] h-[40px] ring-2");

            Assert.That(band?.resolvedStyle.width, Is.EqualTo((float?)104f));
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlaySitsTwoPixelsOutsideTheInnerLeftEdge()
        {
            // The band's left edge sits (offset + width) = 2px outside the element's own left. Asserted
            // RELATIVE to the element rather than against an absolute coordinate, so the case stays about the
            // band's offset from its target and not about where the parent happened to place that target.
            var band = MountRinged("w-[100px] h-[40px] ring-2");
            var element = _window.rootVisualElement.Q<VisualElement>("card");

            Assert.That(element.worldBound.x - band?.worldBound.x, Is.EqualTo((float?)2f));
        }

        [Test]
        public void Given_Ring2WithRoundedLg_When_LaidOut_Then_OuterRadiusAddsTheRingWidth()
        {
            // rounded-lg = 8px inner radius; the outset band's outer corner = 8 + 0 + 2 = 10.
            var band = MountRinged("w-[100px] h-[40px] rounded-lg ring-2");

            Assert.That(band?.resolvedStyle.borderTopLeftRadius, Is.EqualTo((float?)10f));
        }

        [Test]
        public void Given_RingWithOffset_When_LaidOut_Then_BandInflatesByOffsetPlusWidth()
        {
            // ring-2 ring-offset-4: the ring spreads 4 + 2 = 6px under the offset fill → the band is 100 + 2*6 = 112.
            var band = MountRinged("w-[100px] h-[40px] ring-2 ring-offset-4");

            Assert.That(band?.resolvedStyle.width, Is.EqualTo((float?)112f));
        }

        [Test]
        public void Given_InsetRing_When_LaidOut_Then_OverlayMatchesTheInnerBox()
        {
            // ring-inset: the band is drawn inside, so the band matches the inner box exactly (no inflation).
            var band = MountRinged("w-[100px] h-[40px] ring-2 ring-inset");

            Assert.That(band?.resolvedStyle.width, Is.EqualTo((float?)100f));
        }

        [Test]
        public void Given_InsetRingWithRoundedLg_When_LaidOut_Then_OuterRadiusEqualsTheInnerRadius()
        {
            // An inset band hugs the inner edge, so its corner radius is the inner radius (8px), NOT inflated.
            var band = MountRinged("w-[100px] h-[40px] rounded-lg ring-2 ring-inset");

            Assert.That(band?.resolvedStyle.borderTopLeftRadius, Is.EqualTo((float?)8f));
        }
    }
}
