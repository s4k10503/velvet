using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Router"/> resolves declarative redirects and route Guards.
    /// <list type="bullet">
    /// <item>A route's <c>RedirectTo</c> sends navigation to the target in Replace mode, following a chain of
    /// redirects to its final target; a redirect cycle yields <see cref="NavigationResult.Error"/>.</item>
    /// <item>A Guard returning null lets navigation pass; returning a path redirects there; the Guard receives
    /// the matched route's params.</item>
    /// <item>A Guard redirect during Back replaces the previous history entry (the index lands on it and
    /// forward navigation remains available), while a failed Guard redirect rolls the history index back.</item>
    /// <item>A Guard redirect during Forward redirects to the Guard target.</item>
    /// <item>A redirect route reached by Forward replaces its history entry, so the forward target becomes the
    /// redirect destination.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RedirectTests
    {
        // Router.Current is global singleton state; dispose between tests.
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
            // The Guard redirect runs in Replace mode and overwrites history[0] with /login, so the index lands
            // on it with no further back step but a forward step still available to the untouched /other entry.
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

            // Assert
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
        public void Given_FailedGuardRedirectDuringBack_When_Rejected_Then_HistoryIndexIsRolledBack()
        {
            // The provisional Back index decrement is undone when the guard redirect fails, leaving the router
            // on /a at index 1.
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

        #region Push with failed redirect (history rollback)

        [Test]
        public void Given_PushRedirectFailsWithForwardHistory_When_RolledBack_Then_ForwardEntryIsPreserved()
        {
            // history = [/home, /page]; GoBack to /home leaves /page available forward. Pushing /admin truncates
            // the forward /page (Push semantics) and provisionally appends /admin; the guard then redirects to a
            // missing route and FAILS. The rollback must restore the prior history EXACTLY — including the
            // truncated forward /page — not leave the ghost /admin in the forward slot.
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

            // Assert — forward navigation reaches the preserved /page, not the rolled-back ghost /admin.
            router.GoForwardSync();
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/page"),
                "A failed push-redirect with forward history must restore the truncated forward entry");
        }

        #endregion

        #region Forward with redirect

        [Test]
        public void Given_ForwardEntryIsRedirectRoute_When_GoForward_Then_LandsOnRedirectTarget()
        {
            // Navigating to /old pushes it provisionally, then the redirect's Replace overwrites it with /new,
            // so a later GoForward from /home resolves to /new.
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
