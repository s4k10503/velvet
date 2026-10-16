using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families Tailwind composes through one declaration — translate, the per-axis and uniform
    /// scale, skew, and the filters — where a value the browser rejects in one utility discards the family's
    /// declaration, and with it the family's other utilities in the same variant prefix and importance.
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
        public void Given_AFamilyHoldingARejectedValue_When_Normalized_Then_TheFamilyIsItsIdentity(string classes, string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        [Test]
        public void Given_ARejectionBehindAVariant_When_Normalized_Then_TheFamilyIsItsIdentityThereOnly()
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(new[] { "translate-x-4", "hover:translate-y-[10]" });

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo("translate-x-4 hover:translate-x-[0] hover:translate-y-[0]"));
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
