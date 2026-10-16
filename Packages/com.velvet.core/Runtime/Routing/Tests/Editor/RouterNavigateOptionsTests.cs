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
            => BuildRouter(start, Route("a"), Route("b"), Route("c"), Route("d"));

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
        public void Given_PathRelativeRouting_When_DotDotFromALayoutAboveTheLeaf_Then_DropsOneSegmentOfTheLayoutsPathname()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users/:id", children: new[] { Route("edit") }),
                    Route("users"),
                }),
            });
            router.NavigateSync("/users/7/edit");

            // Act
            router.NavigateAsync("..", new NavigateOptions { Relative = RelativeRoutingType.Path }, baseRouteIndex: 1)
                .GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/users"));
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
            Navigate(router, "/d");

            // Act
            router.NavigateAsync(-2).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/b"));
        }

        [Test]
        public void Given_ABlockedStepOfTwoBack_When_Proceed_Then_TheStepLandsTwoEntriesBack()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");
            Navigate(router, "/d");
            var blocker = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, blocker);
            router.NavigateAsync(-2).GetAwaiter().GetResult();

            // Act
            blocker.Proceed();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/b"));
        }

        [Test]
        public void Given_AnEarlierEntry_When_DeltaPlusTwo_Then_LandsTwoEntriesForward()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");
            Navigate(router, "/d");
            router.NavigateAsync(-3).GetAwaiter().GetResult();

            // Act
            router.NavigateAsync(2).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/c"));
        }

        [Test]
        public void Given_FewerEntriesForwardThanTheDelta_When_Navigated_Then_LandsOnTheLastEntry()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            Navigate(router, "/c");
            router.NavigateAsync(-2).GetAwaiter().GetResult();

            // Act
            router.NavigateAsync(9).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/c"));
        }

        [Test]
        public void Given_ADeltaOfZero_When_Navigated_Then_TheCurrentEntrysLoaderRunsAgain()
        {
            // Arrange
            var runs = 0;
            var router = BuildRouter("/a", Route("a", loader: (_, _) => VelvetTask.FromResult((object)(++runs))));

            // Act
            router.NavigateAsync(0).GetAwaiter().GetResult();

            // Assert
            Assert.That(runs, Is.EqualTo(2));
        }

        [Test]
        public void Given_AReplaceOfTheFirstEntry_When_Committed_Then_TheKeyIsNoLongerDefault()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            Navigate(router, "/b", new NavigateOptions { Replace = true });

            // Assert
            Assert.That(router.CurrentLocation!.Key, Is.Not.EqualTo("default"));
        }

        [Test]
        public void Given_ACommittedKey_When_Read_Then_ItIsEightBaseThirtySixCharacters()
        {
            // Arrange
            var router = BuildAt("/a");

            // Act
            Navigate(router, "/b");

            // Assert
            Assert.That(router.CurrentLocation!.Key, Does.Match("^[0-9a-z]{8}$"));
        }

        [Test]
        public void Given_ABlockerAskedAboutAPush_When_ConsultedWithState_Then_TheNextLocationCarriesIt()
        {
            // Arrange
            var router = BuildAt("/a");
            object? seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = args.NextLocation.State;
                return false;
            }, new RouteBlockerState());

            // Act
            Navigate(router, "/b", new NavigateOptions { State = "carried" });

            // Assert
            Assert.That(seen, Is.EqualTo("carried"));
        }

        [Test]
        public void Given_ABlockerAskedAboutAPush_When_Consulted_Then_TheNextLocationHasAFreshKey()
        {
            // Arrange
            var router = BuildAt("/a");
            string? seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = args.NextLocation.Key;
                return false;
            }, new RouteBlockerState());

            // Act
            Navigate(router, "/b");

            // Assert
            Assert.That(seen, Does.Match("^[0-9a-z]{8}$"));
        }

        [Test]
        public void Given_ABlockerAskedAboutAStepBack_When_Consulted_Then_TheNextLocationCarriesTheEntrysState()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b", new NavigateOptions { State = "on-b" });
            Navigate(router, "/c");
            object? seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = args.NextLocation.State;
                return false;
            }, new RouteBlockerState());

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(seen, Is.EqualTo("on-b"));
        }

        [Test]
        public void Given_ABlockerAskedAboutAStepBack_When_Consulted_Then_TheNextLocationHasTheEntrysKey()
        {
            // Arrange
            var router = BuildAt("/a");
            Navigate(router, "/b");
            var keyOfB = router.CurrentLocation!.Key;
            Navigate(router, "/c");
            string? seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = args.NextLocation.Key;
                return false;
            }, new RouteBlockerState());

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(seen, Is.EqualTo(keyOfB));
        }

        [Test]
        public void Given_ASearchOnlyTarget_When_Navigated_Then_ItKeepsThePathAndTakesTheSearch()
        {
            // Arrange
            var router = BuildAt("/a?old=1");

            // Act
            Navigate(router, "?q=1");

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/a?q=1"));
        }

        [Test]
        public void Given_ANavigationThatOnlyAddsAFragment_When_Navigated_Then_NoLoaderRunsAgain()
        {
            // Arrange
            var runs = 0;
            var router = BuildRouter("/a", Route("a", loader: (_, _) => VelvetTask.FromResult((object)(++runs))));

            // Act
            Navigate(router, "/a#top");

            // Assert
            Assert.That(runs, Is.EqualTo(1));
        }

        [Test]
        public void Given_ANavigationThatRemovesTheFragment_When_Navigated_Then_TheLoaderRunsAgain()
        {
            // Arrange
            var runs = 0;
            var router = BuildRouter("/a#top", Route("a", loader: (_, _) => VelvetTask.FromResult((object)(++runs))));

            // Act
            Navigate(router, "/a");

            // Assert
            Assert.That(runs, Is.EqualTo(2));
        }

        [Test]
        public void Given_PathRelativeRouting_When_DotDotFromASplatRoute_Then_StartsFromTheWholeMatchedPathname()
        {
            // Arrange
            var router = new Router(new[] { Route("/", children: new[] { Route("files/*") }) });
            router.NavigateSync("/files/a/b");

            // Act
            Navigate(router, "..", new NavigateOptions { Relative = RelativeRoutingType.Path });

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/files/a"));
        }

        [Test]
        public void Given_ADeltaOfZero_When_ABlockerIsConsulted_Then_TheHistoryActionIsBack()
        {
            // Arrange
            var router = BuildAt("/a");
            NavigationMode? seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = args.HistoryAction;
                return false;
            }, new RouteBlockerState());

            // Act
            router.NavigateAsync(0).GetAwaiter().GetResult();

            // Assert
            Assert.That(seen, Is.EqualTo(NavigationMode.Back));
        }
    }
}
