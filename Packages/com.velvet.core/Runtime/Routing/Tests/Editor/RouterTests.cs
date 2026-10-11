using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Bounded so a case awaiting a task that never completes fails at thirty seconds rather than at the
    // runner's own bound, which UnityRunnerDefaultTimeoutTests pins.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouterTests
    {
        private RouteDefinition[] _routes;

        [SetUp]
        public void SetUp()
        {
            _routes = new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("about"),
                }),
            };
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        #region Initial state

        [Test]
        public void Given_FreshRouter_When_NotYetNavigated_Then_NoNavigationIsInFlight()
        {
            // Arrange

            // Act
            var router = new Router(_routes);

            // Assert
            Assert.That(router.Navigation.State, Is.EqualTo(NavigationLifecycle.Idle));
        }

        #endregion

        #region Navigation outcome

        [Test]
        public void Given_ValidPath_When_Navigating_Then_ReturnsSuccess()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            var result = router.NavigateSync("/home");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_ValidPath_When_Navigating_Then_NoNavigationIsLeftInFlight()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(
                $"state={router.Navigation.State} location={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("state=Idle location=none"));
        }

        [Test]
        public void Given_ValidPath_When_Navigating_Then_CommitsLocation()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/home"));
        }

        [Test]
        public void Given_UnmatchedPath_When_Navigating_Then_ReturnsNotFound()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            var result = router.NavigateSync("/nonexistent");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.NotFound));
        }

        [Test]
        public void Given_ACommittedLocation_When_AnUnmatchedPathIsNavigatedTo_Then_TheRouterStillDescribesThatLocation()
        {
            // The router's state describes the committed location and the navigation in flight, and an
            // attempt that matched nothing is neither: it answers its own caller and leaves both as they were.
            // Arrange
            var router = BuildRouter("/home", _routes);
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateSync("/nonexistent");

            // Assert
            Assert.That(
                $"result={result} path={router.CurrentLocation?.Path} state={router.Navigation.State} "
                + $"published={string.Join(",", published)}",
                Is.EqualTo("result=NotFound path=/home state=Idle published="));
        }

        [Test]
        public void Given_ACommittedLocation_When_APathThatResolvesToNothingIsNavigatedTo_Then_TheRouterStillDescribesThatLocation()
        {
            // The case above is refused by the match; this one is refused before it, by a separate branch.
            // Arrange
            var router = BuildRouter("/home", _routes);
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateSync(null);

            // Assert
            Assert.That(
                $"result={result} path={router.CurrentLocation?.Path} state={router.Navigation.State} "
                + $"published={string.Join(",", published)}",
                Is.EqualTo("result=NotFound path=/home state=Idle published="));
        }

        [Test]
        public void Given_ASubscriberNavigatingWhenACommitGoesIdle_When_ItsNavigationCommits_Then_ItsLocationIsTheLastAnnounced()
        {
            // The subscriber's navigation commits inside the outer commit's Idle event, so the outer one's
            // location is announced, if at all, after it.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"), Route("x"));
            var announced = new List<string>();
            router.OnLocationChanged += location => announced.Add(location.Path);
            var navigated = false;
            router.OnNavigationChanged += navigation =>
            {
                if (navigation.State != NavigationLifecycle.Idle || navigated) return;
                navigated = true;
                router.NavigateSync("/x");
            };

            // Act
            router.NavigateSync("/about");

            // Assert
            Assert.That(
                $"current={router.CurrentLocation?.Path} last={(announced.Count == 0 ? "none" : announced[announced.Count - 1])}",
                Is.EqualTo("current=/x last=/x"));
        }

        [Test]
        public void Given_ALocationSubscriberThatNavigates_When_ItsNavigationCommits_Then_TheSubscriberAfterItIsLeftOnTheNewerLocation()
        {
            // The first subscriber's navigation commits inside the announcement of /about, before the second
            // subscriber has been handed /about.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"), Route("x"));
            var navigated = false;
            router.OnLocationChanged += location =>
            {
                if (location.Path != "/about" || navigated) return;
                navigated = true;
                router.NavigateSync("/x");
            };
            var received = new List<string>();
            router.OnLocationChanged += location => received.Add(location.Path);

            // Act
            router.NavigateSync("/about");

            // Assert
            Assert.That(
                $"current={router.CurrentLocation?.Path} last={(received.Count == 0 ? "none" : received[received.Count - 1])}",
                Is.EqualTo("current=/x last=/x"));
        }

        [Test]
        public void Given_ALocationSubscriberThatRemovesItselfAndTheNextOne_When_ALocationIsAnnounced_Then_BothHearOnlyThatAnnouncement()
        {
            // As with a multicast delegate, a removal reaches the announcements after the one it is made in.
            // The first subscriber is the first registration, so its own removal is of the list's head.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"), Route("x"));
            var received = new List<string>();
            Action<RouterLocation> second = location => received.Add("second:" + location.Path);
            Action<RouterLocation> first = null;
            first = location =>
            {
                received.Add("first:" + location.Path);
                router.OnLocationChanged -= second;
                router.OnLocationChanged -= first;
            };
            router.OnLocationChanged += first;
            router.OnLocationChanged += second;

            // Act
            router.NavigateSync("/about");
            router.NavigateSync("/x");

            // Assert
            Assert.That(string.Join(",", received), Is.EqualTo("first:/about,second:/about"));
        }

        [Test]
        public void Given_ALocationSubscriberThatAddsAnother_When_LocationsAreAnnounced_Then_TheAddedOneHearsFromTheNextAnnouncement()
        {
            // As with a multicast delegate, an addition reaches the announcements after the one it is made in.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"), Route("x"));
            var received = new List<string>();
            Action<RouterLocation> added = location => received.Add(location.Path);
            var adding = true;
            router.OnLocationChanged += _ =>
            {
                if (!adding) return;
                adding = false;
                router.OnLocationChanged += added;
            };

            // Act
            router.NavigateSync("/about");
            router.NavigateSync("/x");

            // Assert
            Assert.That(string.Join(",", received), Is.EqualTo("/x"));
        }

        [Test]
        public void Given_ARouterNobodyHasSubscribedTo_When_AHandlerIsRemovedFromIt_Then_ItStillNavigates()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.OnLocationChanged -= _ => { };
            var result = router.NavigateSync("/home");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_ASubscriberNavigatingWhenACommitGoesIdle_When_ItsNavigationIsStillLoading_Then_TheCommittedLocationIsAnnounced()
        {
            // The subscriber's navigation parks on its loader, so the location the outer commit landed is the
            // one on show while it loads.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"),
                Route("slow", loader: (ctx, ct) => new VelvetTaskCompletionSource<object>().Task));
            var announced = new List<string>();
            router.OnLocationChanged += location => announced.Add(location.Path);
            var navigated = false;
            router.OnNavigationChanged += navigation =>
            {
                if (navigation.State != NavigationLifecycle.Idle || navigated) return;
                navigated = true;
                router.NavigateAsync("/slow").Forget();
            };

            // Act
            router.NavigateSync("/about");

            // Assert
            Assert.That(
                $"announced={string.Join(",", announced)} loading={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("announced=/about loading=/slow"));
        }

        [Test]
        public void Given_ADisposedRouter_When_NavigatingToAnUnmatchedPath_Then_ItIsCancelledWithoutPublishingANavigation()
        {
            // Arrange
            var router = BuildRouter("/home", _routes);
            router.Dispose();
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateSync("/nonexistent");

            // Assert
            Assert.That($"result={result} published={string.Join(",", published)}", Is.EqualTo("result=Cancelled published="),
                "A disposed router refuses the navigation rather than reporting that no route matched");
        }

        [Test]
        public void Given_ADisposedRouter_When_NavigatingToAPathThatResolvesToNothing_Then_ItIsCancelledWithoutPublishingANavigation()
        {
            // Arrange
            var router = BuildRouter("/home", _routes);
            router.Dispose();
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateSync(null);

            // Assert
            Assert.That($"result={result} published={string.Join(",", published)}", Is.EqualTo("result=Cancelled published="),
                "A disposed router refuses the navigation rather than reporting that the path resolved to nothing");
        }

        #endregion

        #region Await loader

        [Test]
        public void Given_AwaitLoader_When_Navigating_Then_ReturnsSuccess()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("data", loader: (ctx, ct) => VelvetTask.FromResult((object)"loaded")),
            });

            // Act
            var result = router.NavigateSync("/data");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_AwaitLoader_When_Navigating_Then_LoaderDataIsCommittedKeyedByRouteId()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("data", loader: (ctx, ct) => VelvetTask.FromResult((object)"loaded")),
            });

            // Act
            router.NavigateSync("/data");

            // Assert
            Assert.That(router.GetLoaderData("/data"), Is.EqualTo("loaded"));
        }

        #endregion

        #region History

        [Test]
        public void Given_TwoNavigations_When_OnLatest_Then_CanGoBack()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.NavigateSync("/home");
            router.NavigateSync("/about");

            // Assert
            Assert.That(router.CanGoBack, Is.True);
        }

        [Test]
        public void Given_TwoNavigations_When_GoBack_Then_RestoresPreviousLocation()
        {
            // Arrange
            var router = new Router(_routes);
            router.NavigateSync("/home");
            router.NavigateSync("/about");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/home"));
        }

        // GREEN_ON_BASE(characterization): the base appends a first navigation's Replace entry this way too.
        // The branch rewrites the line that appends it, from a cached entry to the bare path.
        [Test]
        public void Given_AReplaceAsTheFirstNavigation_When_APushFollowsAndGoesBack_Then_ItLandsOnTheReplacedEntry()
        {
            // Arrange
            var router = new Router(_routes);
            router.NavigateAsync("/home", NavigationMode.Replace).GetAwaiter().GetResult();
            router.NavigateSync("/about");

            // Act
            var result = router.GoBackSync();

            // Assert
            Assert.That($"result={result} path={router.CurrentLocation?.Path}",
                Is.EqualTo("result=Success path=/home"));
        }

        [Test]
        public void Given_GoneBack_When_OnEarlierEntry_Then_CanGoForward()
        {
            // Arrange
            var router = new Router(_routes);
            router.NavigateSync("/home");
            router.NavigateSync("/about");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CanGoForward, Is.True);
        }

        [Test]
        public void Given_AtHistoryStart_When_GoBack_Then_ReturnsCancelled()
        {
            // Arrange
            var router = new Router(_routes);
            router.NavigateSync("/home");
            Assume.That(router.CanGoBack, Is.False, "Precondition: at the start of history");

            // Act
            var result = router.GoBackSync();

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Cancelled));
        }

        [Test]
        public void Given_AtHistoryEnd_When_GoForward_Then_ReturnsCancelled()
        {
            // Arrange
            var router = new Router(_routes);
            router.NavigateSync("/home");
            Assume.That(router.CanGoForward, Is.False, "Precondition: at the end of history");

            // Act
            var result = router.GoForwardSync();

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Cancelled));
        }

        [Test]
        public void Given_ABackModeNavigationAtTheFirstEntry_When_Requested_Then_ItIsRefusedWithoutTouchingHistory()
        {
            // GoBack refuses this before starting, so only a direct NavigateAsync reaches the step itself. The
            // route redirects because that is what turns an out-of-range step from a throw into silent
            // corruption: the redirect's Replace finds no slot to overwrite and appends instead, leaving the
            // index pointing at an entry that is no longer the last one.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"), Route("guarded", guard: _ => "/target"), Route("target"));
            Assume.That(router.CanGoBack, Is.False, "Precondition: the start entry is the only one on the stack");

            // Act
            var result = router.NavigateAsync("/guarded", NavigationMode.Back).GetAwaiter().GetResult();

            // Assert
            Assert.That($"result={result} paths={RouterHistoryProbe.PathsOf(router)}",
                Is.EqualTo("result=Cancelled paths=/home"));
        }

        [Test]
        public void Given_ABackModeNavigationAtTheFirstEntry_When_Requested_Then_TheRouterStaysOnTheLocationItIsOn()
        {
            // GoBack refuses the same step without publishing a navigation. Reaching the refusal partway
            // through the navigation instead would announce one that was never going to commit.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"));
            Assume.That(router.CanGoBack, Is.False, "Precondition: the start entry is the only one on the stack");
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateAsync("/about", NavigationMode.Back).GetAwaiter().GetResult();

            // Assert
            Assert.That($"result={result} published={string.Join(",", published)}",
                Is.EqualTo("result=Cancelled published="),
                "A refused step leaves the router as the convenience wrapper leaves it");
        }

        [Test]
        public void Given_AForwardModeNavigationAtTheLastEntry_When_Requested_Then_TheRouterIsNotLeftInFlight()
        {
            // The mirror of the Back case, and the one that throws rather than corrupts when the refusal goes
            // missing: the redirect the Guard produces resolves the same out-of-range slot, and at this end of
            // the stack the commit writes into it instead of appending past it.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"), Route("guarded", guard: _ => "/target"), Route("target"));
            Assume.That(router.CanGoForward, Is.False, "Precondition: the start entry is the last on the stack");
            var published = new List<NavigationLifecycle>();
            router.OnNavigationChanged += navigation => published.Add(navigation.State);

            // Act
            var result = router.NavigateAsync("/guarded", NavigationMode.Forward).GetAwaiter().GetResult();

            // Assert
            Assert.That($"result={result} published={string.Join(",", published)}",
                Is.EqualTo("result=Cancelled published="));
        }

        #endregion

        #region History length

        [Test]
        public void Given_ManyPushes_When_WalkingBackToStart_Then_TheFirstEntryIsStillThere()
        {
            // Arrange
            const int pushes = 120;
            var router = new Router(new[] { Route(":page") });
            for (var i = 1; i <= pushes; i++)
            {
                router.NavigateSync($"/p{i}");
            }

            // Act
            // Bound the walk so a broken CanGoBack cannot hang the fixture.
            var steps = 0;
            while (router.CanGoBack && steps++ < pushes * 2)
            {
                router.GoBackSync();
            }

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/p1"));
        }

        #endregion

        #region Loaders on Back/Forward

        [Test]
        public void Given_LoadedRoute_When_GoBackThenGoForward_Then_TheLoaderRunsAgain()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("data", loader: (ctx, ct) => VelvetTask.FromResult((object)$"loaded-{++loaderCallCount}")),
                }),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/data");

            // Act
            router.GoBackSync();
            router.GoForwardSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(2), "A Forward step runs the route's loader as a Push does");
        }

        [Test]
        public void Given_LoadedRoute_When_GoBackThenGoForward_Then_TheLoaderDataIsTheNewRunsResult()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("data", loader: (ctx, ct) => VelvetTask.FromResult((object)$"loaded-{++loaderCallCount}")),
                }),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/data");

            // Act
            router.GoBackSync();
            router.GoForwardSync();

            // Assert
            Assert.That(router.GetLoaderData("/data"), Is.EqualTo("loaded-2"));
        }

        [Test]
        public void Given_LoadedRoute_When_GoBackToIt_Then_TheLoaderRunsAgain()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("page1", loader: (ctx, ct) => VelvetTask.FromResult((object)$"page1-{++loaderCallCount}")),
                    Route("page2"),
                }),
            });
            router.NavigateSync("/page1");
            router.NavigateSync("/page2");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(2), "A Back step runs the route's loader as a Push does");
        }

        [Test]
        public void Given_LoadedRoute_When_GoBackToIt_Then_TheLoaderDataIsTheNewRunsResult()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("page1", loader: (ctx, ct) => VelvetTask.FromResult((object)$"page1-{++loaderCallCount}")),
                    Route("page2"),
                }),
            });
            router.NavigateSync("/page1");
            router.NavigateSync("/page2");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.GetLoaderData("/page1"), Is.EqualTo("page1-2"));
        }

        [Test]
        public void Given_ErroredRoute_When_NavigatingAway_Then_ErrorMapIsCleared()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("boom", loader: (ctx, ct) => throw new InvalidOperationException("boom-error")),
                    Route("safe"),
                }),
            });
            router.NavigateSync("/boom");
            Assume.That(router.CurrentLoaderErrors.Count, Is.EqualTo(1), "Precondition: the errored route recorded its error");

            // Act
            router.NavigateSync("/safe");

            // Assert
            Assert.That(router.CurrentLoaderErrors, Is.Empty);
        }

        [Test]
        public void Given_ErroredRouteLeftAndReturned_When_GoBackToIt_Then_TheLoaderRunsAgain()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("boom", loader: (ctx, ct) =>
                    {
                        loaderCallCount++;
                        throw new InvalidOperationException("boom-error");
                    }),
                    Route("safe"),
                }),
            });
            router.NavigateSync("/boom");
            router.NavigateSync("/safe");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(2), "A Back step onto a route whose loader failed runs it again");
        }

        [Test]
        public void Given_ErroredRouteLeftAndReturned_When_GoBackToIt_Then_TheNewRunsErrorIsPresented()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("boom", loader: (ctx, ct) => throw new InvalidOperationException($"boom-{++loaderCallCount}")),
                    Route("safe"),
                }),
            });
            router.NavigateSync("/boom");
            router.NavigateSync("/safe");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLoaderErrors["/boom"].Message, Is.EqualTo("boom-2"));
        }

        #endregion

        #region Loader data snapshot

        [UnityTest]
        public IEnumerator Given_ACallerHoldingCurrentLoaderData_When_ASuspendLoaderResolvesLate_Then_TheHeldDictionaryIsUnchanged()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The count is read on both sides of the resolution rather than only after it, because a snapshot
            // that was already populated when it was handed out would satisfy an after-only reading of an
            // unchanged one.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            router.NavigateSync("/deferred");
            var held = router.CurrentLoaderData;
            var countWhenHandedOut = held.Count;

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That($"handedOut={countWhenHandedOut} afterResolution={held.Count}",
                Is.EqualTo("handedOut=0 afterResolution=0"),
                "A dictionary published as read-only must not gain entries under whoever is holding it");
        });

        #endregion

        #region Events

        [Test]
        public void Given_LocationChangedSubscriber_When_Navigating_Then_FiresWithCommittedLocation()
        {
            // Arrange
            var router = new Router(_routes);
            RouterLocation receivedLocation = null;
            router.OnLocationChanged += loc => receivedLocation = loc;

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(receivedLocation?.Path, Is.EqualTo("/home"));
        }

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolves_When_AfterCommit_Then_ReEmitsLocationOnce()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            var emitCount = 0;
            router.OnLocationChanged += _ => emitCount++;
            router.NavigateSync("/deferred");
            Assume.That(emitCount, Is.EqualTo(1), "Precondition: navigation emitted once");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "Suspend completion re-emits OnLocationChanged exactly once more");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolves_When_AfterCommit_Then_ReEmitsWithFreshIdentity()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The canonical location Provider bails on a referentially-equal value, so reusing the same
            // instance would drop the re-render; the re-emit must carry a fresh RouterLocation identity.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            RouterLocation lastEmitted = null;
            router.OnLocationChanged += loc => lastEmitted = loc;
            router.NavigateSync("/deferred");
            var navigationLocation = router.CurrentLocation;
            Assume.That(lastEmitted, Is.SameAs(navigationLocation), "Precondition: navigation emitted its own instance");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(lastEmitted, Is.Not.SameAs(navigationLocation),
                "The re-emit carries a fresh instance the location Provider will not bail");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolves_When_AfterCommit_Then_PreservesLocationContent()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            RouterLocation lastEmitted = null;
            router.OnLocationChanged += loc => lastEmitted = loc;
            router.NavigateSync("/deferred");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(lastEmitted.Path, Is.EqualTo("/deferred"), "Location content is preserved across the re-emit");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFails_When_AfterCommit_Then_ReEmitsLocationOnce()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            var emitCount = 0;
            router.OnLocationChanged += _ => emitCount++;
            router.NavigateSync("/deferred");
            Assume.That(emitCount, Is.EqualTo(1), "Precondition: navigation emitted once");
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("deferred-failure"));

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "Suspend failure re-emits OnLocationChanged exactly once more");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFails_When_AfterCommit_Then_ReEmitsWithFreshIdentity()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A fresh identity is required so UseRouteError consumers re-render on the deferred failure.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                }),
            });
            RouterLocation lastEmitted = null;
            router.OnLocationChanged += loc => lastEmitted = loc;
            router.NavigateSync("/deferred");
            var navigationLocation = router.CurrentLocation;
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("deferred-failure"));

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(lastEmitted, Is.Not.SameAs(navigationLocation));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_DoesNotReEmit()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A navigated-away loader's late result belongs to a route that is no longer current, so the router
            // discards it without re-emitting the unrelated current location.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            var emitCount = 0;
            router.OnLocationChanged += _ => emitCount++;
            router.NavigateSync("/deferred");
            router.NavigateSync("/other");
            Assume.That(emitCount, Is.EqualTo(2), "Precondition: each navigation emitted once");

            // Act
            tcs.TrySetResult("stale-deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "A navigated-away Suspend loader's late resolution does not re-emit");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_CurrentLocationIdentityIsUnchanged()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            router.NavigateSync("/deferred");
            router.NavigateSync("/other");
            var otherLocation = router.CurrentLocation;

            // Act
            tcs.TrySetResult("stale-deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLocation, Is.SameAs(otherLocation),
                "A stale resolution leaves the current location identity unchanged, so consumers do not churn");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_LoaderDataNotPolluted()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A navigated-away Suspend loader's late result belongs to a superseded round; it must not land in
            // the live loader data of the unrelated current location, where UseLoaderData / GetLoaderData read it.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            router.NavigateSync("/deferred");
            router.NavigateSync("/other");
            Assume.That(router.CurrentLoaderData, Is.Empty, "Precondition: /other commits with no loader data");

            // Act
            tcs.TrySetResult("stale-deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderData, Is.Empty,
                "A navigated-away Suspend loader's late result does not pollute the current location's loader data");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFailsAfterNavigateAway_When_Failing_Then_ErrorNotRecorded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A navigated-away Suspend loader's late failure belongs to a superseded round; it must not record
            // an error under the unrelated current location nor surface via UseRouteError.
            // Arrange
            var tcs = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            router.NavigateSync("/deferred");
            router.NavigateSync("/other");
            Assume.That(router.CurrentLoaderErrors, Is.Empty, "Precondition: /other commits with no loader errors");

            // Act
            tcs.TrySetException(new InvalidOperationException("stale-deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderErrors, Is.Empty,
                "A navigated-away Suspend loader's late failure does not record an error under the current location");
        });

        [UnityTest]
        public IEnumerator Given_InFlightSuspendLoader_When_BackReturnsToAnEntryOfTheSameRoute_Then_ItsLateResultIsDropped()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Both entries match the same route pattern, so they share a RouteId and nothing downstream of the
            // runner can tell the late result apart from the one the Back's own run produced.
            // Arrange
            var first = new VelvetTaskCompletionSource<object>();
            var second = new VelvetTaskCompletionSource<object>();
            var loaderCalls = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users/:id", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) => Interlocked.Increment(ref loaderCalls) switch
                        {
                            1 => first.Task,
                            2 => second.Task,
                            _ => VelvetTask.FromResult((object)"user-1-again"),
                        }),
                }),
            });
            router.NavigateSync("/users/1");
            first.TrySetResult("user-1");
            await VelvetTask.Yield();
            router.NavigateSync("/users/2");
            router.GoBackSync();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/users/1"),
                "Precondition: the Back committed the earlier entry");

            // Act
            second.TrySetResult("user-2");
            await VelvetTask.Yield();

            // Assert
            Assert.That(string.Join(",", router.CurrentLoaderData.Values), Is.EqualTo("user-1-again"),
                "A Back supersedes the round it left, so that round's late result is dropped");
        });

        [Test]
        public void Given_ASuspendLoaderHandedACompletedTask_When_ItResolvesBeforeTheCommit_Then_ItsResultIsStillCommitted()
        {
            // A loader is free to answer immediately, and one that does resolves while its own round is still
            // running rather than after the navigation that started it.
            // Arrange
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("ready", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) => VelvetTask.FromResult((object)"ready-data")),
                }),
            });

            // Act
            router.NavigateSync("/ready");

            // Assert
            Assert.That(router.GetLoaderData("/ready"), Is.EqualTo("ready-data"),
                "A result produced before the commit belongs to the location that commit establishes");
        }

        #endregion

        #region Router.Current

        [Test]
        public void Given_TwoRoutersConstructed_When_TheLaterIsDisposed_Then_CurrentIsTheEarlier()
        {
            // Arrange
            using var earlier = new Router(new[] { Route("/") });
            var later = new Router(new[] { Route("/") });

            // Act
            later.Dispose();

            // Assert
            Assert.That(ReferenceEquals(Router.Current, earlier), Is.True);
        }

        [Test]
        public void Given_ThreeRoutersConstructed_When_TheMiddleThenTheLatestAreDisposed_Then_CurrentIsTheFirst()
        {
            // Arrange
            using var first = new Router(new[] { Route("/") });
            var middle = new Router(new[] { Route("/") });
            var latest = new Router(new[] { Route("/") });
            middle.Dispose();

            // Act
            latest.Dispose();

            // Assert
            Assert.That(ReferenceEquals(Router.Current, first), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base leaves Router.Current alone this way too.
        // The change rewrites the property's documentation to say so.
        [Test]
        public void Given_TwoRoutersConstructed_When_TheEarlierIsDisposed_Then_CurrentIsStillTheLater()
        {
            // Arrange
            var earlier = new Router(new[] { Route("/") });
            using var later = new Router(new[] { Route("/") });

            // Act
            earlier.Dispose();

            // Assert
            Assert.That(ReferenceEquals(Router.Current, later), Is.True);
        }

        private static void ForgetEveryRouter() => typeof(Router).GetMethod("ForgetEveryRouter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null);

        // GREEN_ON_BASE(characterization): the base names no router once the newest is disposed.
        [Test]
        public void Given_TheOnlyTwoRoutersRegistered_When_BothAreDisposed_Then_CurrentNamesNone()
        {
            // Arrange — the registry starts empty, so the earlier router is its first entry.
            ForgetEveryRouter();
            var earlier = new Router(new[] { Route("/") });
            var later = new Router(new[] { Route("/") });
            earlier.Dispose();

            // Act
            later.Dispose();

            // Assert
            Assert.That(Router.Current, Is.Null);
        }

        [Test]
        public void Given_RoutersConstructedAndDisposedInTurn_When_AnotherIsConstructed_Then_TheRegistryHoldsOnlyIt()
        {
            // Arrange
            ForgetEveryRouter();
            for (var i = 0; i < 3; i++)
            {
                new Router(new[] { Route("/") }).Dispose();
            }

            // Act
            using var live = new Router(new[] { Route("/") });

            // Assert
            var registry = typeof(Router).GetField("s_constructed",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
            Assert.That((registry as System.Collections.ICollection)?.Count ?? -1, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base clamps a route level past the matched chain the same way.
        [Test]
        public void Given_ARouteLevelPastTheMatchedChain_When_NavigatingToTheCurrentRoute_Then_ItAnchorsAtTheLeaf()
        {
            // Arrange
            var router = BuildRouter("/users/7", Route("/", children: new[] { Route("users/:id") }));

            // Act
            router.NavigateAsync(".", NavigationMode.Push, baseRouteIndex: 9).GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/users/7"));
        }

        [Test]
        public void Given_ALiveRouter_When_TheSubsystemsAreRegisteredAgain_Then_CurrentNamesNone()
        {
            // Arrange — what Unity calls entering Play Mode without a domain reload.
            using var router = new Router(new[] { Route("/") });
            var reset = typeof(Router).GetMethod("ForgetEveryRouter",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            reset?.Invoke(null, null);

            // Assert
            Assert.That(Router.Current, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base references only the newest router, so an earlier one
        // nothing else holds is collectable there too.
        [Test]
        public void Given_RoutersNobodyDisposedOrHolds_When_ALaterOneIsDisposed_Then_TheyCanBeCollected()
        {
            // Arrange
            var earlier = ConstructUnheldRouters(32);
            new Router(new[] { Route("/") }).Dispose();

            // Act
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert
            Assert.That(earlier.FindAll(router => router.IsAlive).Count, Is.LessThan(earlier.Count / 2));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static List<WeakReference> ConstructUnheldRouters(int count)
        {
            var routers = new List<WeakReference>();
            for (var i = 0; i < count; i++)
            {
                routers.Add(new WeakReference(new Router(new[] { Route("/") })));
            }
            return routers;
        }

        #endregion

        #region ScopeFactory

        [Test]
        public void Given_ScopeFactorySupplied_When_Constructed_Then_ExposesIt()
        {
            // Arrange
            var factory = new TestRouteScopeFactory();

            // Act
            var router = new Router(_routes, factory);

            // Assert
            Assert.That(router.ScopeFactory, Is.SameAs(factory));
        }

        [Test]
        public void Given_NoScopeFactory_When_Constructed_Then_ScopeFactoryIsNull()
        {
            // Arrange

            // Act
            var router = new Router(_routes);

            // Assert
            Assert.That(router.ScopeFactory, Is.Null);
        }

        #endregion

        #region Concurrent navigation cancellation

        // GREEN_ON_BASE(characterization): the base returns Cancelled for a cancelled Back the same way.
        // This names the case for the step rather than for the history cache the change deletes.
        [UnityTest]
        public IEnumerator Given_CancelledToken_When_GoBack_Then_ReturnsCancelled()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await router.GoBack(cts.Token);

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Cancelled));
        });

        // GREEN_ON_BASE(refactor): the base unwinds a cancelled Back the same way before its loaders.
        // The branch moves that check out of the blocker phase, which it removes from there, and no
        // history cache is left for the Back to hit.
        [UnityTest]
        public IEnumerator Given_CancelledToken_When_GoBack_Then_NothingIsLeftInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await router.GoBack(cts.Token);

            // Assert
            Assert.That(
                $"result={result} state={router.Navigation.State} pending={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("result=Cancelled state=Idle pending=none"));
        });

        // GREEN_ON_BASE(characterization): the base leaves a cancelled Back uncommitted the same way.
        // This names the case for the step rather than for the history cache the change deletes.
        [UnityTest]
        public IEnumerator Given_CancelledToken_When_GoBack_Then_DoesNotCommitLocation()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            await router.GoBack(cts.Token);

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "A cancelled Back does not commit the previous entry");
        });

        // GREEN_ON_BASE(refactor): the takeover this pins is unchanged by the branch; the attempt it takes over
        // from parks on an Await loader, where it parked on an async Blocker, which the branch removes.
        [UnityTest]
        public IEnumerator Given_ACancellationCallbackThatNavigatesDuringATakeover_When_BothNavigationsFinish_Then_OnlyTheOneStartedLastCommits()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The navigation is read while the later one is still loading, the window the one taking over
            // would otherwise report into.
            // Arrange
            var lastLoader = new VelvetTaskCompletionSource<object>();
            var parked = new VelvetTaskCompletionSource<object>();
            Router router = null;
            router = BuildRouter("/home",
                Route("home"),
                Route("away", loader: (ctx, ct) =>
                {
                    ct.Register(() => router.NavigateAsync("/last").Forget());
                    return parked.Task;
                }),
                Route("takeover"),
                Route("last", loader: (ctx, ct) => lastLoader.Task));
            router.NavigateAsync("/away").Forget();

            // Act
            var takeover = await router.NavigateAsync("/takeover");
            var whileLastLoads = $"{router.Navigation.State} {router.Navigation.Location?.Path ?? "none"}";
            lastLoader.TrySetResult("last-data");
            await VelvetTask.Yield();
            var landed = $"{router.CurrentLocation?.Path} forward={router.CanGoForward}";
            await router.GoBack();
            var behind = $"{router.CurrentLocation?.Path} back={router.CanGoBack}";

            // Assert
            Assert.That($"takeover={takeover} while={whileLastLoads} landed={landed} behind={behind}",
                Is.EqualTo("takeover=Cancelled while=Loading /last landed=/last forward=False behind=/home back=False"),
                "A navigation started from inside a takeover supersedes the navigation taking over");
        });

        // GREEN_ON_BASE(characterization): the base suppresses no execution-context flow, so a caller's own
        // suppression has nothing to collide with there.
        [Test]
        public void Given_ACallerThatHasSuppressedExecutionContextFlow_When_ItNavigates_Then_TheNavigationCommits()
        {
            // Arrange
            var router = BuildRouter("/home", _routes);
            NavigationResult result;

            // Act
            using (ExecutionContext.SuppressFlow())
            {
                result = router.NavigateSync("/about");
            }

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        // GREEN_ON_BASE(characterization): the base unlinks a finished navigation by disposing its source.
        [UnityTest]
        public IEnumerator Given_ACommittedRoutesSuspendLoader_When_TheTokenItsNavigationWasGivenIsCancelled_Then_TheLoadersTokenIsNot()
            => VelvetTask.ToCoroutine(async () =>
        {
            // CanBeCanceled is folded in because a loader that never ran leaves a token reading not-cancelled too.
            // Arrange
            using var caller = new CancellationTokenSource();
            CancellationToken loadersToken = default;
            var router = BuildRouter("/home",
                Route("home"),
                Route("feed", loaderMode: LoaderMode.Suspend, loader: (ctx, ct) =>
                {
                    loadersToken = ct;
                    return new VelvetTaskCompletionSource<object>().Task;
                }));
            await router.NavigateAsync("/feed", cancellationToken: caller.Token);

            // Act
            caller.Cancel();

            // Assert
            Assert.That($"{loadersToken.CanBeCanceled} {ReadTokenState(loadersToken)}", Is.EqualTo("True not-cancelled"),
                "A navigation's caller token stops reaching it once the navigation has ended");
        });

        #endregion
    }
}
