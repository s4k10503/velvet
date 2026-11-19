using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the classes an unresolvable custom property falls back to where Velvet paints the utility itself,
    /// each one a Tailwind class of its own: a ring or outline in currentcolor, a font weight or family the
    /// element inherits, and a family name read as Tailwind reads a bracket.
    /// </summary>
    internal sealed class CustomPropertyFallbackClassTests
    {
        [Test]
        public void Given_RingCurrentAfterARingColour_When_Extracted_Then_TheRingIsInCurrentColor()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "ring", "ring-red-500", "ring-current" }, out var spec);

            // Assert
            Assert.That(spec.Ring.CurrentColor, Is.True);
        }

        [Test]
        public void Given_AnInheritedWeightAfterABoldClass_When_Extracted_Then_NoWeightIsOwn()
        {
            // Act
            StyleFontClass.TryExtract(new[] { "font-bold", "font-[weight:inherit]" }, out var intent);

            // Assert
            Assert.That(intent.HasWeight, Is.False);
        }

        [TestCase("font-[family-name:Open_Sans]", "Open Sans")]
        [TestCase("font-[family-name:My\\_Font]", "My_Font")]
        public void Given_AFamilyNameHint_When_Extracted_Then_TheBracketIsReadAsTailwindReadsIt(string cls, string family)
        {
            // Act
            StyleFontClass.TryExtract(new[] { cls }, out var intent);

            // Assert
            Assert.That(intent.Family, Is.EqualTo(family));
        }

        [Test]
        public void Given_AnAddressableKeyWithAnEscapedUnderscore_When_Extracted_Then_TheKeyKeepsTheUnderscore()
        {
            // Act
            StyleFontClass.TryExtract(new[] { "font-[family-name:addr:my\\_key]" }, out var intent);

            // Assert
            Assert.That(intent.Family, Is.EqualTo("addr:my_key"));
        }

        [Test]
        public void Given_APercentageInAnAlphaBracket_When_Parsed_Then_ItIsThatFraction()
        {
            // Act
            StyleColorValueParser.TryParseAlphaModifier("[25%]", out var alpha);

            // Assert
            Assert.That(alpha, Is.EqualTo(0.25f).Within(1e-6f));
        }
    }
}
