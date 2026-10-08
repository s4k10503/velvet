using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the break opportunities <see cref="TextBreakOpportunities"/> models, which decide the min-content
    /// width of a text item: the runs a line may not be broken inside, joined by "|" below.
    /// </summary>
    [TestFixture]
    internal sealed class TextBreakOpportunitiesTests
    {
        private static string Runs(string text)
        {
            var runs = new List<string>();
            TextBreakOpportunities.CollectRuns(text, runs);
            return string.Join("|", runs);
        }

        [Test]
        public void Given_TextSpacesSeparateWords_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "ab cd";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("ab|cd"));
        }

        [Test]
        public void Given_TextANoBreakSpaceJoinsItsNeighbours_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "ab\u00A0cd ef";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("ab\u00A0cd|ef"));
        }

        [Test]
        public void Given_TextAHyphenBetweenLetters_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "well-known";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("well-|known"));
        }

        [Test]
        public void Given_TextAHyphenBeforeADigit_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "a-1";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("a-1"));
        }

        [Test]
        public void Given_TextAZeroWidthSpace_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "ab\u200Bcd";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("ab|cd"));
        }

        [Test]
        public void Given_TextIdeographsAndKana_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "日本語かな";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("日|本|語|か|な"));
        }

        [Test]
        public void Given_TextAnIdeographicFullStop_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "日本。語";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("日|本。|語"));
        }

        [Test]
        public void Given_TextAnOpeningBracket_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "日「本";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("日|「本"));
        }

        [Test]
        public void Given_TextAnIdeographBesideLatin_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "ab日";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("ab|日"));
        }

        [Test]
        public void Given_TextASurrogatePairIdeograph_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "\U00020000\U00020001";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("\U00020000|\U00020001"));
        }

        [Test]
        public void Given_TextAnIdeographThenAComma_When_Split_Then_TheRunsAreTheUnbreakableOnes()
        {
            // Arrange
            const string text = "日,本";

            // Act
            var runs = Runs(text);

            // Assert
            Assert.That(runs, Is.EqualTo("日,|本"));
        }
    }
}
