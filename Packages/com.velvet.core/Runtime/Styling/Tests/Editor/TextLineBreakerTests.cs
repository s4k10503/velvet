using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="TextLineBreaker"/>'s choice of breaks over positions given as numbers, so no text engine
    /// is involved. The expected breaks were worked out against Chromium's ScoreLineBreaker cost, the one the
    /// class ports: squared slack per line, a penalty on breaking before the last word, a penalty per line.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextLineBreakerTests
    {
        private const float Space = 4f;

        // Words of the given widths separated by one space, as the positions Plan reads.
        private static (float[] lineStart, float[] wordEnd) Positions(params float[] widths)
        {
            var lineStart = new float[widths.Length];
            var wordEnd = new float[widths.Length];
            var x = 0f;
            for (var i = 0; i < widths.Length; i++)
            {
                lineStart[i] = i == 0 ? 0f : x + Space;
                x = lineStart[i] + widths[i];
                wordEnd[i] = x;
            }
            return (lineStart, wordEnd);
        }

        private static string Plan(
            float available, TextWrapStyle style, Func<int, int, bool>? fitsExactly, params float[] widths)
        {
            var (lineStart, wordEnd) = Positions(widths);
            var breaks = TextLineBreaker.Plan(lineStart, wordEnd, available, style, 4f * available * 16f, fitsExactly);
            return breaks == null ? "none" : string.Join(",", breaks);
        }

        private static string Plan(float available, TextWrapStyle style, params float[] widths) =>
            Plan(available, style, null, widths);

        [Test]
        public void Given_AShortLastLine_When_Balanced_Then_TheSecondLineTakesWordsFromTheFirst()
        {
            // Arrange / Act — greedy fills the first line and leaves one narrow word; the lowest cost is
            // 28 28 / 28 28 7, the first of the two ways to move words down.
            var breaks = Plan(124f, TextWrapStyle.Balance, 28f, 28f, 28f, 28f, 7f);

            // Assert
            Assert.That(breaks, Is.EqualTo("2"));
        }

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Pretty_Then_OnlyTheWordBeforeItMovesDown()
        {
            // Arrange / Act
            var breaks = Plan(124f, TextWrapStyle.Pretty, 28f, 28f, 28f, 28f, 7f);

            // Assert — the first line keeps three words, the smallest change that gives the last word company.
            Assert.That(breaks, Is.EqualTo("3"));
        }

        [Test]
        public void Given_ALastWordAThirdOfTheLineWide_When_Pretty_Then_NothingIsChanged()
        {
            // Arrange / Act — the same text as above with a last word of 60 of 124, over Chromium's third.
            var breaks = Plan(124f, TextWrapStyle.Pretty, 28f, 28f, 28f, 28f, 60f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_ALastLineOfTwoWords_When_Pretty_Then_NothingIsChanged()
        {
            // Arrange / Act
            var breaks = Plan(124f, TextWrapStyle.Pretty, 28f, 28f, 28f, 28f, 7f, 7f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_ThreeWords_When_Balanced_Then_NothingIsChanged()
        {
            // Arrange / Act — Chromium optimizes from four words.
            var breaks = Plan(60f, TextWrapStyle.Balance, 28f, 28f, 28f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_TextThatFitsOneLine_When_Balanced_Then_NothingIsChanged()
        {
            // Arrange / Act
            var breaks = Plan(200f, TextWrapStyle.Balance, 10f, 10f, 10f, 10f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_LinesThatAreAlreadyEven_When_Balanced_Then_NothingIsChanged()
        {
            // Arrange / Act
            var breaks = Plan(64f, TextWrapStyle.Balance, 30f, 30f, 30f, 30f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_MoreLinesThanChromiumBalances_When_Balanced_Then_NothingIsChanged()
        {
            // Arrange / Act — fourteen words of 30 in a line of 64 take seven lines, one over the six.
            var breaks = Plan(64f, TextWrapStyle.Balance, 30f, 30f, 30f, 30f, 30f, 30f, 30f,
                30f, 30f, 30f, 30f, 30f, 30f, 30f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_MoreLinesThanChromiumPrettifies_When_Pretty_Then_NothingIsChanged()
        {
            // Arrange / Act — six lines, over the four, with a last word that would otherwise qualify.
            var breaks = Plan(64f, TextWrapStyle.Pretty, 30f, 30f, 30f, 30f, 30f, 30f, 30f, 30f, 30f, 30f, 5f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_AWordWiderThanTheLine_When_Balanced_Then_NothingIsChanged()
        {
            // Arrange / Act
            var breaks = Plan(100f, TextWrapStyle.Balance, 30f, 30f, 300f, 30f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_NoWrapStyle_When_Planned_Then_NothingIsChanged()
        {
            // Arrange / Act
            var breaks = Plan(124f, TextWrapStyle.None, 28f, 28f, 28f, 28f, 7f);

            // Assert
            Assert.That(breaks, Is.EqualTo("none"));
        }

        [Test]
        public void Given_AFarFromBorderlineText_When_Planned_Then_TheExactCheckIsNeverAsked()
        {
            // Arrange
            var asked = 0;

            // Act — one line of 135 in 200, outside the tolerance of the width.
            Plan(200f, TextWrapStyle.Balance, (first, end) => { asked++; return true; }, 28f, 28f, 28f, 28f, 7f);

            // Assert
            Assert.That(asked, Is.EqualTo(0));
        }

        [Test]
        public void Given_ALineWithinTheToleranceOfTheWidth_When_Planned_Then_TheExactCheckIsAsked()
        {
            // Arrange
            var asked = 0;

            // Act — four words of 28 take 124 exactly, the whole width.
            Plan(124f, TextWrapStyle.Balance, (first, end) => { asked++; return true; }, 28f, 28f, 28f, 28f, 7f);

            // Assert
            Assert.That(asked > 0, Is.True);
        }
    }
}
