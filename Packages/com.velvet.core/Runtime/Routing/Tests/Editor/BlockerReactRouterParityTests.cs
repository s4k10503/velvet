using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a Blocker's behaviour follows React Router's <c>useBlocker</c> and the data router
    /// behind it.
    /// <list type="bullet">
    /// <item>The router consults only the Blocker registered last, and warns while more than one is
    /// registered.</item>
    /// <item>The Blocker is consulted before the path is matched, so a path no route matches is put to it and
    /// a Guard is not asked about a path the Blocker stopped; nothing of the attempt is published while it is
    /// consulted, and a blocked attempt leaves the navigation already in flight alone.</item>
    /// <item>A kept <c>Proceed</c> is bound to the block it was handed out for: it throws once that block is
    /// over, and releases its own navigation while a newer one is held. A kept <c>Reset</c> returns the
    /// Blocker to Idle whatever its status.</item>
    /// <item>A block stands until a navigation commits, a disposal removes the Blocker with its state, and a
    /// re-registration keeps the Blocker's place in the order.</item>
    /// <item>A component holding a Blocker re-renders when its state changes.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class BlockerReactRouterParityTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        #region Which Blocker is consulted

        [Test]
        public void Given_ABlockingBlockerRegisteredBeforeAnAllowingOne_When_Navigating_Then_TheNavigationCommits()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_TwoBlockingBlockers_When_Navigating_Then_OnlyTheLastRegisteredHoldsTheNavigation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var first = new RouteBlockerState();
            var last = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, first);
            router.RouteBlockerManager.Register(_ => true, last);

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That((first.Status, last.Status), Is.EqualTo((RouteBlockerStatus.Idle, RouteBlockerStatus.Blocked)));
        }

        [Test]
        public void Given_TwoBlockers_When_Navigating_Then_TheRouterWarnsItSupportsOne()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());
            LogAssert.Expect(LogType.Warning, new Regex("A router only supports one blocker at a time"));

            // Act
            router.NavigateSync("/other");

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        #endregion

        #region Where the Blocker sits in the navigation

        [Test]
        public void Given_ABlocker_When_NavigatingToAPathNoRouteMatches_Then_ItBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"));
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/nowhere");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_ABlockerThatBlocks_When_TheBlockedPathHasAGuard_Then_TheGuardIsNotAsked()
        {
            // Arrange
            var guardCalls = 0;
            var router = BuildRouter("/home", Route("home"), Route("admin", guard: _ =>
            {
                guardCalls++;
                return null;
            }));
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/admin");

            // Assert — the result rides along because an attempt the Blocker let through asks no Guard only if
            // the route has none.
            Assert.That($"result={result} guards={guardCalls}", Is.EqualTo("result=Blocked guards=0"));
        }

        [UnityTest]
        public IEnumerator Given_ANavigationLoading_When_ALaterOneIsBlocked_Then_TheLoadingOneStillCommits()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the Blocker lets the first navigation through and blocks the second.
            var loading = new VelvetTaskCompletionSource<object>();
            var checks = 0;
            var router = BuildRouter("/home", Route("home"), Route("slow", loader: (_, _) => loading.Task),
                Route("other"));
            router.RouteBlockerManager.Register(_ => ++checks == 2, new RouteBlockerState());
            var slow = router.NavigateAsync("/slow");
            var blocked = router.NavigateSync("/other");

            // Act
            loading.TrySetResult("slow-data");
            var slowResult = await slow;

            // Assert
            Assert.That($"blocked={blocked} slow={slowResult} at={router.CurrentLocation.Path}",
                Is.EqualTo("blocked=Blocked slow=Success at=/slow"));
        });

        [Test]
        public void Given_ABlocker_When_ItIsConsulted_Then_TheRouterHasPublishedNothingOfTheAttempt()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            string seen = null;
            router.RouteBlockerManager.Register(_ =>
            {
                seen = $"{router.Status} {router.PendingLocation?.Path ?? "none"}";
                return true;
            }, new RouteBlockerState());

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(seen, Is.EqualTo("Ready none"));
        }

        #endregion

        #region What a kept Proceed or Reset does

        [Test]
        public void Given_AProceedKeptFromABlock_When_ItRunsAfterTheBlockIsReset_Then_ItThrows()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/other");
            Action proceed = state.Proceed;
            state.Reset();

            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => proceed());
        }

        [Test]
        public void Given_AProceedKeptFromTheFirstBlock_When_ItRunsWhileASecondNavigationIsHeld_Then_TheFirstLands()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("a"), Route("b"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/a");
            Action proceedFirst = state.Proceed;
            router.NavigateSync("/b");

            // Act
            proceedFirst();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/a"));
        }

        [Test]
        public void Given_AResetKeptFromABlock_When_ItRunsWhileTheReleasedNavigationLoads_Then_TheBlockerReturnsToIdle()
        {
            // Arrange — the released navigation parks on its loader, which keeps the Blocker Proceeding.
            var loading = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home", Route("home"), Route("other", loader: (_, _) => loading.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/other");
            Action reset = state.Reset;
            state.Proceed();
            var statusWhileLoading = state.Status;

            // Act
            reset();

            // Assert
            Assert.That((statusWhileLoading, state.Status),
                Is.EqualTo((RouteBlockerStatus.Proceeding, RouteBlockerStatus.Idle)));
        }

        #endregion

        #region How long a block stands

        [Test]
        public void Given_ABlock_When_ANavigationTheBlockerLetsThroughIsStillLoading_Then_TheBlockStands()
        {
            // Arrange — the Blocker blocks only the first navigation it is asked about.
            var loading = new VelvetTaskCompletionSource<object>();
            var checks = 0;
            var router = BuildRouter("/home", Route("home"), Route("a"),
                Route("b", loader: (_, _) => loading.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => ++checks == 1, state);
            router.NavigateSync("/a");

            // Act
            router.NavigateAsync("/b").Forget();

            // Assert — the check count rides along because a second navigation the Blocker was not asked about
            // would leave the block standing too.
            Assert.That((checks, state.Status), Is.EqualTo((2, RouteBlockerStatus.Blocked)));
        }

        [Test]
        public void Given_ABlockedBlocker_When_ItsRegistrationIsDisposed_Then_ItReturnsToIdle()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            var registration = router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/other");
            var statusBeforeDispose = state.Status;

            // Act
            registration.Dispose();

            // Assert
            Assert.That((statusBeforeDispose, state.Status),
                Is.EqualTo((RouteBlockerStatus.Blocked, RouteBlockerStatus.Idle)));
        }

        #endregion

        #region UseBlocker

        private static StateUpdater<bool> s_showSecondForm;
        private static StateUpdater<bool> s_reviseFirstForm;

        [Component]
        private static VNode StatusReadingForm()
        {
            var blocker = Hooks.UseBlocker(_ => true);
            return V.Label(text: "status-" + blocker.Status);
        }

        [Component]
        private static VNode FirstForm()
        {
            var (revised, revise) = Hooks.UseState(false);
            s_reviseFirstForm = revise;
            Hooks.UseBlocker(_ => true, revised);
            return V.Label(text: "first");
        }

        [Component]
        private static VNode SecondForm()
        {
            Hooks.UseBlocker(_ => false);
            return V.Label(text: "second");
        }

        [Component]
        private static VNode TwoForms()
        {
            var (showSecond, setShowSecond) = Hooks.UseState(false);
            s_showSecondForm = setShowSecond;
            return V.Div(children: new VNode[]
            {
                V.Component(FirstForm, key: "first"),
                showSecond ? V.Component(SecondForm, key: "second") : null,
            });
        }

        [Test]
        public void Given_AComponentHoldingABlocker_When_TheBlockerBlocks_Then_TheComponentRendersTheBlockedState()
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("home", element: V.Component(StatusReadingForm, key: "form")), Route("other"));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();

            // Act
            router.NavigateSync("/other");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("status-Blocked") != null, Is.True);
        }

        [Test]
        public void Given_ABlockerRegisteredFirst_When_ItsDepsChange_Then_TheOneRegisteredAfterItIsStillTheOneConsulted()
        {
            // Arrange — the second form mounts in a later render than the first, so it is registered after
            // it; the first then re-registers for its changed deps.
            s_showSecondForm = default;
            s_reviseFirstForm = default;
            var router = BuildRouter("/home",
                Route("home", element: V.Component(TwoForms, key: "forms")), Route("other"));
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();
            s_showSecondForm.Invoke(true);
            mounted.FlushStateForTest();
            s_reviseFirstForm.Invoke(true);
            mounted.FlushStateForTest();

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        #endregion
    }
}
