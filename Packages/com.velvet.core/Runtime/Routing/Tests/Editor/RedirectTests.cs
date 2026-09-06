using NUnit.Framework;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RedirectTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        #region Static redirect

        [Test]
        public void Given_StaticRedirectRoute_When_NavigatingToIt_Then_LandsOnTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("old", redirectTo: "/new"),
                Route("new"),
            });

            // Act
            router.NavigateSync("/old");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/new"));
        }

        [Test]
        public void Given_ChainedRedirectRoutes_When_NavigatingToFirst_Then_LandsOnFinalTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("a", redirectTo: "/b"),
                Route("b", redirectTo: "/c"),
                Route("c"),
            });

            // Act
            router.NavigateSync("/a");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/c"));
        }

        [Test]
        public void Given_RedirectCycle_When_Navigating_Then_ReturnsError()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("a", redirectTo: "/b"),
                Route("b", redirectTo: "/a"),
            });

            // Act
            var result = router.NavigateSync("/a");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Error));
        }

        // GREEN_ON_BASE(characterization): the status a refused redirect chain leaves behind.
        // The base writes it from this branch unconditionally, where the report is now conditional on
        // nobody else holding a claim. Delete the `ReportUnclaimedOutcome(RouterStatus.Error)` call in
        // `NavigateCore`'s redirect-limit branch and this is what reddens.
        [Test]
        public void Given_ARedirectCycle_When_ItExhaustsTheRedirectLimit_Then_TheStatusIsError()
        {
            // The case above reads the result the caller gets; this one reads what the router is left
            // reporting, which a pending-UI branch renders from. The result rides along because
            // `RouterStatus.Error` is also what a throw out of the commit leaves, and only a returned
            // result says the chain was refused rather than something thrown past.
            // Arrange
            var router = new Router(new[]
            {
                Route("a", redirectTo: "/b"),
                Route("b", redirectTo: "/a"),
            });

            // Act
            var result = router.NavigateSync("/a");

            // Assert
            Assert.That($"result={result} status={router.Status}", Is.EqualTo("result=Error status=Error"));
        }

        #endregion

        #region Guard

        [Test]
        public void Given_GuardReturningNull_When_Navigating_Then_Passes()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("admin", guard: _ => null),
            });

            // Act
            var result = router.NavigateSync("/admin");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_GuardReturningPath_When_Navigating_Then_RedirectsToGuardTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("admin", guard: _ => "/login"),
                Route("login"),
            });

            // Act
            router.NavigateSync("/admin");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/login"));
        }

        [Test]
        public void Given_GuardOnParamRoute_When_Navigating_Then_GuardReceivesMatchedParams()
        {
            // Arrange
            string receivedId = null;
            var router = new Router(new[]
            {
                Route("user/:id", guard: ctx =>
                {
                    receivedId = ctx.Params["id"];
                    return null;
                }),
            });

            // Act
            router.NavigateSync("/user/42");

            // Assert
            Assert.That(receivedId, Is.EqualTo("42"));
        }

        #endregion

        #region Back with Guard

        [Test]
        public void Given_GuardThatActivates_When_GoBackTriggersRedirect_Then_LandsOnGuardTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("protected", guard: MakeToggleGuard(out var enableGuard)),
                Route("other"),
                Route("login"),
            });
            router.NavigateSync("/protected");
            router.NavigateSync("/other");

            // Act
            enableGuard();
            var result = router.GoBackSync();
            Assume.That(result, Is.EqualTo(NavigationResult.Success), "Precondition: the guard redirect succeeded");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/login"));
        }

        [Test]
        public void Given_GuardRedirectDuringBack_When_LandedOnTarget_Then_ReplacesPreviousEntry()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("protected", guard: MakeToggleGuard(out var enableGuard)),
                Route("other"),
                Route("login"),
            });
            router.NavigateSync("/protected");
            router.NavigateSync("/other");

            // Act
            enableGuard();
            router.GoBackSync();

            // Assert — the index and adjacent-step flags distinguish replacement from a push.
            Assert.That(
                (router.HistoryIndex, router.CanGoBack, router.CanGoForward),
                Is.EqualTo((0, false, true)));
        }

        [Test]
        public void Given_GuardRedirectToMissingRoute_When_GoBack_Then_ReturnsNotFound()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("a"),
                Route("b", guard: MakeToggleGuard(out var enableGuard, redirectTo: "/nonexistent")),
            });
            router.NavigateSync("/b");
            router.NavigateSync("/a");

            // Act
            enableGuard();
            var result = router.GoBackSync();

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.NotFound));
        }

        [Test]
        public void Given_FailedGuardRedirectDuringBack_When_Rejected_Then_HistoryIndexIsUnchanged()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("a"),
                Route("b", guard: MakeToggleGuard(out var enableGuard, redirectTo: "/nonexistent")),
            });
            router.NavigateSync("/b");
            router.NavigateSync("/a");
            Assume.That(router.HistoryIndex, Is.EqualTo(1), "Precondition: positioned on /a at index 1");

            // Act
            enableGuard();
            router.GoBackSync();

            // Assert
            Assert.That((router.CurrentLocation.Path, router.HistoryIndex), Is.EqualTo(("/a", 1)));
        }

        #endregion

        #region Forward with Guard

        [Test]
        public void Given_GuardThatActivates_When_GoForwardTriggersRedirect_Then_LandsOnGuardTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home"),
                Route("protected", guard: MakeToggleGuard(out var enableGuard)),
                Route("login"),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/protected");
            router.GoBackSync();

            // Act
            enableGuard();
            var result = router.GoForwardSync();
            Assume.That(result, Is.EqualTo(NavigationResult.Success), "Precondition: the guard redirect succeeded");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/login"));
        }

        [Test]
        public void Given_GuardReturningNull_When_GoForward_Then_LandsOnForwardEntry()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home"),
                Route("other", guard: _ => null),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/other");
            router.GoBackSync();

            // Act
            var result = router.GoForwardSync();
            Assume.That(result, Is.EqualTo(NavigationResult.Success), "Precondition: the forward step passed the guard");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/other"));
        }

        #endregion

        #region Push with failed redirect

        [Test]
        public void Given_PushRedirectFailsWithForwardHistory_When_RolledBack_Then_ForwardEntryIsPreserved()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home"),
                Route("page"),
                Route("admin", guard: MakeToggleGuard(out var enableGuard, redirectTo: "/nonexistent")),
            });
            router.NavigateSync("/home");
            router.NavigateSync("/page");
            router.GoBackSync();
            Assume.That((router.HistoryIndex, router.CanGoForward), Is.EqualTo((0, true)),
                "Precondition: at /home (index 0) with /page available forward");

            // Act
            enableGuard();
            var pushResult = router.NavigateSync("/admin");
            Assume.That(pushResult, Is.EqualTo(NavigationResult.NotFound),
                "Precondition: the push's guard redirect failed");

            // Assert
            router.GoForwardSync();
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/page"),
                "A push that never arrives must not truncate the forward entry it would have replaced");
        }

        #endregion

        #region Push whose redirect resolves to the current path

        [Test]
        public void Given_AGuardRedirectingToTheCurrentPath_When_Pushed_Then_TheTargetIsAppendedAsItsOwnEntry()
        {
            // Arrange
            var router = BuildRouter("/target", Route("guarded", guard: _ => "/target"), Route("target"));
            Assume.That(router.CanGoBack, Is.False, "Precondition: the start entry is the only one on the stack");

            // Act
            router.NavigateSync("/guarded");

            // Assert
            Assert.That($"{RouterHistoryProbe.PathsOf(router)} idx={router.HistoryIndex}",
                Is.EqualTo("/target,/target idx=1"));
        }

        [Test]
        public void Given_APushedGuardRedirect_When_TheBlockerIsAsked_Then_ItIsToldTheStepIsAPush()
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("home"), Route("guarded", guard: _ => "/target"), Route("target"));
            var seen = (NavigationMode?)null;
            using var registration = router.RouteBlockerManager.Register(
                attempt =>
                {
                    seen ??= attempt.NavigationMode;
                    return false;
                },
                new RouteBlockerState());

            // Act
            router.NavigateSync("/guarded");

            // Assert
            Assert.That($"{seen} to {router.CurrentLocation.Path}", Is.EqualTo("Push to /target"));
        }

        #endregion

        #region Forward with redirect

        [Test]
        public void Given_ForwardEntryIsRedirectRoute_When_GoForward_Then_LandsOnRedirectTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start"),
                Route("home"),
                Route("old", redirectTo: "/new"),
                Route("new"),
            });
            router.NavigateSync("/start");
            router.NavigateSync("/home");
            router.NavigateSync("/old");
            Assume.That(router.CurrentLocation.Path, Is.EqualTo("/new"), "Precondition: the redirect committed to /new");
            router.GoBackSync();
            Assume.That(router.CurrentLocation.Path, Is.EqualTo("/home"), "Precondition: back stepped to /home");

            // Act
            router.GoForwardSync();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/new"));
        }

        #endregion
    }
}
