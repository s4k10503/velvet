using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>current</c> and <c>inherit</c> as a colour, and the opacity modifier past 100% that computes as
    /// invalid: the text, background and border utilities, and the border colour preflight gives an element that sets a
    /// border width. The ring and outline readings sit with <see cref="StyleRingClassTests"/>. GWT, one assert per case.
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

        [Test]
        public void Given_ATextColourModifierWithin100_When_Parsed_Then_TheColourIsWritten()
        {
            // The control for the cases above: a modifier inside the range names no reference.
            Assert.That(Parse("text-red-500/50").Reference, Is.EqualTo(ColorReference.None));
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

        [Test]
        public void Given_AWidthBehindAVariant_When_PreflightSynced_Then_TheBorderColourLayerIsApplied()
        {
            var element = new VisualElement();

            PreflightBorderColor.Sync(element, new[] { "hover:border" });

            Assert.That(StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight),
                Is.True);
        }
    }
}
