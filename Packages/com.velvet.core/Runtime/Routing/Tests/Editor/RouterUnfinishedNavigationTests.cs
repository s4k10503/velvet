using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Bounded for the cases here that await a blocker stub's Entered signal;
    // RouteTestStubs.MakeOneShotBlocker states what an unbounded fixture costs.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouterUnfinishedNavigationTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_ABackParkedInABlocker_When_APushCommitsMeanwhile_Then_TheEntryTheUserIsOnSurvives()
            => UniTask.ToCoroutine(async () =>
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
            await entered.Task.Bounded();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/settings"),
                "Precondition: the Back is still parked in the blocker and has committed nothing");

            // Act
            await router.NavigateAsync("/contact");

            // Assert
            Assert.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home,/about,/settings,/contact"),
                "A navigation that has not committed must not move the position a later Push builds on");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_AForwardParkedInABlocker_When_APushTakesOverItsSlot_Then_ItCommitsNothing()
            => UniTask.ToCoroutine(async () =>
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
            await entered.Task.Bounded();
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

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_ARedirectParkedInABlocker_When_ANewerNavigationCommits_Then_NoEntryForTheRedirectingPathRemains()
            => UniTask.ToCoroutine(async () =>
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
            await entered.Task.Bounded();

            // Act
            await router.NavigateAsync("/x");

            // Assert
            Assert.That(RouterHistoryProbe.PathsOf(router), Is.EqualTo("/home,/x"),
                "A redirect that never reached its target must leave no entry for the path it started from");
        });

        [UnityTest]
        public IEnumerator Given_AGuardThatThrows_When_TheExceptionReachesTheCaller_Then_TheRouterIsNoLongerInFlight()
            => UniTask.ToCoroutine(async () =>
        {
            // The exception propagates, but the attempt must release its in-flight Status first.
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
            => UniTask.ToCoroutine(async () =>
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
            => UniTask.ToCoroutine(async () =>
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
            => UniTask.ToCoroutine(async () =>
        {
            // The entry's snapshot is empty because the loader never resolved, which is indistinguishable by
            // content from a route whose loaders legitimately produced nothing.
            // Arrange
            var unresolved = new UniTaskCompletionSource<object>();
            var loaderCalls = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users/:id", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) =>
                        {
                            Interlocked.Increment(ref loaderCalls);
                            return unresolved.Task;
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
            => UniTask.ToCoroutine(async () =>
        {
            // A loader handed an already-complete task resolves inside the run that launched it, so its
            // completion reaches the router before this step has a location to write it under and is refused.
            // The commit is then the only thing left that can mark the entry finished.
            // Arrange
            var unresolved = new UniTaskCompletionSource<object>();
            var loaderCalls = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users/:id", loaderMode: LoaderMode.Suspend,
                        loader: (ctx, ct) => Interlocked.Increment(ref loaderCalls) == 1
                            ? unresolved.Task
                            : UniTask.FromResult((object)"user-data")),
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
                    return UniTask.FromResult<object>("trigger-data");
                }),
                Route("target", loader: (ctx, ct) => UniTask.FromResult<object>("target-data")));

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
                    return UniTask.FromResult<object>("trigger-data");
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
