using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a navigation that has not finished may leave in the router's shared state — the history
    /// list, the history index, and <see cref="RouterStatus"/> — while a second navigation reads it.
    /// <list type="bullet">
    /// <item>An attempt parked in a Blocker leaves the index and the history list describing the entry the
    /// user is still on, so a navigation started meanwhile builds on that entry rather than on the parked
    /// attempt's destination.</item>
    /// <item>A Back or Forward resolves the slot it will commit into before those phases run, so a Push
    /// committing meanwhile can take that slot over. Such an attempt commits nothing at all: the Blocker
    /// check refuses a superseded attempt before the loader phase reads the slot.</item>
    /// <item>A Guard redirect appends nothing before its target commits, so an attempt that never gets there
    /// leaves no entry for a path the user did not arrive at.</item>
    /// <item>A navigation that dies on an exception leaves no in-flight <see cref="RouterStatus"/> behind,
    /// whether it died in a phase or in the commit that follows them.</item>
    /// <item>A history entry committed while a Suspend loader is still running is not served from the
    /// Back/Forward cache, since the snapshot it holds is not the data the route asked for. Stepping onto it
    /// runs the loaders again, and once a round of them finishes the entry is servable, including when they
    /// all finished before that step committed.</item>
    /// <item>An attempt cancelled by a loader that navigated leaves the loader data and the status of the
    /// location that navigation committed, rather than clearing state it never owned.</item>
    /// </list>
    /// </summary>
    // Bounded for the cases here that await a blocker stub's Entered signal;
    // RouteTestStubs.MakeOneShotBlocker states what an unbounded fixture costs.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouterUnfinishedNavigationTests
    {
        // Same isolation rule as RouterTests: Router.Current is a global that each new Router() overwrites.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [UnityTest]
        public IEnumerator Given_ABackParkedInABlocker_When_APushCommitsMeanwhile_Then_TheEntryTheUserIsOnSurvives()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The parked Back is the one navigation that could have removed /settings: a Push truncates the
            // forward entries above the index it reads, and the entry the user is on sits above the parked
            // Back's destination.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"), Route("about"), Route("settings"), Route("contact"),
                }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/settings");
            var (check, entered, _, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var parked = router.GoBack();
            await entered.Task;
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/settings"),
                "Precondition: the Back is still parked in the blocker and has committed nothing");

            // Act
            await router.NavigateAsync("/contact");

            // Assert
            Assert.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home,/about,/settings,/contact"),
                "A navigation that has not committed must not move the position a later Push builds on");
        });

        [UnityTest]
        public IEnumerator Given_AForwardParkedInABlocker_When_APushTakesOverItsSlot_Then_ItCommitsNothing()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The Push truncates the forward entries, so the slot the parked Forward resolved onto holds the
            // pushed entry by the time the blocker releases.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"), Route("about"), Route("settings"), Route("contact"),
                }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/settings");
            await router.GoBack();
            var (check, entered, _, resumeUnblocked) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var parked = router.GoForward();
            await entered.Task;
            await router.NavigateAsync("/contact");
            Assume.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home,/about,/contact"),
                "Precondition: the Push truncated the entry the parked Forward had resolved onto");

            // Act
            resumeUnblocked();
            var result = await parked;

            // Assert
            Assert.That(
                $"result={result} at={router.CurrentLocation?.Path} history={RouterHistoryProbe.PathsOf(router)}",
                Is.EqualTo("result=Cancelled at=/contact history=/home,/about,/contact"),
                "A step whose destination is gone must land nowhere rather than write itself into the slot "
                + "that destination used to occupy");
        });

        [UnityTest]
        public IEnumerator Given_ARedirectParkedInABlocker_When_ANewerNavigationCommits_Then_NoEntryForTheRedirectingPathRemains()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The redirect's own target parks, so the originating path is as far as the attempt ever got. An
            // entry for it would sit in the stack for the rest of the session, and a Back onto it would re-run
            // the guard and land on the redirect target instead.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => "/target"),
                    Route("target"),
                    Route("x"),
                }));
            var (check, entered, _, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var parked = router.NavigateAsync("/guarded");
            await entered.Task;

            // Act
            await router.NavigateAsync("/x");

            // Assert
            Assert.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home,/x"),
                "A redirect that never reached its target must leave no entry for the path it started from");
        });

        [UnityTest]
        public IEnumerator Given_AGuardThatThrows_When_TheExceptionReachesTheCaller_Then_TheRouterIsNoLongerInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A Guard is an application delegate invoked with nothing between it and the caller. Leaving the
            // in-flight Status behind would make every UseNavigation consumer render its pending branch for
            // the rest of the session, including components mounted after the throw.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("boom", guard: _ => throw new InvalidOperationException("guard-boom")),
                }));
            Exception caught = null;

            // Act
            try
            {
                await router.NavigateAsync("/boom");
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That($"threw={caught != null} status={router.Status}", Is.EqualTo("threw=True status=Error"),
                "A navigation that died on an exception reports the failure and stops reporting itself as in flight");
        });

        [UnityTest]
        public IEnumerator Given_ARedirectTargetDeclaringBothRedirectToAndGuard_When_ItThrows_Then_TheOriginatingPathIsNotRecorded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The mutual-exclusion throw comes from inside the redirect target's own navigation, so the
            // originating path is the one an aborted attempt could leave on the stack.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("start", guard: _ => "/broken"),
                    Route("broken", redirectTo: "/home", guard: _ => null),
                }));

            // Act
            try
            {
                await router.NavigateAsync("/start");
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home"),
                "A navigation that threw before committing leaves the history as it found it");
        });

        [UnityTest]
        public IEnumerator Given_ACommitThatThrows_When_TheExceptionReachesTheCaller_Then_TheRouterIsNoLongerInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The commit is the last thing a navigation does and the only step after the phases, so an
            // exception raised there is the one an unwind handler placed around the phases alone would miss.
            // A mode outside the enum is what reaches it: every defined mode has a commit branch.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            Exception caught = null;

            // Act
            try
            {
                await router.NavigateAsync("/other", (NavigationMode)int.MaxValue);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That($"threw={caught != null} status={router.Status}", Is.EqualTo("threw=True status=Error"),
                "A navigation that died in its commit stops reporting itself as in flight, like one that died earlier");
        });

        [UnityTest]
        public IEnumerator Given_AnEntryCommittedWhileItsSuspendLoaderRuns_When_GoingBackToIt_Then_TheLoaderRunsAgain()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The entry's snapshot is empty because the loader never resolved, which is indistinguishable by
            // content from a route whose loaders legitimately produced nothing.
            // Arrange
            var loaderCalls = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    // A source per call, not one shared: a VelvetTask carries a single awaiter, so handing
                    // the same one back on the re-run throws where the first run is still parked on it.
                    Route("users/:id", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) =>
                        {
                            Interlocked.Increment(ref loaderCalls);
                            return new VelvetTaskCompletionSource<object>().Task;
                        }),
                    Route("other"),
                }),
            });
            await router.NavigateAsync("/users/1");
            await router.NavigateAsync("/other");
            Assume.That(loaderCalls, Is.EqualTo(1), "Precondition: the loader ran once and never resolved");

            // Act
            await router.GoBack();

            // Assert
            Assert.That(loaderCalls, Is.EqualTo(2),
                "A snapshot taken before the loaders finished is not the data the route asked for, so it is not cached");
        });

        [UnityTest]
        public IEnumerator Given_AStepOntoAnUnsettledEntryWhoseSuspendLoaderAnswersImmediately_When_TheEntryIsSteppedOntoAgain_Then_ItIsServedFromTheCache()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A loader handed an already-complete task resolves inside the run that launched it, so its
            // completion reaches the router before this step has a location to write it under and is refused.
            // The commit is then the only thing left that can mark the entry finished.
            // Arrange
            var unresolved = new VelvetTaskCompletionSource<object>();
            var loaderCalls = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users/:id", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) => Interlocked.Increment(ref loaderCalls) == 1
                            ? unresolved.Task
                            : VelvetTask.FromResult((object)"user-data")),
                    Route("other"),
                }),
            });
            await router.NavigateAsync("/users/1");
            await router.NavigateAsync("/other");
            await router.GoBack();

            // Act
            await router.NavigateAsync("/other");
            await router.GoBack();

            // Assert
            Assert.That(
                $"calls={loaderCalls} data={string.Join(",", router.CurrentLoaderData.Values)}",
                Is.EqualTo("calls=2 data=user-data"),
                "A round that finished before its step committed leaves the entry servable, so the next step "
                + "onto it reads the cache rather than running the loader again");
        });

        [Test]
        public void Given_ALoaderThatNavigates_When_TheCancelledAttemptUnwinds_Then_TheCommittedLocationKeepsItsLoaderData()
        {
            // The inner navigation runs to completion inside the outer attempt's loader loop, so by the time
            // the outer attempt sees its own token cancelled the live loader state describes the location the
            // user has arrived at. Everything here is synchronous: the loader hands back a completed task, and
            // the nested navigation has no Guard or Blocker to await.
            // Arrange
            Router router = null!;
            router = BuildRouter("/home",
                Route("home"),
                Route("trigger", loader: (ctx, ct) =>
                {
                    router.NavigateAsync("/target").Forget();
                    return VelvetTask.FromResult<object>("trigger-data");
                }),
                Route("target", loader: (ctx, ct) => VelvetTask.FromResult<object>("target-data")));

            // Act
            var result = router.NavigateAsync("/trigger").GetAwaiter().GetResult();

            // Assert
            Assert.That(
                $"result={result} path={router.CurrentLocation?.Path} data={router.GetLoaderData("/target")}",
                Is.EqualTo("result=Cancelled path=/target data=target-data"),
                "An attempt that never commits must leave the loader data of the location that did");
        }

        [Test]
        public void Given_ALoaderThatNavigates_When_TheCancelledAttemptUnwinds_Then_TheCommittedLocationKeepsItsStatus()
        {
            // Same run as the loader-data case, one field over: the cancelled attempt reaches its unwind after
            // the navigation started from inside it has already published its own Ready.
            // Arrange
            Router router = null!;
            router = BuildRouter("/home",
                Route("home"),
                Route("trigger", loader: (ctx, ct) =>
                {
                    router.NavigateAsync("/target").Forget();
                    return VelvetTask.FromResult<object>("trigger-data");
                }),
                Route("target"));

            // Act
            var result = router.NavigateAsync("/trigger").GetAwaiter().GetResult();

            // Assert
            Assert.That($"result={result} status={router.Status}", Is.EqualTo("result=Cancelled status=Ready"),
                "An attempt that has lost the claim must not report its own outcome as the router's state");
        }
    }
}
