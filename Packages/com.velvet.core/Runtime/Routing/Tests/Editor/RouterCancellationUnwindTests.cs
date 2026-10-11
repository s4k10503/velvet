using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Bounded so a case awaiting a task that never completes fails at thirty seconds rather than at the
    // runner's own bound, which UnityRunnerDefaultTimeoutTests pins.
    [Timeout(30000)]
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

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [Test]
        public void Given_ASubscriberNavigatingToAnUnmatchedPathOnTheLoadingEvent_When_TheAttemptGoesOn_Then_ItIsNotDispossessed()
        {
            // The navigation is published from inside the attempt that made it, so a subscriber navigating
            // from there reaches the router while that attempt holds the claim and has committed nothing. The
            // unmatched result is folded in because a case that arranged no inner navigation would otherwise
            // still see the outcome it expects.
            // Arrange
            var router = new Router(_routes);
            router.NavigateSync("/home");
            var innerResult = NavigationResult.Success;
            var navigatedFromTheEvent = false;
            router.OnNavigationChanged += navigation =>
            {
                if (navigation.State != NavigationLifecycle.Loading || navigatedFromTheEvent) return;
                navigatedFromTheEvent = true;
                innerResult = router.NavigateSync("/no-such-route");
            };

            // Act
            var outerResult = router.NavigateSync("/about");

            // Assert
            Assert.That(
                $"inner={innerResult} outer={outerResult} path={router.CurrentLocation?.Path}",
                Is.EqualTo("inner=NotFound outer=Success path=/about"));
        }

        [Test]
        public void Given_AGuardThatDisposesTheRouter_When_ItRedirects_Then_TheRedirectPublishesNoDestination()
        {
            // Arrange
            Router router = null;
            router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ =>
                    {
                        router.Dispose();
                        return "/target";
                    }),
                    Route("target"),
                }));

            // Act
            var result = router.NavigateSync("/guarded");

            // Assert
            Assert.That($"result={result} pending={router.Navigation.Location?.Path ?? "none"}",
                Is.EqualTo("result=Cancelled pending=none"),
                "A redirect taken on a disposed router publishes no destination");
        }
    }
}
