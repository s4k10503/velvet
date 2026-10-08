using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="TextBreakOpportunities"/>: where a paragraph may break, as the items the opportunities
    /// cut it into. The expected splits are the cases of the UAX #14 subset its header names. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextBreakOpportunitiesTests
    {
        private static string Items(string paragraph, bool softHyphens = false)
        {
            var starts = new List<int>();
            var ends = new List<int>();
            if (!TextBreakOpportunities.Find(paragraph, starts, ends, softHyphens))
            {
                return "none";
            }
            return string.Join("|", Enumerable.Range(0, starts.Count)
                .Select(i => paragraph.Substring(starts[i], ends[i] - starts[i])));
        }

        [Test]
        public void Given_TwoSpaceSeparatedWords_When_Found_Then_EachIsAnItem()
        {
            // Arrange / Act
            var items = Items("aa bb cc");

            // Assert
            Assert.That(items, Is.EqualTo("aa|bb|cc"));
        }

        [Test]
        public void Given_ATabAndARunOfSpaces_When_Found_Then_BothAreGaps()
        {
            // Arrange / Act
            var items = Items("a\tb  c");

            // Assert
            Assert.That(items, Is.EqualTo("a|b|c"));
        }

        [Test]
        public void Given_Ideographs_When_Found_Then_TheyBreakBetweenEveryCharacter()
        {
            // Arrange / Act
            var items = Items("日本語");

            // Assert
            Assert.That(items, Is.EqualTo("日|本|語"));
        }

        [Test]
        public void Given_AStopAmongIdeographs_When_Found_Then_NoLineBeginsWithIt()
        {
            // Arrange / Act
            var items = Items("日本。語");

            // Assert
            Assert.That(items, Is.EqualTo("日|本。|語"));
        }

        [Test]
        public void Given_AnOpeningBracketAmongIdeographs_When_Found_Then_NoLineEndsWithIt()
        {
            // Arrange / Act
            var items = Items("日「本");

            // Assert
            Assert.That(items, Is.EqualTo("日|「本"));
        }

        [Test]
        public void Given_LatinBesideAnIdeograph_When_Found_Then_TheyBreakBetweenThem()
        {
            // Arrange / Act
            var items = Items("abc日");

            // Assert
            Assert.That(items, Is.EqualTo("abc|日"));
        }

        [Test]
        public void Given_ALatinWordWithNoSpaces_When_Found_Then_ItIsOneItem()
        {
            // Arrange / Act
            var items = Items("abcdef");

            // Assert
            Assert.That(items, Is.EqualTo("abcdef"));
        }

        [Test]
        public void Given_AHyphenBetweenLetters_When_Found_Then_ABreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("well-known");

            // Assert
            Assert.That(items, Is.EqualTo("well-|known"));
        }

        [Test]
        public void Given_AHyphenBeforeADigit_When_Found_Then_NoBreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("a-1");

            // Assert
            Assert.That(items, Is.EqualTo("a-1"));
        }

        [Test]
        public void Given_AZeroWidthSpace_When_Found_Then_ABreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("ab\u200Bcd");

            // Assert
            Assert.That(items, Is.EqualTo("ab\u200B|cd"));
        }

        [Test]
        public void Given_ATagHoldingASpace_When_Found_Then_TheTagIsNotBrokenInside()
        {
            // Arrange / Act
            var items = Items("<font=\"A B\">x y");

            // Assert
            Assert.That(items, Is.EqualTo("<font=\"A B\">x|y"));
        }

        [Test]
        public void Given_ANoBreakSpace_When_Found_Then_ItIsNotAGap()
        {
            // Arrange / Act
            var items = Items("a\u00A0b c");

            // Assert
            Assert.That(items, Is.EqualTo("a\u00A0b|c"));
        }

        [Test]
        public void Given_WhiteSpaceAroundTheText_When_Found_Then_TheLeadingStaysOnTheFirstItem()
        {
            // Arrange / Act
            var items = Items("  a b  ");

            // Assert
            Assert.That(items, Is.EqualTo("  a|b"));
        }

        [Test]
        public void Given_AProlongedSoundMark_When_Found_Then_NoLineBeginsWithIt()
        {
            // Arrange / Act
            var items = Items("コーヒー");

            // Assert
            Assert.That(items, Is.EqualTo("コー|ヒー"));
        }

        [Test]
        public void Given_AClosingAsciiMarkAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt()
        {
            // Arrange / Act
            var items = Items("日)");

            // Assert
            Assert.That(items, Is.EqualTo("日)"));
        }

        [Test]
        public void Given_AnIdeographOutsideTheBasicPlane_When_Found_Then_TheSurrogatePairIsOneCharacter()
        {
            // Arrange / Act
            var items = Items("\U00020000\U00020001");

            // Assert
            Assert.That(items, Is.EqualTo("\U00020000|\U00020001"));
        }

        [Test]
        public void Given_OnlyWhiteSpace_When_Found_Then_ThereIsNoItem()
        {
            // Arrange / Act
            var items = Items("   ");

            // Assert
            Assert.That(items, Is.EqualTo("none"));
        }

        [Test]
        public void Given_AHyphenBeforeABracket_When_Found_Then_ABreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("foo-(bar)");

            // Assert
            Assert.That(items, Is.EqualTo("foo-|(bar)"));
        }

        [Test]
        public void Given_ASoftHyphen_When_FoundWithoutSoftHyphens_Then_NoBreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("a\u00ADb");

            // Assert
            Assert.That(items, Is.EqualTo("a\u00ADb"));
        }

        [Test]
        public void Given_ASoftHyphen_When_FoundWithSoftHyphens_Then_ABreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("a\u00ADb", softHyphens: true);

            // Assert
            Assert.That(items, Is.EqualTo("a\u00AD|b"));
        }

        [Test]
        public void Given_AnEnDashBeforeADigit_When_Found_Then_ABreakFollowsIt()
        {
            // Arrange / Act
            var items = Items("1\u20132");

            // Assert
            Assert.That(items, Is.EqualTo("1\u2013|2"));
        }

        [Test]
        public void Given_APercentSignAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt()
        {
            // Arrange / Act
            var items = Items("\u65E5%");

            // Assert
            Assert.That(items, Is.EqualTo("\u65E5%"));
        }

        [Test]
        public void Given_AnEllipsisAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt()
        {
            // Arrange / Act
            var items = Items("\u65E5\u2026");

            // Assert
            Assert.That(items, Is.EqualTo("\u65E5\u2026"));
        }

        [Test]
        public void Given_AZeroWidthSpaceBesideAnIdeograph_When_Found_Then_ItStaysWithTheCharacterBeforeIt()
        {
            // Arrange / Act
            var items = Items("\u65E5\u200B\u672C");

            // Assert — a break follows the space and none precedes it.
            Assert.That(items, Is.EqualTo("\u65E5\u200B|\u672C"));
        }

        [Test]
        public void Given_Text_When_RunsAreCollected_Then_TheyAreTheItemsWithSoftHyphensHonoured()
        {
            // Arrange
            var runs = new List<string>();

            // Act
            TextBreakOpportunities.CollectRuns("a b\u00ADc", runs);

            // Assert
            Assert.That(string.Join("|", runs), Is.EqualTo("a|b\u00AD|c"));
        }
    }
}
