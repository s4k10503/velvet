using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the rank <see cref="StyleLayerPriority"/> gives each variant family against the order Tailwind's
    /// generated CSS resolves a tie in: specificity first, then the variants the rule carries compared as a bit
    /// set, highest first. No operator of the mutation campaign reaches the constants, so each ordering rule has
    /// a case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleLayerOrderTests
    {
        private static bool Ascending(params long[] ranks) => ranks.Zip(ranks.Skip(1), (a, b) => a < b).All(x => x);

        [Test]
        public void Given_TheQueriesAndTheChildCombinator_When_Ranked_Then_TheyFollowTailwindsOrder()
        {
            // Arrange
            var ranks = new[]
            {
                StyleLayerPriority.Base, StyleLayerPriority.Supports, StyleLayerPriority.ResponsiveSm,
                StyleLayerPriority.ResponsiveMd, StyleLayerPriority.ResponsiveLg, StyleLayerPriority.ResponsiveXl,
                StyleLayerPriority.Responsive2xl, StyleLayerPriority.Dark, StyleLayerPriority.ChildVariant,
            };

            // Act
            var ascending = Ascending(ranks);

            // Assert
            Assert.That(ascending, Is.True);
        }

        [Test]
        public void Given_ThePseudoClassAndAttributeVariants_When_Ranked_Then_TheyFollowTailwindsOrder()
        {
            // Arrange
            var ranks = new[]
            {
                StyleLayerPriority.GroupFocusWithin, StyleLayerPriority.GroupHover, StyleLayerPriority.GroupFocus,
                StyleLayerPriority.GroupActive, StyleLayerPriority.GroupDisabled, StyleLayerPriority.PeerChecked,
                StyleLayerPriority.PeerFocusWithin, StyleLayerPriority.PeerHover, StyleLayerPriority.PeerFocus,
                StyleLayerPriority.PeerActive, StyleLayerPriority.PeerDisabled, StyleLayerPriority.First,
                StyleLayerPriority.Last, StyleLayerPriority.Only, StyleLayerPriority.Odd, StyleLayerPriority.Even,
                StyleLayerPriority.Checked, StyleLayerPriority.Hover, StyleLayerPriority.Focus,
                StyleLayerPriority.FocusVisible, StyleLayerPriority.Active, StyleLayerPriority.Disabled,
                StyleLayerPriority.Has, StyleLayerPriority.Aria, StyleLayerPriority.Data, StyleLayerPriority.Nth,
                StyleLayerPriority.NthLast, StyleLayerPriority.ArbitrarySelector,
            };

            // Act
            var ascending = Ascending(ranks);

            // Assert
            Assert.That(ascending, Is.True);
        }

        [Test]
        public void Given_TheLowestPseudoClassVariantAndTheChildCombinator_When_Ranked_Then_ThePseudoClassWins()
        {
            // Act — specificity decides before order, and [&>*]: is the highest rank that adds no class.
            var pseudoClassWins = StyleLayerPriority.GroupFocusWithin > StyleLayerPriority.ChildVariant;

            // Assert
            Assert.That(pseudoClassWins, Is.True);
        }

        [Test]
        public void Given_TwoPseudoClassParts_When_Stacked_Then_TheStackOutranksEveryOnePartVariant()
        {
            // Act — hover:focus: is (0,3,0).
            var stack = StyleLayerPriority.Stack(StyleLayerPriority.Hover, StyleLayerPriority.Focus);

            // Assert
            Assert.That(stack > StyleLayerPriority.ArbitrarySelector, Is.True);
        }

        [Test]
        public void Given_AQueryAndAPseudoClass_When_Stacked_Then_TheStackSitsBetweenTheLastNamedVariantAndTheArbitrarySelector()
        {
            // Act — hover's specificity, and dark's bit above every named pseudo-class variant's.
            var darkHover = StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Hover);

            // Assert
            Assert.That(Ascending(StyleLayerPriority.NthLast, darkHover, StyleLayerPriority.ArbitrarySelector),
                Is.True);
        }

        [Test]
        public void Given_TwoStacksSharingTheirHighestPart_When_Ranked_Then_TheNextPartDecides()
        {
            // Act
            var darkFocus = StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Focus);
            var darkHover = StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Hover);

            // Assert
            Assert.That(darkFocus > darkHover, Is.True);
        }

        [Test]
        public void Given_TwoThreePartStacksSharingTheirTopTwoParts_When_Ranked_Then_TheThirdPartDecides()
        {
            // Act
            var withActive = StyleLayerPriority.Stack(
                StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.GroupActive), StyleLayerPriority.Focus);
            var withHover = StyleLayerPriority.Stack(
                StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.GroupHover), StyleLayerPriority.Focus);

            // Assert
            Assert.That(withActive > withHover, Is.True);
        }

        [Test]
        public void Given_TwoRulesOfOneRank_When_Keyed_Then_TheLaterOneIsHigher_AndBothBelowTheNextRank()
        {
            // Act
            var first = StyleLayerPriority.WithRule(StyleLayerPriority.Nth, 1);
            var later = StyleLayerPriority.WithRule(StyleLayerPriority.Nth, 3);

            // Assert
            Assert.That(Ascending(StyleLayerPriority.Nth, first, later, StyleLayerPriority.NthLast), Is.True);
        }

        [Test]
        public void Given_AKeyedRule_When_ItsRankIsRead_Then_ThePositionIsGone()
        {
            // Arrange
            var key = StyleLayerPriority.WithRule(StyleLayerPriority.Data, 7);

            // Act
            var rank = StyleLayerPriority.RankOf(key);

            // Assert
            Assert.That(rank, Is.EqualTo(StyleLayerPriority.Data));
        }

        [Test]
        public void Given_AnImportantBaseAndTheHighestOrdinaryStack_When_Ranked_Then_TheImportantOneWins()
        {
            // Arrange
            var stack = StyleLayerPriority.Stack(
                StyleLayerPriority.Stack(StyleLayerPriority.ArbitrarySelector, StyleLayerPriority.Hover),
                StyleLayerPriority.Focus);

            // Act
            var important = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);

            // Assert
            Assert.That(important > stack, Is.True);
        }

        [Test]
        public void Given_EachBreakpoint_When_ItsVariantRankIsRead_Then_ItIsThatBreakpointsOwn()
        {
            // Act
            var ranks = new[]
            {
                StyleLayerPriority.ForVariant(StyleVariantKind.Sm), StyleLayerPriority.ForVariant(StyleVariantKind.Md),
                StyleLayerPriority.ForVariant(StyleVariantKind.Lg), StyleLayerPriority.ForVariant(StyleVariantKind.Xl),
                StyleLayerPriority.ForVariant(StyleVariantKind.Xxl),
            };

            // Assert
            Assert.That(ranks, Is.EqualTo(new[]
            {
                StyleLayerPriority.ResponsiveSm, StyleLayerPriority.ResponsiveMd, StyleLayerPriority.ResponsiveLg,
                StyleLayerPriority.ResponsiveXl, StyleLayerPriority.Responsive2xl,
            }));
        }
    }
}
