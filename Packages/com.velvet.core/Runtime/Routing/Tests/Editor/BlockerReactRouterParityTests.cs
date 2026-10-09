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
    /// <item>The router consults only the Blocker registered last, and warns each time it does so with more
    /// than one registered.</item>
    /// <item>The Blocker is consulted before the path is matched, so a path no route matches is put to it and
    /// a Guard is not asked about a path the Blocker stopped; nothing of the attempt is published while it is
    /// consulted, and a blocked attempt leaves the navigation already in flight alone.</item>
    /// <item>A kept <c>Proceed</c> throws while the Blocker is not Blocked, and releases its own navigation
    /// while a newer one is held. A kept <c>Reset</c> returns the Blocker to Idle whatever its status.</item>
    /// <item>A Blocker stays Proceeding while a navigation that took over from the one it released is under
    /// way.</item>
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

            // Assert — the result rides along, because an attempt that never reached the router asks no Guard
            // either.
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
                seen = $"{router.Navigation.State} {router.Navigation.Location?.Path ?? "none"}";
                return true;
            }, new RouteBlockerState());

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(seen, Is.EqualTo("Idle none"));
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

        [Test]
        public void Given_ANavigationThatTookOverFromAReleasedOne_When_TheReleasedOneUnwinds_Then_TheBlockerIsStillProceeding()
        {
            // Arrange — the released navigation parks on its loader, and the one taking over parks on its own.
            var released = new VelvetTaskCompletionSource<object>();
            var takingOver = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home", Route("home"),
                Route("a", loader: (_, _) => released.Task),
                Route("b", loader: (_, _) => takingOver.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/a");
            state.Proceed();
            router.NavigateAsync("/b").Forget();

            // Act — the released navigation's loader answers, and the navigation finds it was taken over.
            released.TrySetResult("a-data");

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Proceeding));
        }

        // GREEN_ON_BASE(characterization): the base returns the Blocker to Idle here as well.
        // It did so as the released navigation unwound; the change leaves it Proceeding until the navigation
        // that took over ends, which is the settle this pins.
        [Test]
        public void Given_ANavigationThatTookOverFromAReleasedOne_When_ItIsCancelledWithNoneLeftUnderWay_Then_TheBlockerReturnsToIdle()
        {
            // Arrange — as above, and the navigation taking over is one its caller can cancel.
            var released = new VelvetTaskCompletionSource<object>();
            var takingOver = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home", Route("home"),
                Route("a", loader: (_, _) => released.Task),
                Route("b", loader: (_, _) => takingOver.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/a");
            state.Proceed();
            using var cancellation = new System.Threading.CancellationTokenSource();
            router.NavigateAsync("/b", NavigationMode.Push, cancellation.Token).Forget();
            released.TrySetResult("a-data");
            cancellation.Cancel();

            // Act — the cancelled navigation's loader answers, and the navigation finds it was cancelled.
            takingOver.TrySetResult("b-data");

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_AProceedKeptFromAForwardStepWhoseEntryIsGone_When_ItRunsDuringANewerBlock_Then_TheBlockerReturnsToIdle()
        {
            // Arrange — the Blocker blocks Forward steps and one path. The Forward step it blocks first loses
            // its entry to a Push the Blocker lets through, and a later block is what lets the kept Proceed run.
            var router = BuildRouter("/a", Route("a"), Route("b"), Route("c"), Route("x"));
            router.NavigateSync("/b");
            router.GoBackSync();
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(
                args => args.HistoryAction == NavigationMode.Forward || args.NextLocation.Path == "/x", state);
            router.GoForwardSync();
            Action proceedForward = state.Proceed;
            router.NavigateSync("/c");
            router.NavigateSync("/x");

            // Act — the Forward step it releases has no entry to land on.
            proceedForward();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
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
        public void Given_ABlockHeldByABlockerNoLongerRegisteredLast_When_ANavigationCommits_Then_ItReturnsToIdle()
        {
            // Arrange — the first Blocker blocks while it is the last registered; the one registered after it
            // lets the next navigation through.
            var router = BuildRouter("/home", Route("home"), Route("a"), Route("b"));
            var first = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, first);
            router.NavigateSync("/a");
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/b");

            // Assert
            Assert.That((result, first.Status), Is.EqualTo((NavigationResult.Success, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_AReleasedNavigationTheNextBlockerBlocks_When_ItEnds_Then_TheReleasingBlockerStaysProceeding()
        {
            // Arrange — the first Blocker holds the navigation, and a second registered after it is the one
            // the released navigation is put to.
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var releasing = new RouteBlockerState();
            var holding = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, releasing);
            router.NavigateSync("/other");
            router.RouteBlockerManager.Register(_ => true, holding);

            // Act
            releasing.Proceed();

            // Assert
            Assert.That((releasing.Status, holding.Status),
                Is.EqualTo((RouteBlockerStatus.Proceeding, RouteBlockerStatus.Blocked)));
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
            // it; the first's deps then change.
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
