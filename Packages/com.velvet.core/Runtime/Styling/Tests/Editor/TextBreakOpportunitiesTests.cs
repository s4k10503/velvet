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

        [TestCase("aa bb cc", "aa|bb|cc", TestName = "Given_TwoSpaceSeparatedWords_When_Found_Then_EachIsAnItem")]
        [TestCase("a\tb  c", "a|b|c", TestName = "Given_ATabAndARunOfSpaces_When_Found_Then_BothAreGaps")]
        [TestCase("日本語", "日|本|語", TestName = "Given_Ideographs_When_Found_Then_TheyBreakBetweenEveryCharacter")]
        [TestCase("日本。語", "日|本。|語", TestName = "Given_AStopAmongIdeographs_When_Found_Then_NoLineBeginsWithIt")]
        [TestCase("日「本", "日|「本", TestName = "Given_AnOpeningBracketAmongIdeographs_When_Found_Then_NoLineEndsWithIt")]
        [TestCase("abc日", "abc|日", TestName = "Given_LatinBesideAnIdeograph_When_Found_Then_TheyBreakBetweenThem")]
        [TestCase("abcdef", "abcdef", TestName = "Given_ALatinWordWithNoSpaces_When_Found_Then_ItIsOneItem")]
        [TestCase("well-known", "well-|known", TestName = "Given_AHyphenBetweenLetters_When_Found_Then_ABreakFollowsIt")]
        [TestCase("a-1", "a-1", TestName = "Given_AHyphenBeforeADigit_When_Found_Then_NoBreakFollowsIt")]
        [TestCase("ab\u200Bcd", "ab\u200B|cd", TestName = "Given_AZeroWidthSpace_When_Found_Then_ABreakFollowsIt")]
        [TestCase("<font=\"A B\">x y", "<font=\"A B\">x|y", TestName = "Given_ATagHoldingASpace_When_Found_Then_TheTagIsNotBrokenInside")]
        [TestCase("a\u00A0b c", "a\u00A0b|c", TestName = "Given_ANoBreakSpace_When_Found_Then_ItIsNotAGap")]
        [TestCase("  a b  ", "  a|b", TestName = "Given_WhiteSpaceAroundTheText_When_Found_Then_TheLeadingStaysOnTheFirstItem")]
        [TestCase("コーヒー", "コー|ヒー", TestName = "Given_AProlongedSoundMark_When_Found_Then_NoLineBeginsWithIt")]
        [TestCase("日)", "日)", TestName = "Given_AClosingAsciiMarkAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt")]
        [TestCase("\U00020000\U00020001", "\U00020000|\U00020001", TestName = "Given_AnIdeographOutsideTheBasicPlane_When_Found_Then_TheSurrogatePairIsOneCharacter")]
        [TestCase("   ", "none", TestName = "Given_OnlyWhiteSpace_When_Found_Then_ThereIsNoItem")]
        [TestCase("foo-(bar)", "foo-|(bar)", TestName = "Given_AHyphenBeforeABracket_When_Found_Then_ABreakFollowsIt")]
        [TestCase("a\u00ADb", "a\u00ADb", TestName = "Given_ASoftHyphen_When_FoundWithoutSoftHyphens_Then_NoBreakFollowsIt")]
        [TestCase("1\u20132", "1\u2013|2", TestName = "Given_AnEnDashBeforeADigit_When_Found_Then_ABreakFollowsIt")]
        [TestCase("\u65E5%", "\u65E5%", TestName = "Given_APercentSignAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt")]
        [TestCase("\u65E5\u2026", "\u65E5\u2026", TestName = "Given_AnEllipsisAfterAnIdeograph_When_Found_Then_NoLineBeginsWithIt")]
        public void Given_AParagraph_When_Found_Then_ItIsCutIntoTheExpectedItems(string paragraph, string expected)
        {
            // Arrange / Act
            var items = Items(paragraph);

            // Assert
            Assert.That(items, Is.EqualTo(expected));
        }

        [TestCase("a\u00ADb", "a\u00AD|b", TestName = "Given_ASoftHyphen_When_FoundWithSoftHyphens_Then_ABreakFollowsIt")]
        public void Given_AParagraph_When_FoundWithSoftHyphens_Then_ItIsCutIntoTheExpectedItems(string paragraph, string expected)
        {
            // Arrange / Act
            var items = Items(paragraph, softHyphens: true);

            // Assert
            Assert.That(items, Is.EqualTo(expected));
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
