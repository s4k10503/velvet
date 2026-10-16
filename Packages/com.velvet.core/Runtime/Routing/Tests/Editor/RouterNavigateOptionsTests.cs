// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a location keeps of the entry it was made for, and the history delta and
    /// <c>relative: "path"</c> of <c>Router.NavigateAsync</c>, each against React Router's <c>navigate</c>
    /// and <c>location</c>.
    /// </summary>
    [TestFixture]
    internal sealed class RouterNavigateOptionsTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        private static Router BuildAt(string start)
            => BuildRouter(start, Route("a"), Route("b"), Route("c"));

        private static void Navigate(Router router, string path, NavigateOptions? options = null)
            => router.NavigateAsync(path, options).GetAwaiter().GetResult();

        [Test]
        public void Given_TheFirstEntry_When_Committed_Then_TheKeyIsDefault()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            var key = router.CurrentLocation!.Key;

            // Assert
            Assert.That(key, Is.EqualTo("default"));
        }

        [Test]
        public void Given_PushesAfterTheFirstEntry_When_Committed_Then_EveryKeyIsUnique()
        {
            // Arrange
            var router = BuildAt("/a");
            var keys = new List<string> { router.CurrentLocation!.Key };

            // Act
            Navigate(router, "/b");
            keys.Add(router.CurrentLocation!.Key);
            Navigate(router, "/c", new NavigateOptions { Replace = true });
            keys.Add(router.CurrentLocation!.Key);

            // Assert
            Assert.That(keys.Distinct().Count(), Is.EqualTo(keys.Count));
        }

        [Test]
        public void Given_AnEarlierEntry_When_SteppedBackOnto_Then_TheLocationHasTheKeyItWasMadeWith()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            var keyOfB = router.CurrentLocation!.Key;
            Navigate(router, "/c");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLocation!.Key, Is.EqualTo(keyOfB));
        }

        [Test]
        public void Given_AnEarlierEntryMadeWithState_When_SteppedBackOnto_Then_TheLocationCarriesThatState()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b", new NavigateOptions { State = "on-b" });
            Navigate(router, "/c", new NavigateOptions { State = "on-c" });

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLocation!.State, Is.EqualTo("on-b"));
        }

        [Test]
        public void Given_AnEarlierEntryWithAFragment_When_SteppedBackOnto_Then_TheHashComesBack()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b#top");
            Navigate(router, "/c");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLocation!.Hash, Is.EqualTo("#top"));
        }

        [Test]
        public void Given_FewerEntriesBackThanTheDelta_When_Navigated_Then_LandsOnTheFirstEntry()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");

            // Act
            router.NavigateAsync(-9).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/a"));
        }

        [Test]
        public void Given_PathRelativeRouting_When_DotDotFromAMultiSegmentRoute_Then_DropsOneSegment()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("/", children: new[] { Route("team/:id/settings"), Route("team/:id") }),
            });
            router.NavigateSync("/team/7/settings");

            // Act
            Navigate(router, "..", new NavigateOptions { Relative = RelativeRoutingType.Path });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/team/7"));
        }

        [Test]
        public void Given_AStateInTheOptions_When_Navigated_Then_TheLocationCarriesIt()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            Navigate(router, "/b", new NavigateOptions { State = "carried" });

            // Assert
            Assert.That(router.CurrentLocation!.State, Is.EqualTo("carried"));
        }

        [Test]
        public void Given_ReplaceInTheOptions_When_Navigated_Then_TheHistoryIndexStaysOnTheReplacedEntry()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");

            // Act
            Navigate(router, "/c", new NavigateOptions { Replace = true });

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABlockedAttemptCarryingState_When_Proceed_Then_TheCommittedLocationCarriesIt()
        {
            // Arrange
            var router = BuildAt("/a");
            var blocker = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, blocker);
            Navigate(router, "/b", new NavigateOptions { State = "carried" });

            // Act
            blocker.Proceed();

            // Assert
            Assert.That(router.CurrentLocation!.State, Is.EqualTo("carried"));
        }

        [Test]
        public void Given_AFragmentInTheTarget_When_Navigated_Then_ThePathHoldsNoneOfIt()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            Navigate(router, "/b?q=1#section");

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/b?q=1"));
        }

        [Test]
        public void Given_AFragmentInTheTarget_When_Navigated_Then_TheHashHoldsItWithItsMark()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            Navigate(router, "/b?q=1#section");

            // Assert
            Assert.That(router.CurrentLocation!.Hash, Is.EqualTo("#section"));
        }

        [Test]
        public void Given_ATargetThatIsOnlyAFragment_When_Navigated_Then_ItKeepsThePathAndDropsTheQuery()
        {
            // Arrange
            var router = BuildAt("/a?q=1");

            // Act
            Navigate(router, "#section");

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/a"));
        }

        [Test]
        public void Given_FourEntries_When_DeltaMinusTwo_Then_LandsTwoEntriesBack()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");
            Navigate(router, "/a");

            // Act
            router.NavigateAsync(-2).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_ABlockedStepOfTwoBack_When_Proceed_Then_TheHistoryIndexStepsTwoBack()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");
            Navigate(router, "/a");
            var blocker = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, blocker);
            router.NavigateAsync(-2).GetAwaiter().GetResult();

            // Act
            blocker.Proceed();

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }
    }
}
