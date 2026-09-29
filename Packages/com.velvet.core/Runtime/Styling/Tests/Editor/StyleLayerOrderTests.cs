using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the rank <see cref="StyleLayerPriority"/> gives each variant against the order Tailwind's generated CSS
    /// resolves a tie in: specificity first, then the variants the rule carries compared as a bit set, highest
    /// first. No operator of the mutation campaign reaches the constants, so each ordering rule has a case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleLayerOrderTests
    {
        private static bool Ascending(params long[] ranks) => ranks.Zip(ranks.Skip(1), (a, b) => a < b).All(x => x);

        [Test]
        public void Given_TheQueriesAndTheChildCombinator_When_Ranked_Then_TheyFollowTailwindsOrder()
        {
            // Assert
            Assert.That(Ascending(StyleLayerPriority.Base, StyleLayerPriority.Supports, StyleLayerPriority.ResponsiveSm,
                StyleLayerPriority.ResponsiveMd, StyleLayerPriority.ResponsiveLg, StyleLayerPriority.ResponsiveXl,
                StyleLayerPriority.Responsive2xl, StyleLayerPriority.Dark, StyleLayerPriority.ChildVariant), Is.True);
        }

        [Test]
        public void Given_ThePseudoClassAndAttributeVariants_When_Ranked_Then_TheyFollowTailwindsOrder()
        {
            // Assert
            Assert.That(Ascending(StyleLayerPriority.GroupFocusWithin, StyleLayerPriority.GroupHover,
                StyleLayerPriority.GroupFocus, StyleLayerPriority.GroupActive, StyleLayerPriority.GroupDisabled,
                StyleLayerPriority.PeerChecked, StyleLayerPriority.PeerFocusWithin, StyleLayerPriority.PeerHover,
                StyleLayerPriority.PeerFocus, StyleLayerPriority.PeerActive, StyleLayerPriority.PeerDisabled,
                StyleLayerPriority.First, StyleLayerPriority.Last, StyleLayerPriority.Only, StyleLayerPriority.Odd,
                StyleLayerPriority.Even, StyleLayerPriority.Checked, StyleLayerPriority.Hover, StyleLayerPriority.Focus,
                StyleLayerPriority.FocusVisible, StyleLayerPriority.Active, StyleLayerPriority.Disabled,
                StyleLayerPriority.Has, StyleLayerPriority.Aria, StyleLayerPriority.Data, StyleLayerPriority.Nth,
                StyleLayerPriority.NthLast, StyleLayerPriority.ArbitrarySelector), Is.True);
        }

        [Test]
        public void Given_TheLowestPseudoClassVariantAndTheHighestQuery_When_Ranked_Then_ThePseudoClassWins()
        {
            // Assert — specificity decides before order.
            Assert.That(StyleLayerPriority.GroupFocusWithin > StyleLayerPriority.ChildVariant, Is.True);
        }

        [Test]
        public void Given_TwoPseudoClassParts_When_Stacked_Then_TheStackOutranksEveryOnePartVariant()
        {
            // Assert — hover:focus: is (0,3,0).
            Assert.That(StyleLayerPriority.Stack(StyleLayerPriority.Hover, StyleLayerPriority.Focus)
                > StyleLayerPriority.ArbitrarySelector, Is.True);
        }

        [Test]
        public void Given_AQueryAndAPseudoClass_When_Stacked_Then_TheStackSitsBetweenTheLastNamedVariantAndTheArbitrarySelector()
        {
            // Arrange
            var darkHover = StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Hover);

            // Assert — hover's specificity, and dark's bit above every named pseudo-class variant's.
            Assert.That(Ascending(StyleLayerPriority.NthLast, darkHover, StyleLayerPriority.ArbitrarySelector),
                Is.True);
        }

        [Test]
        public void Given_TwoStacksSharingTheirHighestPart_When_Ranked_Then_TheNextPartDecides()
        {
            // Assert
            Assert.That(StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Focus)
                > StyleLayerPriority.Stack(StyleLayerPriority.Dark, StyleLayerPriority.Hover), Is.True);
        }

        [Test]
        public void Given_TwoThreePartStacksSharingTheirTopTwoParts_When_Ranked_Then_TheThirdPartDecides()
        {
            // Arrange
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
            // Arrange
            var first = StyleLayerPriority.WithRule(StyleLayerPriority.Nth, 1);
            var later = StyleLayerPriority.WithRule(StyleLayerPriority.Nth, 3);

            // Assert
            Assert.That(Ascending(StyleLayerPriority.Nth, first, later, StyleLayerPriority.NthLast), Is.True);
        }

        [Test]
        public void Given_AKeyedRule_When_ItsRankIsRead_Then_ThePositionIsGone()
        {
            // Assert
            Assert.That(StyleLayerPriority.RankOf(StyleLayerPriority.WithRule(StyleLayerPriority.Data, 7)),
                Is.EqualTo(StyleLayerPriority.Data));
        }

        [Test]
        public void Given_AnImportantBaseAndTheHighestOrdinaryStack_When_Ranked_Then_TheImportantOneWins()
        {
            // Arrange
            var stack = StyleLayerPriority.Stack(
                StyleLayerPriority.Stack(StyleLayerPriority.ArbitrarySelector, StyleLayerPriority.Hover),
                StyleLayerPriority.Focus);

            // Assert
            Assert.That(StyleLayerPriority.ImportantOf(StyleLayerPriority.Base) > stack, Is.True);
        }

        [Test]
        public void Given_EachBreakpoint_When_ItsVariantRankIsRead_Then_ItIsThatBreakpointsOwn()
        {
            // Assert
            Assert.That(new[]
                {
                    StyleLayerPriority.ForVariant(StyleVariantKind.Sm), StyleLayerPriority.ForVariant(StyleVariantKind.Md),
                    StyleLayerPriority.ForVariant(StyleVariantKind.Lg), StyleLayerPriority.ForVariant(StyleVariantKind.Xl),
                    StyleLayerPriority.ForVariant(StyleVariantKind.Xxl),
                },
                Is.EqualTo(new[]
                {
                    StyleLayerPriority.ResponsiveSm, StyleLayerPriority.ResponsiveMd, StyleLayerPriority.ResponsiveLg,
                    StyleLayerPriority.ResponsiveXl, StyleLayerPriority.Responsive2xl,
                }));
        }
    }
}
