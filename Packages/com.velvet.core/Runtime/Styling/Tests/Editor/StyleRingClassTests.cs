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
    /// keeps both, and the ring and the outline are bands of their own. <c>ring-0</c> / <c>outline-none</c>
    /// resolve to no ring. GWT, one assert per case.
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
            hasRing: false, NoBand, fill: new RingOffsetFill(false, new Color(float.NaN, float.NaN, float.NaN, float.NaN), ColorReference.None), inset: false, NoBand);

        private static RingSpec Extract(params string[] classNames)
            => StyleRingClass.TryExtract(classNames, out var spec) ? spec : NoRing;

        [Test]
        public void Given_BareRing_When_Extracted_Then_UsesTheOnePixelDefaultWidth()
        {
            // Act
            var width = Extract("ring").Ring.Width;

            // Assert: Tailwind v4's --default-ring-width.
            Assert.That(width, Is.EqualTo(1f));
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
            // Act
            var extracted = StyleRingClass.TryExtract(new[] { "ring-red-500" }, out _);

            // Assert: a ring colour writes only --tw-ring-color.
            Assert.That(extracted, Is.False);
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
        public void Given_AVariantColorOverAWidthAndColorBase_When_Extracted_Then_TheWidthSlotStays()
        {
            // A variant contributes its classes after the base ones, so it routinely names ONE slot while the
            // base named others. Resolving the ring whole-spec — the shadow parser's rule — would let a
            // variant that mentions only a colour reset the width to the family default.
            Assert.That(Extract("ring-2", "ring-red-500", "ring-blue-500").Ring.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AVariantColorOverAWidthAndColorBase_When_Extracted_Then_TheColorSlotMoves()
        {
            // Arrange
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);

            // Act
            var color = Extract("ring-2", "ring-red-500", "ring-blue-500").Ring.Color;

            // Assert
            Assert.That(color, Is.EqualTo(blue));
        }

        [Test]
        public void Given_ADefaultColorRing_When_Extracted_Then_ItTakesTheTextColour()
        {
            // Tailwind v4's ring is var(--tw-ring-color, currentcolor).
            Assert.That(Extract("ring-2").Ring.CurrentColor, Is.True);
        }

        [Test]
        public void Given_AnExplicitColorRing_When_Extracted_Then_TheColorStaysOpaque()
        {
            // Only the DEFAULT ring color is semi-transparent; an explicit ring-<color> is opaque.
            Assert.That(Extract("ring-2", "ring-red-500").Ring.Color.a, Is.EqualTo(1f).Within(1e-4f));
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
            // Act
            var extracted = StyleRingClass.TryExtract(new[] { "ring-0" }, out _);

            // Assert
            Assert.That(extracted, Is.False);
        }

        [Test]
        public void Given_RingThenRingZero_When_Extracted_Then_LaterZeroCancels()
        {
            // Act
            var extracted = StyleRingClass.TryExtract(new[] { "ring-2", "ring-0" }, out _);

            // Assert
            Assert.That(extracted, Is.False);
        }

        [Test]
        public void Given_RingNone_When_Extracted_Then_ItCancelsNothing()
        {
            // ring-none is no Tailwind utility.
            Assert.That(Extract("ring-2", "ring-none").Ring.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_OutlineWithARingColorToken_When_Extracted_Then_TheOutlineDefaultWidthSurvives()
        {
            // Act
            var width = Extract("outline", "ring-red-500").Outline.Width;

            // Assert: a colour names no width, so it must not decide which family's default width applies.
            Assert.That(width, Is.EqualTo(1f));
        }

        [Test]
        public void Given_OutlineWithARingColorToken_When_Extracted_Then_TheOutlineStillTakesTheTextColour()
        {
            // Act
            var spec = Extract("outline", "ring-red-500");

            // Assert: a ring colour is --tw-ring-color, which an outline does not read.
            Assert.That(spec.Outline.CurrentColor, Is.True);
        }

        [Test]
        public void Given_OutlineWithAnOutlineColorToken_When_Extracted_Then_TheOutlineIsThatColour()
        {
            // Arrange
            VelvetPalette.TryResolveColorToken("red-500", out var red);

            // Act
            var spec = Extract("outline", "outline-red-500");

            // Assert
            Assert.That(spec.Outline.Color, Is.EqualTo(red));
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
            Assert.That(StyleRingClass.TryExtract(new[] { "outline", "outline-none" }, out _), Is.False);
        }

        [Test]
        public void Given_OutlineWithOffset_When_Extracted_Then_OffsetIsResolved()
        {
            Assert.That(Extract("outline-2", "outline-offset-4").Outline.Offset, Is.EqualTo(4f));
        }

        // 3 is off the 0/1/2/4/8 Tailwind suggests, which is not the set it reads.
        [Test]
        public void Given_ABareRingWidthOffTheSuggestedScale_When_Extracted_Then_ItIsThatManyPixels()
        {
            Assert.That(Extract("ring-3").Ring.Width, Is.EqualTo(3f));
        }

        [Test]
        public void Given_ABareOutlineWidthOffTheSuggestedScale_When_Extracted_Then_ItIsThatManyPixels()
        {
            Assert.That(Extract("outline-3").Outline.Width, Is.EqualTo(3f));
        }

        // outline-offset may be negative, drawing the outline inside the border edge.
        [TestCase("-outline-offset-2", -2f)]
        [TestCase("-outline-offset-[3px]", -3f)]
        [TestCase("outline-offset-[-2px]", -2f)]
        public void Given_ANegativeOutlineOffset_When_Extracted_Then_TheOffsetIsNegative(string offset, float expected)
        {
            // Act
            var resolved = Extract("outline-2", offset).Outline.Offset;

            // Assert
            Assert.That(resolved, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ANegatedOutlineOffset_When_Gated_Then_HasRingClassLetsItThrough()
        {
            // Act
            var has = StyleRingClass.HasRingClass(new[] { "-outline-offset-2" });

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_ABareRingOffsetOffTheSuggestedScale_When_Extracted_Then_ItIsThatManyPixels()
        {
            Assert.That(Extract("ring-2", "ring-offset-3").Ring.Offset, Is.EqualTo(3f));
        }

        [Test]
        public void Given_ABareOutlineOffsetOffTheSuggestedScale_When_Extracted_Then_ItIsThatManyPixels()
        {
            Assert.That(Extract("outline-2", "outline-offset-3").Outline.Offset, Is.EqualTo(3f));
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
            // ring-foo is neither a width nor a color token, so it establishes no ring on its own.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-foo" }, out _), Is.False);
        }

        [Test]
        public void Given_UnrecognizedRingSuffixBesideAWidth_When_Extracted_Then_ItIsIgnored()
        {
            // Beside a width an unrecognized suffix must be IGNORED rather than cancel what the rest of the list resolved.
            Assert.That(Extract("ring-2", "ring-foo").Ring.Width, Is.EqualTo(2f));
        }

        // A ring is box-shadow and an outline is outline, two properties the browser draws together, each with its
        // own width, colour and offset.

        [Test]
        public void Given_ARingAndAnOutline_When_Extracted_Then_TheRingKeepsItsWidth()
        {
            Assert.That(Extract("ring-4", "outline-2").Ring.Width, Is.EqualTo(4f));
        }

        [Test]
        public void Given_ARingAndAnOutline_When_Extracted_Then_TheOutlineKeepsItsWidth()
        {
            Assert.That(Extract("ring-4", "outline-2").Outline.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_ARingOffsetAndAnOutlineOffset_When_Extracted_Then_TheOutlineKeepsItsOwnOffset()
        {
            Assert.That(Extract("ring-2", "ring-offset-4", "outline", "outline-offset-1").Outline.Offset, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ARingOffsetAndAnOutlineOffset_When_Extracted_Then_TheRingKeepsItsOwnOffset()
        {
            Assert.That(Extract("ring-2", "ring-offset-4", "outline", "outline-offset-1").Ring.Offset, Is.EqualTo(4f));
        }

        [Test]
        public void Given_ARejectedRingBesideAnOutline_When_Extracted_Then_TheRingGoesAndTheOutlineStays()
        {
            // The rejected width discards the box-shadow, which the outline is not part of.
            Assert.That(Extract("ring", "ring-[5PX]", "outline-2").Outline.Width, Is.EqualTo(2f));
        }

        [Test]
        public void Given_ARejectedRingBesideAnOutline_When_Extracted_Then_NoRingBandIsDrawn()
        {
            // Act
            var spec = Extract("ring", "ring-[5PX]", "outline-2");

            // Assert
            Assert.That(spec.RingSpread, Is.EqualTo(0f));
        }

        // The offset fill is --tw-ring-offset-shadow, a spread of the offset width in --tw-ring-offset-color, whose
        // initial value is #fff.

        [Test]
        public void Given_ARingOffsetNamingNoColour_When_Extracted_Then_ItIsFilledWhite()
        {
            Assert.That(Extract("ring-2", "ring-offset-2").OffsetColor, Is.EqualTo(Color.white));
        }

        [Test]
        public void Given_ARingOffset_When_Extracted_Then_TheFillSpansTheOffset()
        {
            Assert.That(Extract("ring-2", "ring-offset-2").OffsetSpread, Is.EqualTo(2f));
        }

        [Test]
        public void Given_ARingOffsetColour_When_Extracted_Then_TheFillTakesIt()
        {
            // Arrange
            StyleColorValueParser.TryParseColor("#00ff00".AsSpan(), out var green);

            // Act
            var fill = Extract("ring-2", "ring-offset-2", "ring-offset-[#00ff00]").OffsetColor;

            // Assert
            Assert.That(fill, Is.EqualTo(green));
        }

        [Test]
        public void Given_ARingWithAnOffset_When_Extracted_Then_TheRingSpreadSpansTheOffset()
        {
            // calc(<width> + var(--tw-ring-offset-width)): the fill covers the inner part of the ring's spread.
            Assert.That(Extract("ring-2", "ring-offset-4").RingSpread, Is.EqualTo(6f));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Extracted_Then_TheOffsetFillIsDrawn()
        {
            // The box-shadow Tailwind writes for shadow-* lists var(--tw-ring-offset-shadow) too.
            Assert.That(Extract("shadow-lg", "ring-offset-2").OffsetSpread, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Extracted_Then_NoRingIsDrawn()
        {
            Assert.That(Extract("shadow-lg", "ring-offset-2").RingSpread, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ARingOffsetAlone_When_Extracted_Then_NothingIsDrawn()
        {
            // No class declares a box-shadow for the offset fill to be listed in.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-offset-2" }, out _), Is.False);
        }

        [Test]
        public void Given_AnInsetRingBesideARingOffset_When_Extracted_Then_TheOffsetFillIsDrawn()
        {
            Assert.That(Extract("inset-ring-2", "ring-offset-2").OffsetSpread, Is.EqualTo(2f));
        }

        [Test]
        public void Given_RingZeroWithAnOffset_When_Extracted_Then_TheRingSpreadIsTheOffset()
        {
            // ring-0 still writes --tw-ring-shadow, a spread of 0px plus the offset width, under the offset fill.
            Assert.That(Extract("ring-0", "ring-offset-2").RingSpread, Is.EqualTo(2f));
        }

        [Test]
        public void Given_ARingOffsetCurrent_When_Extracted_Then_TheFillIsTheTextColour()
        {
            Assert.That(Extract("ring-2", "ring-offset-2", "ring-offset-current").OffsetSource, Is.EqualTo(ColorReference.Current));
        }

        [Test]
        public void Given_RingInherit_When_Extracted_Then_TheRingInherits()
        {
            Assert.That(Extract("ring-2", "ring-inherit").Ring.InheritColor, Is.True);
        }

        [Test]
        public void Given_RingCurrentAfterRingInherit_When_Extracted_Then_TheRingIsTheTextColour()
        {
            Assert.That(Extract("ring-2", "ring-inherit", "ring-current").Ring.CurrentColor, Is.True);
        }

        [Test]
        public void Given_OutlineInherit_When_Extracted_Then_TheOutlineInherits()
        {
            Assert.That(Extract("outline-2", "outline-inherit").Outline.InheritColor, Is.True);
        }

        [Test]
        public void Given_AnOutlineColourPast100_When_Extracted_Then_TheOutlineIsTheTextColour()
        {
            // outline-color is invalid once computed, so it takes its initial currentcolor.
            Assert.That(Extract("outline-2", "outline-red-500/150").Outline.CurrentColor, Is.True);
        }

        [Test]
        public void Given_AnOutlineColourWithin100_When_Extracted_Then_TheOutlineIsNotTheTextColour()
        {
            // The control for the case above.
            Assert.That(Extract("outline-2", "outline-red-500/50").Outline.CurrentColor, Is.False);
        }

        [Test]
        public void Given_AWidthWithALeadingZero_When_Extracted_Then_ItIsNoUtility()
        {
            // Tailwind generates ring-2 and not ring-02.
            Assert.That(StyleRingClass.TryExtract(new[] { "ring-02" }, out _), Is.False);
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
    /// the ringed element is LAID OUT (a real panel), the band must size and position to its resolved
    /// box. Outset (default): inflated by (offset + width) per side. Inset (ring-inset): the element's own
    /// first child, matching the padding box at its own radius. Off-panel
    /// there is no resolved size, so this needs a real <see cref="UnityEditor.EditorWindow"/> panel + forced layout (the spec's width/color
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
                    return host[i].Q<VisualElement>(RingOverlay.RingBandName);
                }
            }
            return null;
        }

        // A second mount in one case replaces the first, so the band found is always this one's.
        private VisualElement MountRinged(string className)
        {
            _mounted?.Dispose();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return RingBandIn(_window.rootVisualElement);
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_BandInflatesByTwicePerSideWidth()
        {
            // 100x40 card, ring-2, no offset: the band is 2px outside each edge → the band is 100+4 wide.
            var band = MountRinged("w-[100px] h-[40px] ring-2");

            Assert.That(band?.resolvedStyle.width, Is.EqualTo((float?)104f));
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_BandSitsTwoPixelsOutsideTheLeftEdge()
        {
            // The band's left edge sits (offset + width) = 2px outside the element's own left. Asserted
            // RELATIVE to the element rather than against an absolute coordinate, so the case stays about the
            // band's offset from its target and not about where the parent happened to place that target.
            var band = MountRinged("w-[100px] h-[40px] ring-2");
            var element = _window.rootVisualElement.Q<VisualElement>("card");

            Assert.That(element.worldBound.x - band?.worldBound.x, Is.EqualTo((float?)2f));
        }

        [Test]
        public void Given_RingWithOffset_When_LaidOut_Then_BandInflatesByOffsetPlusWidth()
        {
            // ring-2 ring-offset-4: the ring spreads 4 + 2 = 6px under the offset fill → the band is 100 + 2*6 = 112.
            var band = MountRinged("w-[100px] h-[40px] ring-2 ring-offset-4");

            Assert.That(band?.resolvedStyle.width, Is.EqualTo((float?)112f));
        }

        [Test]
        public void Given_Ring2WithRoundedLg_When_LaidOut_Then_TheBindingHoldsTheElementsRadius()
        {
            // rounded-lg = 8px; the band grows it by the spread in RingBandShape.Spread.
            MountRinged("w-[100px] h-[40px] rounded-lg ring-2");
            var element = _window.rootVisualElement.Q<VisualElement>("card");

            Assert.That(RingOverlay.TryGet(element).Radii.TopLeft.x, Is.EqualTo(8f));
        }

        // An inset band is the ringed element's own first child rather than its sibling.
        private static VisualElement InsetBandIn(VisualElement element)
        {
            for (var i = 0; i < element.childCount; i++)
            {
                if (element[i].ClassListContains(RingOverlay.InsetMarkerClass))
                {
                    return element[i];
                }
            }
            return null;
        }

        private VisualElement MountInsetRinged(string className)
        {
            _mounted?.Dispose();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return InsetBandIn(_window.rootVisualElement.Q<VisualElement>("card"));
        }

        [Test]
        public void Given_InsetRing_When_LaidOut_Then_OverlayMatchesTheInnerBox()
        {
            // ring-inset: the band is drawn inside, so the overlay matches the inner box exactly (no inflation).
            var overlay = MountInsetRinged("w-[100px] h-[40px] ring-2 ring-inset");

            Assert.That(overlay?.resolvedStyle.width, Is.EqualTo((float?)100f));
        }

        [Test]
        public void Given_InsetRingWithRoundedLg_When_LaidOut_Then_TheBindingHoldsThePaddingBoxRadius()
        {
            // An inset band hugs the inner edge, so its corner radius is the inner radius (8px), NOT inflated.
            MountInsetRinged("w-[100px] h-[40px] rounded-lg ring-2 ring-inset");
            var element = _window.rootVisualElement.Q<VisualElement>("card");

            Assert.That(RingOverlay.TryGet(element).InsideRadii.TopLeft.x, Is.EqualTo(8f));
        }
    }
}
