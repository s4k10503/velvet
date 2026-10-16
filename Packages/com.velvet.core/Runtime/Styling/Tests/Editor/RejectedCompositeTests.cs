using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families Tailwind composes through one declaration — translate, the per-axis and uniform
    /// scale, skew, and the filters — where a value the browser rejects discards the family's declaration wherever
    /// it is the value its custom property takes, after importance, variants and Tailwind's order.
    /// </summary>
    internal sealed class RejectedCompositeTests
    {
        [TestCase("translate-x-4 translate-y-[10]", "translate-x-[0] translate-y-[0]")]
        [TestCase("scale-x-[2] scale-y-[2px]", "scale-x-[1] scale-y-[1]")]
        [TestCase("scale-50 scale-x-[abc]", "scale-x-[1] scale-y-[1]")]
        [TestCase("skew-x-6 skew-y-[10]", "skew-x-[0] skew-y-[0]")]
        [TestCase("blur-sm brightness-[abc]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0]")]
        [TestCase("drop-shadow-lg blur-[2]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0] drop-shadow-none")]
        [TestCase("blur-[-2px]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0]")]
        [TestCase("blur-sm p-4 hue-rotate-[45]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0] p-4")]
        [TestCase("translate-y-[10] hover:translate-x-4", "translate-x-[0] translate-y-[0]")]
        [TestCase("!translate-y-[10] translate-x-4", "!translate-x-[0] !translate-y-[0]")]
        [TestCase("!skew-x-[8] skew-x-6", "!skew-x-[0] !skew-y-[0]")]
        [TestCase("skew-x-6 skew-x-[8]", "skew-x-[0] skew-y-[0]")]
        [TestCase("translate-x-4 translate-z-[10]", "translate-x-[0] translate-y-[0]")]
        [TestCase("!brightness-[abc] hover:brightness-50", "!blur-[0] !brightness-[1] !contrast-[1] !grayscale-[0] !hue-rotate-[0] !invert-[0] !saturate-[1] !sepia-[0]")]
        public void Given_AFamilyHoldingARejectedValue_When_Normalized_Then_TheFamilyIsItsIdentity(string classes, string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        // The identity is important where it has to outrank an important base class; the last case spells the
        // variant's rejected value important as Tailwind does, behind the variant.
        [TestCase("translate-x-4 hover:translate-y-[10]", "translate-x-4 hover:translate-x-[0] hover:translate-y-[0]")]
        [TestCase("!skew-x-6 hover:skew-y-[8]", "!skew-x-6 !hover:skew-x-[0] !hover:skew-y-[0]")]
        [TestCase("!skew-x-6 hover:!skew-y-[8]", "!skew-x-6 !hover:skew-x-[0] !hover:skew-y-[0]")]
        public void Given_ARejectionBehindAVariant_When_Normalized_Then_TheFamilyIsItsIdentityThereOnly(string classes,
            string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        // Each variant outranks every rejected value of the base, so the family holds there with the base's other
        // values, while the base keeps its identity.
        [TestCase("skew-x-[8] skew-y-3 hover:skew-x-6", "skew-x-[0] skew-y-[0] hover:skew-x-6 hover:skew-y-3")]
        [TestCase("translate-y-[10] hover:translate-y-4", "translate-x-[0] translate-y-[0] hover:translate-y-4")]
        [TestCase("scale-y-[abc] hover:scale-y-50", "scale-x-[1] scale-y-[1] hover:scale-y-50")]
        [TestCase("blur-sm hue-rotate-[45] hover:hue-rotate-15", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0] hover:blur-sm hover:hue-rotate-15")]
        [TestCase("drop-shadow-lg drop-shadow-red-500 blur-[2] hover:blur-sm", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0] drop-shadow-none hover:blur-sm hover:drop-shadow-red-500 hover:drop-shadow-lg")]
        [TestCase("!skew-x-[8] skew-y-3 !hover:skew-x-6","!skew-x-[0] !skew-y-[0] !hover:skew-x-6 !hover:skew-y-3")]
        public void Given_AVariantOutrankingARejectedBase_When_Normalized_Then_TheFamilyHoldsUnderIt(string classes,
            string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        // GREEN_ON_BASE(characterization): the base kept every token here, having no composed-family rejection at all.
        // Each rejected value loses its property to an important class, to a class Tailwind emits later (blur-sm
        // after blur-[-2px], one axis after the uniform utility), or is read by no declaration that wins (a z
        // utility's, outranked by an important x one).
        [TestCase("skew-x-[8] !skew-x-6")]
        [TestCase("translate-x-[10] !translate-x-4")]
        [TestCase("scale-x-[abc] !scale-x-50")]
        [TestCase("!scale-50 scale-x-[abc]")]
        [TestCase("blur-[-2px] blur-sm")]
        [TestCase("drop-shadow-lg blur-[2] !blur-sm")]
        [TestCase("translate-[10] translate-x-4 translate-y-4")]
        [TestCase("!translate-x-4 translate-z-[10]")]
        [TestCase("!scale-x-50 scale-z-[abc]")]
        public void Given_ARejectedValueAnotherClassOutranks_When_Normalized_Then_TheFamilyIsKept(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }

        // GREEN_ON_BASE(characterization): every value here is one the browser takes, so the base kept them too.
        [TestCase("translate-x-4 translate-y-[10px]")]
        [TestCase("scale-x-[2] scale-y-[50%]")]
        [TestCase("skew-x-6 skew-y-[0]")]
        [TestCase("blur-sm brightness-[1.5]")]
        [TestCase("blur-sm hue-rotate-[.5TURN]")]
        public void Given_AFamilyOfValuesTheBrowserTakes_When_Normalized_Then_ItIsKept(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }

        // GREEN_ON_BASE(characterization): the base kept both tokens, as a dropped plain declaration takes no sibling.
        [TestCase("scale-x-[2] scale-[2px]")]
        [TestCase("rotate-45 rotate-[45]")]
        public void Given_APlainDeclarationTheBrowserDrops_When_Normalized_Then_ItsNeighbourStays(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
        }
    }
}
