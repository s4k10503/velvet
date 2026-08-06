using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a cancelled navigation leaves behind when it unwinds by exception rather than by
    /// return value. A Blocker or a Guard redirect that honors its cancellation token raises
    /// OperationCanceledException out of an await that sits after the provisional history mutations, so
    /// each of those mutations needs undoing on the exceptional path as well as the normal one.
    /// <list type="bullet">
    /// <item>The provisional Back/Forward index is restored, so the history keeps describing the entry the
    /// user is still on.</item>
    /// <item>The provisional Push entry a Guard redirect appended is removed again.</item>
    /// <item><see cref="RouterStatus"/> returns to Idle, so <c>UseNavigation</c> stops reporting a pending
    /// navigation that no longer exists.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterCancellationUnwindTests
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

        // Same isolation rule as RouterTests: Router.Current is a global that each new Router() overwrites.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledDuringBackAwait_Then_HistoryIndexIsRestored()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.GoBack(callerCts.Token);
            await entered.Task;
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "Precondition: the Back is still parked in the blocker and has committed nothing");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1),
                "A cancelled Back leaves the history pointing at the entry the user never left");
        });

        [UnityTest]
        public IEnumerator Given_GuardRedirect_When_CancelledDuringRedirectBlockerAwait_Then_ProvisionalPushIsUndone()
            => UniTask.ToCoroutine(async () =>
        {
            // The redirect target's own navigation is what parks on the blocker, so the cancellation surfaces
            // inside the await that RunGuardChecks wraps around it — past the point where the originating Push
            // entry was appended for the redirect to overwrite.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => "/target"),
                    Route("target"),
                }));
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.NavigateAsync("/guarded", NavigationMode.Push, callerCts.Token);
            await entered.Task;

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That($"{HistoryCountOf(router)}/{router.HistoryIndex}", Is.EqualTo("1/0"),
                "A cancelled redirect restores the history list and the index together; restoring either alone "
                + "still leaves the stack describing a navigation that never happened");
        });

        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledWithNoFollowUpNavigation_Then_StatusReturnsToIdle()
            => UniTask.ToCoroutine(async () =>
        {
            // A caller-supplied token rather than a second navigation taking over: the newer navigation
            // would commit and set Status itself, masking whether this one cleaned up after itself.
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.NavigateAsync("/about", NavigationMode.Push, callerCts.Token);
            await entered.Task;
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "Precondition: the cancelled navigation never commits a location");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Idle),
                "With no navigation left in flight, UseNavigation must not keep reporting one");
        });

        // The history list has no accessor of its own, and adding one would put a test-only member on a
        // production type.
        private static int HistoryCountOf(Router router)
        {
            var field = typeof(Router).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((ICollection)field.GetValue(router)).Count;
        }
    }
}
