using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    /// the Blocker refusal that abandons it, a guard redirect that matches no route, a redirect chain that
    /// exhausts the limit, and disposing the router each withdraw it.</item>
    /// <item>A loader that fails after suspending has its own exception recorded against the route, and the
    /// navigation still commits.</item>
    /// <item>The route on screen through that window is still the live one: a Suspend loader of its own
    /// keeps its token and reaches the live loader data, and only the commit that leaves the route ends
    /// it.</item>
    /// <item>A navigation that matches no route neither cancels the attempt holding the window open nor
    /// takes over the status describing it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterAwaitedLoaderTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        private static (Router router, UniTaskCompletionSource<object> loader) RouterAwaitingItsLoader()
        {
            var loader = new UniTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("users/:id", loader: (ctx, ct) => loader.Task));
            return (router, loader);
        }

        // A router sitting on /feed, whose Suspend loader has not produced anything, with a navigation to
        // /profile parked on an Await loader — so /feed is what the user is looking at for as long as the
        // caller leaves `awaited` unresolved.
        private static (Router router, UniTaskCompletionSource<object> streaming,
            UniTaskCompletionSource<object> awaited) RouterStreamingUnderAnAwaitedNavigation(
                Func<RouteLoaderContext, CancellationToken, UniTask<object>> feedLoader = null)
        {
            var streaming = new UniTaskCompletionSource<object>();
            var awaited = new UniTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("feed", loaderMode: LoaderMode.Suspend,
                    loader: feedLoader ?? ((ctx, ct) => streaming.Task)),
                Route("profile", loader: (ctx, ct) => awaited.Task));
            router.NavigateSync("/feed");
            router.NavigateAsync("/profile").Forget();
            return (router, streaming, awaited);
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
            => UniTask.ToCoroutine(async () =>
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
            => UniTask.ToCoroutine(async () =>
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
            => UniTask.ToCoroutine(async () =>
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
            => UniTask.ToCoroutine(async () =>
        {
            // The redirect ends before taking a claim of its own, so nothing in that frame withdraws what the
            // attempt it belongs to published. A second navigation carries the publishing side of the
            // comparison, since a redirect resolves with no await for the first one's destination to be read
            // across.
            // Arrange
            var loader = new UniTaskCompletionSource<object>();
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
        public IEnumerator Given_ARedirectChain_When_ItExhaustsTheRedirectLimit_Then_ThePendingDestinationIsWithdrawn()
            => UniTask.ToCoroutine(async () =>
        {
            // The hop that is refused ends the attempt its initiator published a destination for, and the
            // initiator forwards the result without touching it. A second navigation carries the publishing
            // side, since a redirect chain resolves with no await for the first destination to be read across.
            // Arrange
            var loader = new UniTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("a", redirectTo: "/b"),
                Route("b", redirectTo: "/a"),
                Route("users/:id", loader: (ctx, ct) => loader.Task));

            // Act
            var overflowed = await router.NavigateAsync("/a");
            var afterOverflow = router.PendingLocation?.Path ?? "none";
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(
                $"result={overflowed} afterOverflow={afterOverflow} "
                + $"loading={router.PendingLocation?.Path ?? "none"}",
                Is.EqualTo("result=Error afterOverflow=none loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderOnTheRouteOnScreen_When_AnAwaitedNavigationHoldsTheCommit_Then_ItsResultReachesTheLiveData()
            => UniTask.ToCoroutine(async () =>
        {
            // The committed location is what the user is looking at, and it stays /feed for the whole window.
            // Folding it in separates a result that landed on the route it belongs to from one that landed
            // anywhere.
            // Arrange
            var (router, streaming, _) = RouterStreamingUnderAnAwaitedNavigation();

            // Act
            streaming.TrySetResult("feed-data");
            await UniTask.Yield();

            // Assert
            Assert.That(
                $"path={router.CurrentLocation?.Path} "
                + $"data={string.Join(",", router.CurrentLoaderData.Values)}",
                Is.EqualTo("path=/feed data=feed-data"));
        });

        [Test]
        public void Given_ASuspendLoaderOnTheRouteOnScreen_When_AnAwaitedNavigationHoldsTheCommit_Then_ItsTokenIsNotCancelled()
        {
            // A loader that honours its token produces nothing once cancelled, so cancelling it here is the
            // same defect as withholding its result: the spinner on the route on screen never clears.
            // Arrange
            CancellationToken captured = default;
            var (router, _, _) = RouterStreamingUnderAnAwaitedNavigation((ctx, ct) =>
            {
                captured = ct;
                return new UniTaskCompletionSource<object>().Task;
            });

            // Act
            var stillLoading = router.Status;

            // Assert
            Assert.That(
                $"status={stillLoading} cancellable={captured.CanBeCanceled} "
                + $"cancelled={captured.IsCancellationRequested}",
                Is.EqualTo("status=Loading cancellable=True cancelled=False"));
        }

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderOnTheRouteOnScreen_When_TheAwaitedNavigationCommits_Then_ItsTokenIsCancelled()
            => UniTask.ToCoroutine(async () =>
        {
            // The other half of the rule: the round streaming into the route on screen ends at the commit
            // that leaves that route, not before it and not never.
            // Arrange
            CancellationToken captured = default;
            var (router, _, awaited) = RouterStreamingUnderAnAwaitedNavigation((ctx, ct) =>
            {
                captured = ct;
                return new UniTaskCompletionSource<object>().Task;
            });

            // Act
            awaited.TrySetResult("profile-data");
            await UniTask.Yield();

            // Assert
            Assert.That(
                $"path={router.CurrentLocation?.Path} cancelled={captured.IsCancellationRequested}",
                Is.EqualTo("path=/profile cancelled=True"));
        });

        [UnityTest]
        public IEnumerator Given_ANavigationHoldingTheCommit_When_APathMatchingNoRouteIsNavigatedTo_Then_TheHeldNavigationStillCommits()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var (router, loader) = RouterAwaitingItsLoader();
            var navigation = router.NavigateAsync("/users/7");

            // Act
            var unmatched = await router.NavigateAsync("/nowhere");
            loader.TrySetResult("user-7");
            var held = await navigation;

            // Assert
            Assert.That(
                $"unmatched={unmatched} held={held} path={router.CurrentLocation?.Path}",
                Is.EqualTo("unmatched=NotFound held=Success path=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_ANavigationHoldingTheCommit_When_APathMatchingNoRouteIsNavigatedTo_Then_TheStatusStillDescribesTheHeldNavigation()
            => UniTask.ToCoroutine(async () =>
        {
            // An attempt that never matched takes no claim, so it is not the one Status belongs to. The
            // result it hands its own caller is folded in: withholding the status must not cost the caller
            // the outcome.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();
            router.NavigateAsync("/users/7").Forget();

            // Act
            var unmatched = await router.NavigateAsync("/nowhere");

            // Assert
            Assert.That($"unmatched={unmatched} status={router.Status}",
                Is.EqualTo("unmatched=NotFound status=Loading"));
        });

        [UnityTest]
        public IEnumerator Given_ABlockerParkedOnTheDeparture_When_ItRefuses_Then_ThePendingDestinationIsWithdrawn()
            => UniTask.ToCoroutine(async () =>
        {
            // An attempt that gives up has to take its destination back with it, and the blocker is the phase
            // that can be held open long enough to read the destination before the refusal lands.
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("users/:id"));
            var parked = new UniTaskCompletionSource<bool>();
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
