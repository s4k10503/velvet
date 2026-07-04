using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the navigation, history, loader-cache, and event contract of <see cref="Router"/>.
    /// <list type="bullet">
    /// <item>A fresh router is Idle; a valid navigation becomes Ready and exposes the committed location, while
    /// an unmatched path returns <see cref="NavigationResult.NotFound"/> and the NotFound status.</item>
    /// <item>An Await loader's result is committed before navigation completes and is keyed by
    /// <see cref="RouteMatch.RouteId"/>.</item>
    /// <item>Each successful navigation pushes a history entry; GoBack/GoForward restore the previous/next
    /// location, and stepping past either end returns <see cref="NavigationResult.Cancelled"/>.</item>
    /// <item>GoBack/GoForward serve loader data and loader errors from the history cache without re-running the
    /// loader.</item>
    /// <item>A Suspend loader commits navigation immediately and resolves later; on resolution or failure the
    /// router writes the post-resolution value into the current history entry and re-emits the location with a
    /// fresh identity, but a resolution arriving after the user navigated away does not churn the current
    /// location.</item>
    /// <item><c>OnLocationChanged</c> fires once per navigation with the committed location.</item>
    /// <item>An optional <see cref="IRouteScopeFactory"/> is exposed through <c>ScopeFactory</c> and is null
    /// when not supplied.</item>
    /// <item>A concurrent navigation arriving during an async Blocker await cancels the in-flight navigation
    /// (which returns <see cref="NavigationResult.Cancelled"/>) and adopts the latest; a caller token cancelled
    /// during that window also maps to Cancelled rather than throwing.</item>
    /// </list>
    /// </summary>
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

        // Router.Current is a global singleton; each new Router() overwrites it, so disposing in TearDown
        // returns it to null and isolates tests.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        #region Initial state

        [Test]
        public void Given_FreshRouter_When_NotYetNavigated_Then_StatusIsIdle()
        {
            // Arrange + Act
            var router = new Router(_routes);

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Idle));
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
        public void Given_ValidPath_When_Navigating_Then_StatusBecomesReady()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Ready));
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
        public void Given_UnmatchedPath_When_Navigating_Then_StatusBecomesNotFound()
        {
            // Arrange
            var router = new Router(_routes);

            // Act
            router.NavigateSync("/nonexistent");

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.NotFound));
        }

        #endregion

        #region Await loader

        [Test]
        public void Given_AwaitLoader_When_Navigating_Then_ReturnsSuccess()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("data", loader: (ctx, ct) => UniTask.FromResult((object)"loaded")),
            });

            // Act
            var result = router.NavigateSync("/data");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_AwaitLoader_When_Navigating_Then_LoaderDataIsCommittedKeyedByRouteId()
        {
            // The loader data is keyed by RouteId (the cumulative pattern path from the root).
            // Arrange
            var router = new Router(new[]
            {
                Route("data", loader: (ctx, ct) => UniTask.FromResult((object)"loaded")),
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

        #endregion

        #region Loader cache on Back/Forward

        [Test]
        public void Given_LoadedRoute_When_GoBackThenGoForward_Then_LoaderNotReRun()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("data", loader: (ctx, ct) => UniTask.FromResult((object)$"loaded-{++loaderCallCount}")),
                }),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/data");
            Assume.That(loaderCallCount, Is.EqualTo(1), "Precondition: the loader ran once on first load");

            // Act
            router.GoBackSync();
            router.GoForwardSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(1), "The loader is served from cache on GoForward");
        }

        [Test]
        public void Given_LoadedRoute_When_GoForwardFromCache_Then_LoaderDataIsRestored()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("data", loader: (ctx, ct) => UniTask.FromResult((object)$"loaded-{++loaderCallCount}")),
                }),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/data");

            // Act
            router.GoBackSync();
            router.GoForwardSync();

            // Assert
            Assert.That(router.GetLoaderData("/data"), Is.EqualTo("loaded-1"));
        }

        [Test]
        public void Given_LoadedRoute_When_GoBackToIt_Then_LoaderNotReRun()
        {
            // Arrange
            var loaderCallCount = 0;
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("page1", loader: (ctx, ct) => UniTask.FromResult((object)$"page1-{++loaderCallCount}")),
                    Route("page2"),
                }),
            });
            router.NavigateSync("/page1");
            router.NavigateSync("/page2");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(1), "The loader is served from cache on GoBack");
        }

        [Test]
        public void Given_LoadedRoute_When_GoBackToIt_Then_LoaderDataIsRestored()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("page1", loader: (ctx, ct) => UniTask.FromResult((object)"page1-data")),
                    Route("page2"),
                }),
            });
            router.NavigateSync("/page1");
            router.NavigateSync("/page2");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.GetLoaderData("/page1"), Is.EqualTo("page1-data"));
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
        public void Given_ErroredRouteLeftAndReturned_When_GoBackToIt_Then_LoaderNotReRun()
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
            Assume.That(loaderCallCount, Is.EqualTo(1), "Precondition: the loader ran once on first load");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(loaderCallCount, Is.EqualTo(1), "The error is served from the history cache, not re-run");
        }

        [Test]
        public void Given_ErroredRouteLeftAndReturned_When_GoBackToIt_Then_CachedErrorIsRePresented()
        {
            // A Back cache hit restores the cached loader error symmetrically with loader data, so
            // UseRouteError / ErrorElement fire again.
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
            router.NavigateSync("/safe");
            Assume.That(router.CurrentLoaderErrors, Is.Empty, "Precondition: leaving cleared the error map");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLoaderErrors["/boom"].Message, Does.Contain("boom-error"));
        }

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvedAfterCommit_When_GoBackToIt_Then_RestoresPostResolutionData()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange — a Suspend loader commits before resolving, so the history snapshot freezes without the value.
            var tcs = new UniTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            router.NavigateSync("/deferred");
            Assume.That(router.GetLoaderData("/deferred"), Is.Null, "Precondition: unresolved at commit time");
            tcs.TrySetResult("deferred-data");
            await UniTask.Yield();
            Assume.That(router.GetLoaderData("/deferred"), Is.EqualTo("deferred-data"), "Precondition: resolved after commit");
            router.NavigateSync("/other");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.GetLoaderData("/deferred"), Is.EqualTo("deferred-data"),
                "The Back cache hit restores the post-resolution snapshot, not the stale pre-resolution one");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFailedAfterCommit_When_GoBackToIt_Then_RestoresCachedError()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("deferred", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend),
                    Route("other"),
                }),
            });
            router.NavigateSync("/deferred");
            Assume.That(router.CurrentLoaderErrors, Is.Empty, "Precondition: unfailed at commit time");
            // Suspend-mode failures route through OnSuspendLoaderFailed, which logs the exception.
            LogAssert.Expect(UnityEngine.LogType.Exception, new System.Text.RegularExpressions.Regex("deferred-failure"));
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await UniTask.Yield();
            Assume.That(router.CurrentLoaderErrors.Count, Is.EqualTo(1), "Precondition: the failure was recorded after commit");
            router.NavigateSync("/other");

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLoaderErrors["/deferred"].Message, Does.Contain("deferred-failure"),
                "The Back cache hit restores the post-resolution error recorded by the Suspend loader");
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
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "Suspend completion re-emits OnLocationChanged exactly once more");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolves_When_AfterCommit_Then_ReEmitsWithFreshIdentity()
            => UniTask.ToCoroutine(async () =>
        {
            // The canonical location Provider bails on a referentially-equal value, so reusing the same
            // instance would drop the re-render; the re-emit must carry a fresh RouterLocation identity.
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(lastEmitted, Is.Not.SameAs(navigationLocation),
                "The re-emit carries a fresh instance the location Provider will not bail");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolves_When_AfterCommit_Then_PreservesLocationContent()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(lastEmitted.Path, Is.EqualTo("/deferred"), "Location content is preserved across the re-emit");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFails_When_AfterCommit_Then_ReEmitsLocationOnce()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "Suspend failure re-emits OnLocationChanged exactly once more");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFails_When_AfterCommit_Then_ReEmitsWithFreshIdentity()
            => UniTask.ToCoroutine(async () =>
        {
            // A fresh identity is required so UseRouteError consumers re-render on the deferred failure.
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(lastEmitted, Is.Not.SameAs(navigationLocation));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_DoesNotReEmit()
            => UniTask.ToCoroutine(async () =>
        {
            // A navigated-away loader's late result belongs to a route that is no longer current, so the router
            // discards it without re-emitting the unrelated current location.
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(emitCount, Is.EqualTo(2), "A navigated-away Suspend loader's late resolution does not re-emit");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_CurrentLocationIdentityIsUnchanged()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(router.CurrentLocation, Is.SameAs(otherLocation),
                "A stale resolution leaves the current location identity unchanged, so consumers do not churn");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderResolvesAfterNavigateAway_When_Resolving_Then_LoaderDataNotPolluted()
            => UniTask.ToCoroutine(async () =>
        {
            // A navigated-away Suspend loader's late result belongs to a superseded round; it must not land in
            // the live loader data of the unrelated current location, where UseLoaderData / GetLoaderData read it.
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderData, Is.Empty,
                "A navigated-away Suspend loader's late result does not pollute the current location's loader data");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderFailsAfterNavigateAway_When_Failing_Then_ErrorNotRecorded()
            => UniTask.ToCoroutine(async () =>
        {
            // A navigated-away Suspend loader's late failure belongs to a superseded round; it must not record
            // an error under the unrelated current location nor surface via UseRouteError.
            // Arrange
            var tcs = new UniTaskCompletionSource<object>();
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
            await UniTask.Yield();

            // Assert
            Assert.That(router.CurrentLoaderErrors, Is.Empty,
                "A navigated-away Suspend loader's late failure does not record an error under the current location");
        });

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
            // Arrange + Act
            var router = new Router(_routes);

            // Assert
            Assert.That(router.ScopeFactory, Is.Null);
        }

        #endregion

        #region Concurrent navigation cancellation

        [UnityTest]
        public IEnumerator Given_ConcurrentNavigationDuringBlockerAwait_When_SecondTakesOver_Then_FirstReturnsCancelled()
            => UniTask.ToCoroutine(async () =>
        {
            // An async Blocker await is exactly the window where a second navigation can take over. The
            // first nav's UniTask.Never(ct) raises OperationCanceledException on cancellation, which the OCE
            // catch filter maps to Cancelled.
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var _ = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var firstNav = router.NavigateAsync("/about");
            await entered.Task;
            var secondNav = router.NavigateAsync("/home");

            // Act
            var firstResult = await firstNav;
            await secondNav;

            // Assert
            Assert.That(firstResult, Is.EqualTo(NavigationResult.Cancelled));
        });

        [UnityTest]
        public IEnumerator Given_ConcurrentNavigationDuringBlockerAwait_When_SecondTakesOver_Then_SecondSucceeds()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var _ = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var firstNav = router.NavigateAsync("/about");
            await entered.Task;
            var secondNav = router.NavigateAsync("/home");

            // Act
            await firstNav;
            var secondResult = await secondNav;

            // Assert
            Assert.That(secondResult, Is.EqualTo(NavigationResult.Success));
        });

        [UnityTest]
        public IEnumerator Given_ConcurrentNavigationDuringBlockerAwait_When_SecondTakesOver_Then_CommitsLatestLocation()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var _ = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var firstNav = router.NavigateAsync("/about");
            await entered.Task;
            var secondNav = router.NavigateAsync("/home");

            // Act
            await firstNav;
            await secondNav;

            // Assert
            Assert.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "The final committed location reflects the latest nav, not the cancelled first");
        });

        [UnityTest]
        public IEnumerator Given_CallerCancelsTokenDuringBlockerAwait_When_Cancelled_Then_ReturnsCancelledInsteadOfThrowing()
            => UniTask.ToCoroutine(async () =>
        {
            // The OCE catch filter maps caller-token cancellation during the blocker await to Cancelled,
            // symmetrically with the loader phase, so callers branching on `nav != Success` never see an
            // uncaught OperationCanceledException.
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var _ = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.NavigateAsync("/about", cancellationToken: callerCts.Token);
            await entered.Task;

            // Act
            callerCts.Cancel();
            var result = await nav;

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Cancelled));
        });

        [UnityTest]
        public IEnumerator Given_CancelledToken_When_GoBackHitsCachedEntry_Then_ReturnsCancelled()
            => UniTask.ToCoroutine(async () =>
        {
            // A cached Back/Forward navigation commits without reaching the loader-phase cancellation check, so a
            // superseded attempt must unwind at the blocker boundary instead. The blocker phase observes the
            // already-cancelled token even with no blocker registered (CheckAsync would otherwise return false
            // and fall through to the cached commit).
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            Assume.That(router.CanGoBack, Is.True, "Precondition: history has a previous cached entry");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = await router.GoBack(cts.Token);

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Cancelled));
        });

        [UnityTest]
        public IEnumerator Given_CancelledToken_When_GoBackHitsCachedEntry_Then_DoesNotCommitLocation()
            => UniTask.ToCoroutine(async () =>
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
                "A cancelled cached Back does not commit the previous entry");
        });

        // Blocker test infra: the first nav parks on UniTask.Never(ct) (raises OCE on cancellation);
        // subsequent navs pass through without blocking. A per-invocation counter keeps the two navs from
        // sharing TCS state.
        private static (Func<NavigationAttempt, CancellationToken, UniTask<bool>> Check, UniTaskCompletionSource Entered) MakeOneShotBlocker()
        {
            var entered = new UniTaskCompletionSource();
            int invocationCount = 0;
            UniTask<bool> Check(NavigationAttempt _, CancellationToken ct)
            {
                var n = Interlocked.Increment(ref invocationCount);
                if (n == 1)
                {
                    entered.TrySetResult();
                    return UniTask.Never<bool>(ct);
                }
                return UniTask.FromResult(false);
            }
            return (Check, entered);
        }

        #endregion

        private sealed class TestRouteScopeFactory : IRouteScopeFactory
        {
            public int CreateScopeCount { get; private set; }

            public IRouteScope CreateScope(RouteDefinition? route, IRouteScope? parent)
            {
                CreateScopeCount++;
                return new TestRouteScope();
            }
        }

        private sealed class TestRouteScope : IRouteScope
        {
            public bool IsDisposed { get; private set; }

            public T Resolve<T>() => throw new NotImplementedException();

            public void Dispose() => IsDisposed = true;
        }
    }
}
