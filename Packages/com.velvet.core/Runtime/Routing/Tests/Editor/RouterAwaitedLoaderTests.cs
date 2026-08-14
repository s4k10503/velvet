using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the window an <see cref="LoaderMode.Await"/> loader opens between the navigation starting
    /// and committing, and what the router publishes while it is open.
    /// <list type="bullet">
    /// <item>The commit waits for the loader: the committed location stays that of the route already on
    /// screen until the loader's task resolves, and the location and its data commit together.</item>
    /// <item>The router reports <see cref="RouterStatus.Loading"/> through that window, and
    /// <see cref="Router.PendingLocation"/> reports the destination — resolved, so it carries the
    /// destination's path parameters.</item>
    /// <item>The destination is published only while a navigation is in flight: the commit that lands it,
    /// the Blocker refusal that abandons it, a guard redirect that matches no route, and disposing the
    /// router each withdraw it.</item>
    /// <item>A loader that fails after suspending has its own exception recorded against the route, and the
    /// navigation still commits.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterAwaitedLoaderTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private static (Router router, VelvetTaskCompletionSource<object> loader) RouterAwaitingItsLoader()
        {
            var loader = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("users/:id", loader: (ctx, ct) => loader.Task));
            return (router, loader);
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_TheCommittedLocationIsUnchanged()
        {
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "The route on screen stays there until the awaited loader resolves");
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_TheStatusIsLoading()
        {
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Loading));
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_PendingLocationIsTheDestination()
        {
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.PendingLocation?.Path, Is.EqualTo("/users/7"));
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_PendingLocationCarriesTheDestinationsParams()
        {
            // The destination is resolved against the route tree rather than carried as the raw path a
            // Blocker is handed, so a pending-UI branch can read the parameters it is loading for.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.PendingLocation?.Params["id"], Is.EqualTo("7"));
        }

        [UnityTest]
        public IEnumerator Given_ANavigationAwaitingItsLoader_When_TheLoaderResolves_Then_TheLocationAndItsDataCommitTogether()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var (router, loader) = RouterAwaitingItsLoader();
            var navigation = router.NavigateAsync("/users/7");

            // Act
            loader.TrySetResult("user-7");
            var result = await navigation;

            // Assert
            Assert.That(
                $"result={result} path={router.CurrentLocation?.Path} "
                + $"data={string.Join(",", router.CurrentLoaderData.Values)}",
                Is.EqualTo("result=Success path=/users/7 data=user-7"));
        });

        [UnityTest]
        public IEnumerator Given_ANavigationAwaitingItsLoader_When_TheLoaderResolves_Then_ThePendingDestinationIsWithdrawn()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Both halves in one comparison: read alone, a null on the settled side is also what a router
            // that never published a destination reports.
            // Arrange
            var (router, loader) = RouterAwaitingItsLoader();
            var navigation = router.NavigateAsync("/users/7");
            var whileLoading = router.PendingLocation?.Path ?? "none";

            // Act
            loader.TrySetResult("user-7");
            await navigation;

            // Assert
            Assert.That(
                $"loading={whileLoading} settled={router.PendingLocation?.Path ?? "none"}",
                Is.EqualTo("loading=/users/7 settled=none"));
        });

        [UnityTest]
        public IEnumerator Given_ANavigationAwaitingItsLoader_When_TheLoaderFails_Then_TheRouteRecordsTheLoadersOwnError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var (router, loader) = RouterAwaitingItsLoader();
            var navigation = router.NavigateAsync("/users/7");

            // Act
            loader.TrySetException(new InvalidOperationException("late-failure"));
            var result = await navigation;

            // Assert
            Assert.That(
                $"result={result} errors="
                + string.Join(",", router.CurrentLoaderErrors.Values.Select(error => error.Message)),
                Is.EqualTo("result=Success errors=late-failure"));
        });

        [Test]
        public void Given_ANavigationAwaitingItsLoader_When_TheRouterIsDisposed_Then_ThePendingDestinationIsWithdrawn()
        {
            // The attempt that would withdraw it never gets to: disposal retires its claim first, and the
            // loader it is parked on need never resolve at all.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();
            router.NavigateAsync("/users/7").Forget();
            var whileLoading = router.PendingLocation?.Path ?? "none";

            // Act
            router.Dispose();

            // Assert
            Assert.That(
                $"loading={whileLoading} disposed={router.PendingLocation?.Path ?? "none"}",
                Is.EqualTo("loading=/users/7 disposed=none"));
        }

        [UnityTest]
        public IEnumerator Given_AGuardRedirectingToNoRoute_When_ItFailsToMatch_Then_ThePendingDestinationIsWithdrawn()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The redirect ends before taking a claim of its own, so nothing in that frame withdraws what the
            // attempt it belongs to published. A second navigation carries the publishing side of the
            // comparison, since a redirect resolves with no await for the first one's destination to be read
            // across.
            // Arrange
            var loader = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("gated", guard: _ => "/nowhere"),
                Route("users/:id", loader: (ctx, ct) => loader.Task));

            // Act
            var redirected = await router.NavigateAsync("/gated");
            var afterRedirect = router.PendingLocation?.Path ?? "none";
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(
                $"result={redirected} afterRedirect={afterRedirect} "
                + $"loading={router.PendingLocation?.Path ?? "none"}",
                Is.EqualTo("result=NotFound afterRedirect=none loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_ABlockerParkedOnTheDeparture_When_ItRefuses_Then_ThePendingDestinationIsWithdrawn()
            => VelvetTask.ToCoroutine(async () =>
        {
            // An attempt that gives up has to take its destination back with it, and the blocker is the phase
            // that can be held open long enough to read the destination before the refusal lands.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("users/:id"));
            var parked = new VelvetTaskCompletionSource<bool>();
            using var registration = router.RouteBlockerManager.Register(
                (_, _) => parked.Task, new RouteBlockerState());
            var navigation = router.NavigateAsync("/users/7");
            var whileBlocking = router.PendingLocation?.Path ?? "none";

            // Act
            parked.TrySetResult(true);
            var result = await navigation;

            // Assert
            Assert.That(
                $"blocking={whileBlocking} result={result} settled={router.PendingLocation?.Path ?? "none"}",
                Is.EqualTo("blocking=/users/7 result=Blocked settled=none"));
        });
    }
}
