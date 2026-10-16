using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a container variant prefix reads: the threshold each name on Tailwind v4's
    /// <c>--container-*</c> scale stands for, the floor and ceiling forms, the arbitrary lengths, the container
    /// name after a <c>/</c>, and the prefixes that are declined rather than claimed.
    /// </summary>
    [TestFixture]
    internal sealed class StyleContainerQueryTests
    {
        [TestCase("@3xs", 256f)]
        [TestCase("@2xs", 288f)]
        [TestCase("@xs", 320f)]
        [TestCase("@sm", 384f)]
        [TestCase("@md", 448f)]
        [TestCase("@lg", 512f)]
        [TestCase("@xl", 576f)]
        [TestCase("@2xl", 672f)]
        [TestCase("@3xl", 768f)]
        [TestCase("@4xl", 896f)]
        [TestCase("@5xl", 1024f)]
        [TestCase("@6xl", 1152f)]
        [TestCase("@7xl", 1280f)]
        public void Given_ANameOnTheContainerScale_When_Parsed_Then_ItsThresholdIsThatManyRem(
            string spelling, float expectedPx)
        {
            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out var query);

            // Assert
            Assert.That((parsed, query.Px, query.IsMax), Is.EqualTo((true, expectedPx, false)));
        }

        [TestCase("@min-md", 448f, false)]
        [TestCase("@max-md", 448f, true)]
        [TestCase("@max-3xs", 256f, true)]
        [TestCase("@min-7xl", 1280f, false)]
        public void Given_AMinOrMaxForm_When_Parsed_Then_ItIsAFloorOrACeilingAtTheScaledThreshold(
            string spelling, float expectedPx, bool expectedMax)
        {
            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out var query);

            // Assert
            Assert.That((parsed, query.Px, query.IsMax), Is.EqualTo((true, expectedPx, expectedMax)));
        }

        [TestCase("@[475px]", 475f, false)]
        [TestCase("@min-[475px]", 475f, false)]
        [TestCase("@max-[600px]", 600f, true)]
        [TestCase("@min-[40rem]", 640f, false)]
        [TestCase("@[24.5rem]", 392f, false)]
        public void Given_AnArbitraryLength_When_Parsed_Then_ItIsThatManyPx(
            string spelling, float expectedPx, bool expectedMax)
        {
            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out var query);

            // Assert
            Assert.That((parsed, query.Px, query.IsMax), Is.EqualTo((true, expectedPx, expectedMax)));
        }

        [TestCase("@md/main", "main")]
        [TestCase("@max-lg/side", "side")]
        [TestCase("@min-[10rem]/x", "x")]
        [TestCase("@[40rem]/a_b", "a_b")]
        [TestCase("@md", "")]
        public void Given_ANameAfterTheSlash_When_Parsed_Then_ItIsTheContainerAsked(string spelling, string expected)
        {
            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out var query);

            // Assert
            Assert.That((parsed, query.Name), Is.EqualTo((true, expected)));
        }

        [Test]
        public void Given_ASlashInsideTheBrackets_When_Parsed_Then_TheNameIsTheOneAfterTheLastBracket()
        {
            // Arrange — the division inside the brackets is a length's own, and the name follows them.
            var spelling = "@min-[calc(100px/2)]/side";

            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out var query);

            // Assert
            Assert.That((parsed, query.Name, query.Px), Is.EqualTo((true, "side", 50f)));
        }

        [Test]
        public void Given_ACalcLength_When_Parsed_Then_ItIsReadAsItsPixels()
        {
            // Act
            var parsed = StyleContainerQuery.TryParse("@min-[calc(10rem+2px)]", out var query);

            // Assert
            Assert.That((parsed, query.Px), Is.EqualTo((true, 162f)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("@")]
        [TestCase("md")]
        [TestCase("@container")]
        [TestCase("@container/main")]
        [TestCase("@8xl")]
        [TestCase("@max-")]
        [TestCase("@min-")]
        [TestCase("@max-8xl")]
        [TestCase("@md/")]
        [TestCase("@md/a/b")]
        [TestCase("@md//a")]
        [TestCase("@min-[-5px]")]
        [TestCase("@min-[50%]")]
        [TestCase("@min-[20em]")]
        [TestCase("@min-[500]")]
        [TestCase("@[500]")]
        [TestCase("@max-[12.]")]
        [TestCase("@min-[]")]
        [TestCase("@[]")]
        [TestCase("@foo")]
        public void Given_APrefixThatIsNotAContainerQuery_When_Parsed_Then_ItIsDeclined(string? spelling)
        {
            // Act
            var parsed = StyleContainerQuery.TryParse(spelling, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        [TestCase(447.99f, false)]
        [TestCase(448f, true)]
        [TestCase(448.01f, true)]
        public void Given_AFloor_When_AWidthIsTested_Then_ItMatchesAtOrAboveTheThreshold(float width, bool expected)
        {
            // Arrange
            StyleContainerQuery.TryParse("@md", out var query);

            // Act
            var matches = query.Matches(width);

            // Assert
            Assert.That(matches, Is.EqualTo(expected));
        }

        [TestCase(447.99f, true)]
        [TestCase(448f, false)]
        [TestCase(448.01f, false)]
        public void Given_ACeiling_When_AWidthIsTested_Then_ItMatchesOnlyBelowTheThreshold(float width, bool expected)
        {
            // Arrange
            StyleContainerQuery.TryParse("@max-md", out var query);

            // Act
            var matches = query.Matches(width);

            // Assert
            Assert.That(matches, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AWidthThatIsNotANumber_When_TestedAgainstAFloorAndACeiling_Then_NeitherMatches()
        {
            // Arrange — a container's resolved width is NaN until its first layout pass.
            StyleContainerQuery.TryParse("@md", out var floor);
            StyleContainerQuery.TryParse("@max-md", out var ceiling);

            // Act
            var matches = (floor.Matches(float.NaN), ceiling.Matches(float.NaN));

            // Assert
            Assert.That(matches, Is.EqualTo((false, false)));
        }
    }
}
