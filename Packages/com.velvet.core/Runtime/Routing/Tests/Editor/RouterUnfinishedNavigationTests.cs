using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Bounded so a case awaiting a task that never completes fails at thirty seconds rather than at the
    // runner's own bound, which UnityRunnerDefaultTimeoutTests pins.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouterUnfinishedNavigationTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [UnityTest]
        public IEnumerator Given_AGuardThatThrows_When_TheExceptionReachesTheCaller_Then_TheRouterIsNoLongerInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The exception propagates, but the attempt must end its navigation first.
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
            Assert.That($"threw={caught != null} state={router.Navigation.State}", Is.EqualTo("threw=True state=Idle"),
                "A navigation that died on an exception stops reporting itself as in flight");
        });

        // GREEN_ON_BASE(characterization): the base maps an OperationCanceledException to Cancelled only when
        // the navigation's own token is the cancelled one.
        [UnityTest]
        public IEnumerator Given_AGuardThatThrowsACancellationOfItsOwn_When_TheNavigationIsNotCancelled_Then_TheExceptionReachesTheCaller()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => throw new OperationCanceledException("guard-cancelled")),
                }));
            NavigationResult? result = null;
            Exception caught = null;

            // Act
            try
            {
                result = await router.NavigateAsync("/guarded");
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That($"threw={caught != null} result={result?.ToString() ?? "none"}", Is.EqualTo("threw=True result=none"),
                "A Guard's own cancellation is its failure, not a cancellation of the navigation");
        });

        [UnityTest]
        public IEnumerator Given_AGuardThatThrowsACancellationOfItsOwn_When_TheExceptionReachesTheCaller_Then_TheRouterIsNoLongerInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // An OperationCanceledException takes a handler of its own in the attempt, apart from the one the
            // InvalidOperationException of the first case reaches.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => throw new OperationCanceledException("guard-cancelled")),
                }));
            Exception caught = null;

            // Act
            try
            {
                await router.NavigateAsync("/guarded");
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That($"threw={caught != null} state={router.Navigation.State}", Is.EqualTo("threw=True state=Idle"),
                "A navigation its Guard cancelled stops reporting itself as in flight");
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
            Assert.That($"threw={caught != null} state={router.Navigation.State}", Is.EqualTo("threw=True state=Idle"),
                "A navigation that died in its commit stops reporting itself as in flight, like one that died earlier");
        });

        [Test]
        public void Given_ADepartingLoadersCancellationCallbackThatThrows_When_TheNextNavigationCommits_Then_TheCommitStillCompletes()
        {
            // The commit ends the departing round, which runs whatever cancellation callbacks that round's
            // Loaders registered — application code, and below the handlers. An escape there leaves the
            // router reporting a navigation it has already committed as still in flight.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"),
                Route("registering", loader: (ctx, ct) =>
                {
                    ct.Register(() => throw new InvalidOperationException("cancellation-callback-boom"));
                    return VelvetTask.FromResult<object>("registering-data");
                }),
                Route("other"));
            router.NavigateSync("/registering");
            ContainedFailureLog.Expect<InvalidOperationException>(
                nameof(RouteLoaderRunner), "cancellation-callback-boom");
            Exception caught = null;

            // Act
            try
            {
                router.NavigateSync("/other");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(
                $"threw={caught != null} path={router.CurrentLocation?.Path} state={router.Navigation.State}",
                Is.EqualTo("threw=False path=/other state=Idle"),
                "A Loader's own cancellation callback must not take down the commit that ends its round");
        }

        [Test]
        public void Given_AParkedLoadersCancellationCallbackThatThrows_When_ANewerNavigationTakesOver_Then_TheTakeoverCommits()
        {
            // The takeover cancels the parked navigation's source, and the parked round runs under a token
            // linked to it, so that round's Loaders have their cancellation callbacks run from the takeover
            // rather than from the retire the commit performs.
            // Arrange
            var parking = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("parking", loader: (ctx, ct) =>
                {
                    ct.Register(() => throw new InvalidOperationException("cancellation-callback-boom"));
                    return parking.Task;
                }),
                Route("other"));
            router.NavigateAsync("/parking").Forget();
            ContainedFailureLog.Expect<InvalidOperationException>(
                nameof(Router), "cancellation-callback-boom");
            Exception caught = null;

            // Act
            try
            {
                router.NavigateSync("/other");
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(
                $"threw={caught != null} path={router.CurrentLocation?.Path} state={router.Navigation.State}",
                Is.EqualTo("threw=False path=/other state=Idle"),
                "A Loader's own cancellation callback must not take down the navigation superseding its round");
        }

        // GREEN_ON_BASE(refactor): the base also takes the disposed router out of Router.Current.
        // Current can now name an earlier router still alive, so the case asks whether it names this one.
        [Test]
        public void Given_AParkedLoadersCancellationCallbackThatThrows_When_TheRouterIsDisposed_Then_TheTeardownStillCompletes()
        {
            // Disposal cancels the parked navigation's source, and the parked round runs under a token linked
            // to it, so that round's Loaders have their cancellation callbacks run from there rather than from
            // the runner's retire below it.
            // Arrange
            var parking = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("parking", loader: (ctx, ct) =>
                {
                    ct.Register(() => throw new InvalidOperationException("cancellation-callback-boom"));
                    return parking.Task;
                }));
            router.NavigateAsync("/parking").Forget();
            ContainedFailureLog.Expect<InvalidOperationException>(
                nameof(Router), "cancellation-callback-boom");
            Exception caught = null;

            // Act
            try
            {
                router.Dispose();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That($"threw={caught != null} current={ReferenceEquals(Router.Current, router)}",
                Is.EqualTo("threw=False current=False"),
                "A Loader's own cancellation callback must not take down the teardown that ends its round");
        }

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
        public void Given_ALoaderThatNavigates_When_TheCancelledAttemptUnwinds_Then_TheNavigationItStartedIsStillReported()
        {
            // The loader-data case's run with the inner navigation parked on its own loader, so the cancelled
            // attempt reaches its unwind while the navigation that took over from it is still in flight.
            // Arrange
            var parked = new VelvetTaskCompletionSource<object>();
            Router router = null!;
            router = BuildRouter("/home",
                Route("home"),
                Route("trigger", loader: (ctx, ct) =>
                {
                    router.NavigateAsync("/target").Forget();
                    return VelvetTask.FromResult<object>("trigger-data");
                }),
                Route("target", loader: (ctx, ct) => parked.Task));

            // Act
            var result = router.NavigateAsync("/trigger").GetAwaiter().GetResult();

            // Assert
            Assert.That(
                $"result={result} state={router.Navigation.State} location={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("result=Cancelled state=Loading location=/target"),
                "An attempt that has lost the claim must not end the navigation that holds it");
        }
    }
}
