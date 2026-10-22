using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that Tailwind's arbitrary <c>text-decoration-line</c> property sets no decoration.
    /// fonts.md owns the limitation.
    /// </summary>
    [TestFixture]
    internal sealed class ArbitraryDecorationLineTests
    {
        // GREEN_ON_BASE(characterization): the base leaves an arbitrary text-decoration-line token unset.
        [Test]
        public void Given_UnderlineAndOverlineInOneArbitraryProperty_When_Parsed_Then_NoDecorationIsSet()
        {
            // Act
            var tokens = V.ParseClassNames("[text-decoration-line:underline_overline]");
            var effect = StyleTextEffectClass.Parse(tokens);

            // Assert — a set decoration reads as "decoration-set", so the token has to survive and set none.
            Assert.That(
                effect.Decoration == null ? string.Join(" ", tokens) : "decoration-set",
                Is.EqualTo("[text-decoration-line:underline_overline]"));
        }

        // GREEN_ON_BASE(characterization): the base leaves an arbitrary text-decoration-line token unset.
        [Test]
        public void Given_UnderlineAndLineThroughInOneArbitraryProperty_When_Parsed_Then_NoDecorationIsSet()
        {
            // Act
            var tokens = V.ParseClassNames("[text-decoration-line:underline_line-through]");
            var effect = StyleTextEffectClass.Parse(tokens);

            // Assert — a set decoration reads as "decoration-set", so the token has to survive and set none.
            Assert.That(
                effect.Decoration == null ? string.Join(" ", tokens) : "decoration-set",
                Is.EqualTo("[text-decoration-line:underline_line-through]"));
        }
    }
}
