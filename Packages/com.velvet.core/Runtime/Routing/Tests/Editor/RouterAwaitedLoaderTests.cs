using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    /// <item><see cref="Router.Navigation"/> reports <see cref="NavigationLifecycle.Loading"/> through that
    /// window, and the destination — resolved, so it carries the destination's path parameters.</item>
    /// <item>The destination is published only while a navigation is in flight: the commit that lands it,
    /// a guard redirect that matches no route, a redirect chain that exhausts the limit, and disposing the
    /// router each withdraw it — the two redirect refusals only from the initiator that still holds the
    /// claim.</item>
    /// <item>A loader that fails after suspending has its own exception recorded against the route, and the
    /// navigation still commits.</item>
    /// <item>The route on screen through that window is still the live one: a Suspend loader of its own
    /// keeps its token and reaches the live loader data, and only the commit that leaves the route ends
    /// it.</item>
    /// <item>What that Suspend loader settles is its own round, so the history entry it writes back to
    /// stays servable even while the round holding the commit is unsettled.</item>
    /// <item>A loader still holding its round's token when the router ends that round reads the ending
    /// off it rather than finding the source gone — the awaited one superseded inside its own run, and
    /// the Suspend one on screen at the commit that leaves it.</item>
    /// <item>A navigation that matches no route neither cancels the attempt holding the window open nor
    /// takes over the navigation describing it.</item>
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

        // A router sitting on /feed, whose Suspend loader has not produced anything, with a navigation to
        // /profile parked on an Await loader — so /feed is what the user is looking at for as long as the
        // caller leaves `awaited` unresolved.
        private static (Router router, VelvetTaskCompletionSource<object> streaming,
            VelvetTaskCompletionSource<object> awaited) RouterStreamingUnderAnAwaitedNavigation(
                Func<RouteLoaderContext, CancellationToken, VelvetTask<object>> feedLoader = null)
        {
            var streaming = new VelvetTaskCompletionSource<object>();
            var awaited = new VelvetTaskCompletionSource<object>();
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
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_TheNavigationIsLoading()
        {
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.Navigation.State, Is.EqualTo(NavigationLifecycle.Loading));
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_TheNavigationsLocationIsTheDestination()
        {
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.Navigation.Location?.Path, Is.EqualTo("/users/7"));
        }

        [Test]
        public void Given_AnAwaitLoaderThatHasNotResolved_When_Navigating_Then_TheNavigationsLocationCarriesTheDestinationsParams()
        {
            // The destination is resolved against the route tree rather than carried as the raw path a
            // Blocker is handed, so a pending-UI branch can read the parameters it is loading for.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();

            // Act
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(router.Navigation.Location?.Params["id"], Is.EqualTo("7"));
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
            var whileLoading = router.Navigation.Location?.Path ?? "none";

            // Act
            loader.TrySetResult("user-7");
            await navigation;

            // Assert
            Assert.That(
                $"loading={whileLoading} settled={router.Navigation.Location?.Path ?? "none"}",
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
        public void Given_ANavigationAwaitingItsLoader_When_ANewerNavigationTakesOver_Then_TheRouterGoesStraightFromOneToTheNext()
        {
            // The parked loader honours its token, so the attempt it holds unwinds inside the takeover's
            // cancel, before the newer navigation has published itself. React Router's navigation goes from
            // one loading navigation to the next with no idle between them.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"),
                Route("a", loader: (ctx, ct) =>
                {
                    var pending = new VelvetTaskCompletionSource<object>();
                    ct.Register(() => pending.TrySetCanceled());
                    return pending.Task;
                }),
                Route("b"));
            var published = new List<string>();
            router.OnNavigationChanged += navigation =>
                published.Add($"{navigation.State}({navigation.Location?.Path})");
            router.NavigateAsync("/a").Forget();

            // Act
            router.NavigateSync("/b");

            // Assert
            Assert.That(string.Join(",", published), Is.EqualTo("Loading(/a),Loading(/b),Idle()"));
        }

        [UnityTest]
        public IEnumerator Given_ANavigationAwaitingItsLoader_When_ItsCallerCancelsIt_Then_NoNavigationIsLeftInFlight()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The cancellation is observed after the loaders rather than before them, by a check of its own.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"),
                Route("users/:id", loader: (ctx, ct) =>
                {
                    var pending = new VelvetTaskCompletionSource<object>();
                    ct.Register(() => pending.TrySetCanceled());
                    return pending.Task;
                }));
            using var caller = new CancellationTokenSource();
            var navigation = router.NavigateAsync("/users/7", NavigationMode.Push, caller.Token);
            var whileLoading = router.Navigation.State;

            // Act
            caller.Cancel();
            var result = await navigation;

            // Assert
            Assert.That($"loading={whileLoading} result={result} state={router.Navigation.State}",
                Is.EqualTo("loading=Loading result=Cancelled state=Idle"));
        });

        [Test]
        public void Given_ANavigationAwaitingItsLoader_When_TheRouterIsDisposed_Then_ThePendingDestinationIsWithdrawn()
        {
            // The attempt that would withdraw it never gets to: disposal retires its claim first, and the
            // loader it is parked on need never resolve at all.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();
            router.NavigateAsync("/users/7").Forget();
            var whileLoading = router.Navigation.Location?.Path ?? "none";

            // Act
            router.Dispose();

            // Assert
            Assert.That(
                $"loading={whileLoading} disposed={router.Navigation.Location?.Path ?? "none"}",
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
            var afterRedirect = router.Navigation.Location?.Path ?? "none";
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(
                $"result={redirected} afterRedirect={afterRedirect} "
                + $"loading={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("result=NotFound afterRedirect=none loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_ARedirectChain_When_ItExhaustsTheRedirectLimit_Then_ThePendingDestinationIsWithdrawn()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The hop that is refused ends the attempt its initiator published a destination for, and the
            // initiator forwards the result without touching it. A second navigation carries the publishing
            // side, since a redirect chain resolves with no await for the first destination to be read across.
            // Arrange
            var loader = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home",
                Route("home"),
                Route("a", redirectTo: "/b"),
                Route("b", redirectTo: "/a"),
                Route("users/:id", loader: (ctx, ct) => loader.Task));

            // Act
            var overflowed = await router.NavigateAsync("/a");
            var afterOverflow = router.Navigation.Location?.Path ?? "none";
            router.NavigateAsync("/users/7").Forget();

            // Assert
            Assert.That(
                $"result={overflowed} afterOverflow={afterOverflow} "
                + $"loading={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("result=Error afterOverflow=none loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_AGuardThatNavigatesThenRedirectsToNoRoute_When_TheRedirectIsTaken_Then_ItIsCancelledAndTheNewerDestinationStands()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The two cases above withdraw the destination of an initiator that still holds the claim.
            // A guard delegate runs before its own attempt has awaited anything, so a navigation issued
            // from one takes the claim while the attempt that called it is still inside the guard — and
            // the attempt the redirect belongs to has then been superseded, as React Router's aborted
            // navigation is, whatever the redirect's target.
            // Arrange
            var loader = new VelvetTaskCompletionSource<object>();
            Router router = null;
            router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ =>
                    {
                        router.NavigateAsync("/users/7").Forget();
                        return "/no-such-route";
                    }),
                    Route("users/:id", loader: (ctx, ct) => loader.Task),
                }),
            });
            await router.NavigateAsync("/home");

            // Act
            var guarded = await router.NavigateAsync("/guarded");

            // Assert — the result rides along because a destination reading /users/7 is also what a guard
            // that redirected nowhere at all would leave.
            Assert.That(
                $"guarded={guarded} loading={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("guarded=Cancelled loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_AGuardThatNavigatesThenRedirectsToARoute_When_TheRedirectMatches_Then_TheNewerDestinationStands()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The case above with a redirect that matches, which would otherwise publish its own destination
            // over the newer attempt's.
            // Arrange
            var loader = new VelvetTaskCompletionSource<object>();
            Router router = null;
            router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ =>
                    {
                        router.NavigateAsync("/users/7").Forget();
                        return "/target";
                    }),
                    Route("target"),
                    Route("users/:id", loader: (ctx, ct) => loader.Task),
                }),
            });
            await router.NavigateAsync("/home");

            // Act
            var guarded = await router.NavigateAsync("/guarded");

            // Assert
            Assert.That(
                $"guarded={guarded} path={router.CurrentLocation?.Path} "
                + $"loading={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("guarded=Cancelled path=/home loading=/users/7"));
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderOnTheRouteOnScreen_When_AnAwaitedNavigationHoldsTheCommit_Then_ItsResultReachesTheLiveData()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The committed location is what the user is looking at, and it stays /feed for the whole window.
            // Folding it in separates a result that landed on the route it belongs to from one that landed
            // anywhere.
            // Arrange
            var (router, streaming, _) = RouterStreamingUnderAnAwaitedNavigation();

            // Act
            streaming.TrySetResult("feed-data");
            await VelvetTask.Yield();

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
                return new VelvetTaskCompletionSource<object>().Task;
            });

            // Act
            var stillLoading = router.Navigation.State;

            // Assert
            Assert.That(
                $"state={stillLoading} cancellable={captured.CanBeCanceled} "
                + $"cancelled={captured.IsCancellationRequested}",
                Is.EqualTo("state=Loading cancellable=True cancelled=False"));
        }

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderOnTheRouteOnScreen_When_TheAwaitedNavigationCommits_Then_ItsTokenIsCancelled()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The other half of the rule: the round streaming into the route on screen ends at the commit
            // that leaves that route, not before it and not never.
            // Arrange
            CancellationToken captured = default;
            var (router, _, awaited) = RouterStreamingUnderAnAwaitedNavigation((ctx, ct) =>
            {
                captured = ct;
                return new VelvetTaskCompletionSource<object>().Task;
            });

            // Act
            awaited.TrySetResult("profile-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(
                $"path={router.CurrentLocation?.Path} cancelled={captured.IsCancellationRequested}",
                Is.EqualTo("path=/profile cancelled=True"));
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderOnTheRouteOnScreen_When_TheAwaitedNavigationCommits_Then_ItReadsCancellationRatherThanAReleasedSource()
            => VelvetTask.ToCoroutine(async () =>
        {
            // This loader's own run returned at the commit that put it on screen, so this loader is what
            // still holds the token — where the awaited loader below is inside the run that launched it.
            // Arrange
            CancellationToken captured = default;
            var (router, _, awaited) = RouterStreamingUnderAnAwaitedNavigation((ctx, ct) =>
            {
                captured = ct;
                return new VelvetTaskCompletionSource<object>().Task;
            });

            // Act
            awaited.TrySetResult("profile-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(ReadTokenState(captured), Is.EqualTo("cancelled"),
                "A loader the commit cancelled is still able to ask its token what happened");
        });

        [UnityTest]
        public IEnumerator Given_ANavigationAwaitingItsLoader_When_ANewerNavigationSupersedesIt_Then_TheLoaderReadsCancellationRatherThanAReleasedSource()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The supersession retires this round while the run that launched it is still awaiting this
            // loader, which is the half of the window the Suspend case above cannot reach.
            // Arrange
            var parked = new VelvetTaskCompletionSource<object>();
            var entered = new VelvetTaskCompletionSource();
            string loaderSaw = null;
            var router = BuildRouter("/home",
                Route("home"),
                Route("users/:id", loader: async (ctx, ct) =>
                {
                    entered.TrySetResult();
                    try
                    {
                        return await parked.Task;
                    }
                    catch (OperationCanceledException)
                    {
                        loaderSaw = ReadTokenState(ct);
                        throw;
                    }
                }),
                Route("other"));
            router.NavigateAsync("/users/7").Forget();
            await entered.Task;

            // Act
            await router.NavigateAsync("/other");
            parked.TrySetCanceled();

            // Assert
            Assert.That(loaderSaw, Is.EqualTo("cancelled"),
                "A loader unwinding on the cancellation that superseded it is still able to ask its token");
        });

        [UnityTest]
        public IEnumerator Given_ANavigationHoldingTheCommit_When_APathMatchingNoRouteIsNavigatedTo_Then_TheHeldNavigationStillCommits()
            => VelvetTask.ToCoroutine(async () =>
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
        public IEnumerator Given_ANavigationHoldingTheCommit_When_APathMatchingNoRouteIsNavigatedTo_Then_TheRouterStillReportsTheHeldNavigation()
            => VelvetTask.ToCoroutine(async () =>
        {
            // An attempt that never matched takes no claim, so Navigation is not its to describe. The result
            // it hands its own caller is folded in: reporting nothing must not cost the caller the outcome.
            // Arrange
            var (router, _) = RouterAwaitingItsLoader();
            router.NavigateAsync("/users/7").Forget();

            // Act
            var unmatched = await router.NavigateAsync("/nowhere");

            // Assert
            Assert.That(
                $"unmatched={unmatched} state={router.Navigation.State} "
                + $"location={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("unmatched=NotFound state=Loading location=/users/7"));
        });
    }
}
