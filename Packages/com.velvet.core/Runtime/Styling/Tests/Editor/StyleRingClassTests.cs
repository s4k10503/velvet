using System;
using NUnit.Framework;
using UnityEngine;

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
}
