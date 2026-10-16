using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Which values the browser rejects in the one box-shadow Tailwind composes from an element's ring, shadow and inset
    /// utilities, discarding all of it, and what an opacity modifier past 100% does to each family. Every expected
    /// outcome is Chromium's computed box-shadow (or outline-color) for the same classes under Tailwind v4.3.3: none
    /// where the declaration is discarded. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowDiscardTests
    {
        private static bool Shadow(params string[] classNames) => StyleShadowClass.TryExtract(classNames, out _);

        [TestCase("inset-ring", "inset-ring-[5PX]")]
        [TestCase("inset-ring-[length:2]")]
        [TestCase("inset-shadow-[0_4_8_#000]")]
        [TestCase("inset-shadow-[5PX]")]
        [TestCase("inset-shadow-sm", "inset-shadow-red-500/150")]
        [TestCase("inset-ring-2", "inset-ring-red-500/150")]
        [TestCase("ring-offset-2", "ring-offset-red-500/150")]
        [TestCase("shadow-red-500/150")]
        [TestCase("shadow-red-500/[-0.5]")]
        [TestCase("shadow-[color:0_4px_8px]")]
        public void Given_ARejectedValueBesideAShadow_When_Extracted_Then_TheShadowIsDiscarded(params string[] rejected)
        {
            // Arrange — the control is shadow-lg alone.
            var classes = new string[rejected.Length + 1];
            classes[0] = "shadow-lg";
            rejected.CopyTo(classes, 1);

            // Act & Assert
            Assert.That((Shadow("shadow-lg"), Shadow(classes)), Is.EqualTo((true, false)));
        }

        [TestCase("inset-ring-[5PX]")]
        [TestCase("inset-ring-red-500/150")]
        [TestCase("ring-offset-red-500/150")]
        [TestCase("inset-shadow-sm", "inset-shadow-[5PX]")]
        public void Given_ARejectedValueNoLayerReads_When_Extracted_Then_TheShadowStays(params string[] unread)
        {
            // Arrange — a colour with no band to colour, and a rejected size Tailwind emits before a valid one.
            var classes = new string[unread.Length + 1];
            classes[0] = "shadow-lg";
            unread.CopyTo(classes, 1);

            // Act & Assert
            Assert.That(Shadow(classes), Is.True);
        }

        [Test]
        public void Given_ARingBesideARejectedShadowValue_When_Extracted_Then_TheRingGoesWithIt()
        {
            // A length hint makes the bracket a box-shadow value, which #ff0000 alone is not.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "ring-2" }, out _),
                    StyleRingClass.TryExtract(new[] { "ring-2", "shadow-[length:#ff0000]" }, out _)),
                Is.EqualTo((true, false)));
        }

        // An opacity modifier past 100%.

        [Test]
        public void Given_ASizeModifierPast100_When_Extracted_Then_ItsOwnColourIsOpaque()
        {
            // oklab(from <colour> l a b / 150%) clamps to an alpha of 1.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/150" }, out var spec);
            Assert.That(spec.Color.a, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ASizeModifierPast100BesideAShadowColour_When_Extracted_Then_TheShadowIsDiscarded()
        {
            // --tw-shadow-alpha: 150% leaves the colour's color-mix() invalid.
            Assert.That((Shadow("shadow-lg/150"), Shadow("shadow-lg/150", "shadow-red-500")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ABracketedNegativeSizeModifier_When_Extracted_Then_ItsOwnColourIsClearButAShadowColourIsWhole()
        {
            // The size's own colour clamps to 0; --tw-shadow-alpha rejects the bare number for its initial 100%.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/[-0.5]" }, out var own);
            StyleShadowClass.TryExtract(new[] { "shadow-lg/[-0.5]", "shadow-red-500" }, out var coloured);
            Assert.That((own.Color.a, coloured.Color.a), Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Given_AnOutlineColourPast100_When_Extracted_Then_TheOutlineIsTheTextColour()
        {
            // outline-color is invalid once computed, so it takes its initial currentcolor; the control is a colour
            // inside the range.
            Assert.That(
                (StyleRingClass.TryExtract(new[] { "outline-2", "outline-red-500/150" }, out var past) && past.Outline.CurrentColor,
                    StyleRingClass.TryExtract(new[] { "outline-2", "outline-red-500/50" }, out var within) && within.Outline.CurrentColor),
                Is.EqualTo((true, false)));
        }

        [TestCase("ring-red-500/2.25", 0.0225f)]
        [TestCase("ring-red-500/[33.3%]", 0.333f)]
        public void Given_AModifierFormTailwindGenerates_When_Extracted_Then_ItScalesTheAlpha(string colour, float alpha)
        {
            StyleRingClass.TryExtract(new[] { "ring-2", colour }, out var spec);
            Assert.That(spec.Ring.Color.a, Is.EqualTo(alpha).Within(1e-4f));
        }

        [Test]
        public void Given_AColourHintedShadowBracket_When_Extracted_Then_ItIsNoSize()
        {
            // A color hint makes the bracket a colour even where it holds a shadow; the control is the same bracket
            // unhinted, a size.
            Assert.That((Shadow("shadow-[0_4px_8px]"), Shadow("shadow-[color:0_4px_8px]")), Is.EqualTo((true, false)));
        }
    }
}
