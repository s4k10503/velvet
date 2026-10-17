using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>current</c> and <c>inherit</c> as a colour, and the opacity modifier past 100% that computes as
    /// invalid: the text, background, border, ring and outline utilities, and the border colour preflight gives
    /// an element that sets a border width.
    /// </summary>
    internal sealed class ColourKeywordTests
    {
        private static (bool ok, ArbitraryProperty property, ColorReference reference, float alpha) Parse(string cls)
        {
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);
            return (ok, s.Property, s.Reference, s.Color.a);
        }

        [Test]
        public void Given_TextInherit_When_Parsed_Then_TheTextColourInherits()
        {
            Assert.That(Parse("text-inherit"), Is.EqualTo((true, ArbitraryProperty.TextColor, ColorReference.Inherit, 1f)));
        }

        [Test]
        public void Given_TextCurrent_When_Parsed_Then_TheTextColourReadsItself()
        {
            Assert.That(Parse("text-current"), Is.EqualTo((true, ArbitraryProperty.TextColor, ColorReference.Current, 1f)));
        }

        [Test]
        public void Given_BorderCurrent_When_Parsed_Then_TheBorderColourReadsTheTextColour()
        {
            Assert.That(Parse("border-current"), Is.EqualTo((true, ArbitraryProperty.BorderColor, ColorReference.Current, 1f)));
        }

        [Test]
        public void Given_BorderInherit_When_Parsed_Then_TheBorderColourInherits()
        {
            Assert.That(Parse("border-inherit"), Is.EqualTo((true, ArbitraryProperty.BorderColor, ColorReference.Inherit, 1f)));
        }

        [Test]
        public void Given_BackgroundCurrentWithAModifier_When_Parsed_Then_TheModifierScalesTheAlpha()
        {
            Assert.That(Parse("bg-current/25"), Is.EqualTo((true, ArbitraryProperty.BackgroundColor, ColorReference.Current, 0.25f)));
        }

        [Test]
        public void Given_ATextColourModifierPast100_When_Parsed_Then_TheTextColourInherits()
        {
            // Chromium computes `text-blue-500 text-red-500/150` as the parent's colour: the declaration is invalid
            // once computed, which leaves color at its inherited value.
            Assert.That(Parse("text-red-500/150"), Is.EqualTo((true, ArbitraryProperty.TextColor, ColorReference.Inherit, 1f)));
        }

        [Test]
        public void Given_ABorderColourModifierPast100_When_Parsed_Then_TheBorderIsCurrentColour()
        {
            // Chromium computes `border-blue-500 border-red-500/150` as currentcolor, border-color's initial value.
            Assert.That(Parse("border-red-500/150"), Is.EqualTo((true, ArbitraryProperty.BorderColor, ColorReference.Current, 1f)));
        }

        [Test]
        public void Given_ATextColourModifierWithin100_When_Parsed_Then_TheColourIsWritten()
        {
            // The control for the two cases above: a modifier inside the range names no reference.
            Assert.That(Parse("text-red-500/50").reference, Is.EqualTo(ColorReference.None));
        }

        private static RingSpec Ring(params string[] classes)
        {
            StyleRingClass.TryExtract(classes, classes.Length, out var spec);
            return spec;
        }

        [Test]
        public void Given_RingInherit_When_Extracted_Then_TheRingInherits()
        {
            var spec = Ring("ring-2", "ring-inherit");
            Assert.That((spec.Ring.InheritColor, spec.Ring.CurrentColor), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_RingCurrent_When_Extracted_Then_TheRingIsTheTextColour()
        {
            var spec = Ring("ring-2", "ring-current");
            Assert.That((spec.Ring.InheritColor, spec.Ring.CurrentColor), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_RingOffsetInherit_When_Extracted_Then_TheOffsetFillInherits()
        {
            var spec = Ring("ring-2", "ring-offset-2", "ring-offset-inherit");
            Assert.That(spec.OffsetSource, Is.EqualTo(ColorReference.Inherit));
        }

        [Test]
        public void Given_OutlineInherit_When_Extracted_Then_TheOutlineInherits()
        {
            var spec = Ring("outline-2", "outline-inherit");
            Assert.That((spec.Outline.InheritColor, spec.Outline.CurrentColor), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABorderWidth_When_PreflightSynced_Then_TheBorderColourLayerIsApplied()
        {
            var element = new VisualElement();

            PreflightBorderColor.Sync(element, new[] { "border" });

            Assert.That(StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight),
                Is.True);
        }

        [Test]
        public void Given_NoBorderWidth_When_PreflightSynced_Then_NoLayerIsApplied()
        {
            var element = new VisualElement();

            PreflightBorderColor.Sync(element, new[] { "p-2", "rounded" });

            Assert.That(StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight),
                Is.False);
        }

        [Test]
        public void Given_TheBorderWidthLeaves_When_PreflightSynced_Then_TheLayerIsCleared()
        {
            var element = new VisualElement();
            PreflightBorderColor.Sync(element, new[] { "border-2" });

            PreflightBorderColor.Sync(element, new[] { "p-2" });

            Assert.That(StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight),
                Is.False);
        }
    }
}
