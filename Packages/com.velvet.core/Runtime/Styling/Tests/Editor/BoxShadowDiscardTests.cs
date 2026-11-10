using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// What an opacity modifier outside 0..100% does to the one box-shadow Tailwind composes from an element's ring,
    /// shadow and inset utilities, and the modifier forms Tailwind generates. Every expected outcome is Chromium's
    /// computed box-shadow (or outline-color) for the same classes under Tailwind v4.3.3: none where the declaration is
    /// discarded. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowDiscardTests
    {
        private static bool Shadow(params string[] classNames) => StyleShadowClass.TryExtract(classNames, out _);

        private static string[] BesideShadowLg(string[] others)
        {
            var classes = new string[others.Length + 1];
            classes[0] = "shadow-lg";
            others.CopyTo(classes, 1);
            return classes;
        }

        // The control is shadow-lg alone: where it does not draw, the value is one the expectation cannot equal.
        [TestCase("inset-shadow-sm", "inset-shadow-red-500/150")]
        [TestCase("inset-ring-2", "inset-ring-red-500/150")]
        [TestCase("ring-offset-2", "ring-offset-red-500/150")]
        [TestCase("shadow-red-500/150")]
        [TestCase("shadow-red-500/[-0.5]")]
        [TestCase("ring-2", "ring-red-500/150")]
        [TestCase("shadow-[color:0_4px_8px]")]
        public void Given_AColourModifierPast100BesideAShadow_When_Extracted_Then_TheShadowIsDiscarded(params string[] rejected)
        {
            // Act
            var drawn = Shadow("shadow-lg") ? Shadow(BesideShadowLg(rejected)) : true;

            // Assert
            Assert.That(drawn, Is.False);
        }

        // A colour only matters to a layer that reads it.
        [TestCase("inset-ring-red-500/150")]
        [TestCase("ring-offset-red-500/150")]
        public void Given_AColourModifierPast100WithNoBandToColour_When_Extracted_Then_TheShadowStays(string unread)
        {
            Assert.That(Shadow("shadow-lg", unread), Is.True);
        }

        [Test]
        public void Given_ARingColourModifierPast100_When_Extracted_Then_TheRingGoesWithIt()
        {
            // The control is the same ring with a modifier inside the range.
            var inside = StyleRingClass.TryExtract(new[] { "ring-2", "ring-red-500/50" }, out _);
            var past = StyleRingClass.TryExtract(new[] { "ring-2", "ring-red-500/150" }, out _);

            Assert.That(inside ? past : true, Is.False);
        }

        [Test]
        public void Given_ASizeModifierPast100_When_Extracted_Then_ItsOwnColourIsOpaque()
        {
            // oklab(from <colour> l a b / 150%) clamps to an alpha of 1.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/150" }, out var spec);

            Assert.That(spec[0].Color.a, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ASizeModifierPast100BesideAShadowColour_When_Extracted_Then_TheShadowIsDiscarded()
        {
            // --tw-shadow-alpha: 150% leaves the colour's color-mix() invalid.
            var drawn = Shadow("shadow-lg/50", "shadow-red-500") ? Shadow("shadow-lg/150", "shadow-red-500") : true;

            Assert.That(drawn, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base discards a box-shadow whose bracket colour mixes an invalid alpha.
        [Test]
        public void Given_ASizeModifierPast100BesideABracketShadowColour_When_Extracted_Then_TheShadowIsDiscarded()
        {
            // Act — the control is the same pair with the size's modifier inside the range.
            var drawn = Shadow("shadow-lg/50", "shadow-[#ff0000]") ? Shadow("shadow-lg/150", "shadow-[#ff0000]") : true;

            // Assert
            Assert.That(drawn, Is.False);
        }

        [Test]
        public void Given_ASizeModifierPast100BesideShadowInherit_When_Extracted_Then_TheShadowStays()
        {
            // Act — Tailwind writes shadow-inherit as --tw-shadow-color: inherit, with no color-mix() for the alpha to
            // break.
            var drawn = Shadow("shadow-lg/150", "shadow-inherit");

            // Assert
            Assert.That(drawn, Is.True);
        }

        // A child whose shadow-inherit layers sit under parentClasses, with the parent's declared colours recorded.
        private static UnityEngine.UIElements.VisualElement InheritingChild(string[] parentClasses, string[] childClasses)
        {
            var parent = new UnityEngine.UIElements.VisualElement();
            var child = new UnityEngine.UIElements.VisualElement();
            parent.Add(child);
            BoxShadowSilhouette.RecordDeclaredColours(parent, parentClasses);
            StyleShadowClass.TryExtract(childClasses, out var layers);
            BoxShadowSilhouette.Attach(child, layers, childClasses);
            return child;
        }

        [TestCase("shadow-lg/150 shadow-red-500", "shadow-lg shadow-inherit")]
        [TestCase("inset-shadow-sm/150 inset-shadow-red-500", "inset-shadow-sm inset-shadow-inherit")]
        public void Given_AParentWhoseSizeModifierPast100MixesItsShadowColour_When_AChildInheritsIt_Then_TheChildDrawsNoBoxShadow(
            string parentClasses, string childClasses)
        {
            // Act — the parent's colour is a color-mix() the browser finds invalid where it is computed; the property is
            // registered with the universal syntax, so the child inherits that value as it is, and its box-shadow, which
            // reads it, is invalid in turn and computes to none.
            var discarded = BoxShadowSilhouette.DiscardedByInheritance(
                InheritingChild(parentClasses.Split(' '), childClasses.Split(' ')));

            // Assert
            Assert.That(discarded, Is.True);
        }

        [Test]
        public void Given_AParentWithAShadowColourInsideItsRange_When_AChildInheritsIt_Then_TheChildDrawsItsBoxShadow()
        {
            // Act
            var discarded = BoxShadowSilhouette.DiscardedByInheritance(
                InheritingChild(new[] { "shadow-lg/50", "shadow-red-500" }, new[] { "shadow-lg", "shadow-inherit" }));

            // Assert
            Assert.That(discarded, Is.False);
        }

        [Test]
        public void Given_AnImportantShadowInitialOverASizeModifierPast100AndAColour_When_Extracted_Then_TheLayersAreOpaqueBlack()
        {
            // Act — shadow-initial takes the colour class's place, so each layer falls back to its own colour, whose
            // alpha the size's /150 takes and clamps to 1.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/150", "shadow-red-500", "!shadow-initial" }, out var spec);

            // Assert
            Assert.That(spec.Length > 0 ? ColorUtility.ToHtmlStringRGBA(spec[0].Color) : "no layer", Is.EqualTo("000000FF"));
        }

        [Test]
        public void Given_ABracketedNegativeSizeModifier_When_Extracted_Then_ItsOwnColourIsClear()
        {
            // The size's own colour clamps to 0.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/[-0.5]" }, out var own);

            Assert.That(own[0].Color.a, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ABracketedNegativeSizeModifier_When_AShadowColourIsBeside_Then_TheColourIsWhole()
        {
            // --tw-shadow-alpha is a registered percentage and rejects the bare number for its initial 100%.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/[-0.5]", "shadow-red-500" }, out var coloured);

            Assert.That(coloured[0].Color.a, Is.EqualTo(1f));
        }

        [TestCase("ring-red-500/2.25", 0.0225f)]
        [TestCase("ring-red-500/[33.3%]", 0.333f)]
        [TestCase("ring-red-500/[0.5]", 0.5f)]
        public void Given_AModifierFormTailwindGenerates_When_Extracted_Then_ItScalesTheAlpha(string colour, float alpha)
        {
            StyleRingClass.TryExtract(new[] { "ring-2", colour }, out var spec);

            Assert.That(spec.Ring.Color.a, Is.EqualTo(alpha).Within(1e-4f));
        }

        [TestCase("ring-red-500/05")]
        [TestCase("ring-red-500/.5")]
        [TestCase("ring-red-500/50.0")]
        [TestCase("ring-red-500/2.3")]
        public void Given_AModifierTailwindDoesNotGenerate_When_Extracted_Then_TheRingNamesNoColour(string colour)
        {
            // An unwritable modifier names no utility, so the ring is left naming no colour.
            StyleRingClass.TryExtract(new[] { "ring-2", colour }, out var spec);

            Assert.That(spec.Ring.CurrentColor, Is.True);
        }

        [Test]
        public void Given_AColourHintedShadowBracket_When_Extracted_Then_ItIsNoSize()
        {
            // A color hint makes the bracket a colour even where it holds a shadow; the control is the same bracket
            // unhinted, a size.
            var drawn = Shadow("shadow-[0_4px_8px]") ? Shadow("shadow-[color:0_4px_8px]") : true;

            Assert.That(drawn, Is.False);
        }
    }
}
