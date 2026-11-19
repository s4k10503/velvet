using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>current</c> and <c>inherit</c> as a colour, and the opacity modifier past 100% that computes as
    /// invalid: the text, background and border utilities. The ring and outline readings sit with <see cref="StyleRingClassTests"/>. GWT, one assert per case.
    /// </summary>
    internal sealed class ColourKeywordTests
    {
        private static ArbitraryStyle Parse(string cls)
            => StyleArbitraryValueResolver.TryParse(cls, out var s) ? s : ArbitraryStyle.RejectedIn(ArbitraryProperty.Width);

        [TestCase("text-inherit", ArbitraryProperty.TextColor)]
        [TestCase("border-inherit", ArbitraryProperty.BorderColor)]
        [TestCase("bg-inherit", ArbitraryProperty.BackgroundColor)]
        public void Given_AnInheritColour_When_Parsed_Then_ThePropertyInherits(string cls, ArbitraryProperty property)
        {
            var parsed = Parse(cls);

            Assert.That(parsed.Property == property ? parsed.Reference : ColorReference.None, Is.EqualTo(ColorReference.Inherit));
        }

        [TestCase("text-current", ArbitraryProperty.TextColor)]
        [TestCase("border-current", ArbitraryProperty.BorderColor)]
        [TestCase("bg-current", ArbitraryProperty.BackgroundColor)]
        public void Given_ACurrentColour_When_Parsed_Then_ThePropertyReadsTheTextColour(string cls, ArbitraryProperty property)
        {
            var parsed = Parse(cls);

            Assert.That(parsed.Property == property ? parsed.Reference : ColorReference.None, Is.EqualTo(ColorReference.Current));
        }

        [Test]
        public void Given_BackgroundCurrentWithAModifier_When_Parsed_Then_TheModifierScalesTheAlpha()
        {
            Assert.That(Parse("bg-current/25").Color.a, Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Given_InheritWithAModifier_When_Parsed_Then_ItNamesNoUtility()
        {
            // color-mix() cannot hold inherit, so Tailwind drops the declaration.
            Assert.That(StyleArbitraryValueResolver.TryParse("text-inherit/50", out _), Is.False);
        }

        [Test]
        public void Given_ATextColourModifierPast100_When_Parsed_Then_TheTextColourInherits()
        {
            // Chromium computes `text-blue-500 text-red-500/150` as the parent's colour: the declaration is invalid
            // once computed, which leaves color at its inherited value.
            Assert.That(Parse("text-red-500/150").Reference, Is.EqualTo(ColorReference.Inherit));
        }

        [Test]
        public void Given_ABorderColourModifierPast100_When_Parsed_Then_TheBorderIsCurrentColour()
        {
            // Chromium computes `border-blue-500 border-red-500/150` as currentcolor, border-color's initial value.
            Assert.That(Parse("border-red-500/150").Reference, Is.EqualTo(ColorReference.Current));
        }

        [Test]
        public void Given_ABackgroundColourModifierPast100_When_Parsed_Then_TheBackgroundIsTransparent()
        {
            // Chromium computes `bg-blue-500 bg-red-500/150` as rgba(0, 0, 0, 0), background-color's initial value.
            Assert.That(Parse("bg-red-500/150").Color, Is.EqualTo(Color.clear));
        }

        [TestCase("bg-[#ff0000]/150")]
        [TestCase("text-[#ff0000]/150")]
        [TestCase("border-[#ff0000]/150")]
        [TestCase("bg-transparent/150")]
        [TestCase("text-transparent/150")]
        [TestCase("border-transparent/150")]
        public void Given_AVarlessColourModifierPast100_When_Parsed_Then_TheClassIsDeclined(string cls)
        {
            // Tailwind writes no var() there, so the out-of-range percentage is a parse error that drops the declaration
            // and leaves an earlier class standing.
            Assert.That(StyleArbitraryValueResolver.TryParse(cls, out _), Is.False);
        }

        [Test]
        public void Given_ABracketedVarColourModifierPast100_When_Parsed_Then_TheBackgroundIsUnset()
        {
            // Tailwind writes the var() inside the color-mix(), which is then invalid only once computed.
            Assert.That(StyleColorValueParser.RejectedAtComputedTime("bg-[var(--x)]/150", 3), Is.True);
        }

        [Test]
        public void Given_ABackgroundCurrentModifierPast100_When_Parsed_Then_TheBackgroundIsPlainCurrentColour()
        {
            var parsed = Parse("bg-current/150");

            Assert.That(parsed.Reference == ColorReference.Current ? parsed.Color.a : float.NaN, Is.EqualTo(1f));
        }

        [Test]
        public void Given_AnOutlineBracketedColourPast100_When_Extracted_Then_TheOutlineKeepsAnEarlierColour()
        {
            // The earlier class stands: blue-500, where the palette colour past 100% would have left the text colour.
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            StyleRingClass.TryExtract(new[] { "outline-2", "outline-blue-500", "outline-[#ff0000]/150" }, out var spec);

            Assert.That(spec.Outline.CurrentColor ? Color.clear : spec.Outline.Color, Is.EqualTo(blue));
        }

        [Test]
        public void Given_ATextColourModifierWithin100_When_Parsed_Then_TheColourIsWritten()
        {
            // The control for the cases above: a modifier inside the range names no reference.
            Assert.That(Parse("text-red-500/50").Reference, Is.EqualTo(ColorReference.None));
        }
    }
}
