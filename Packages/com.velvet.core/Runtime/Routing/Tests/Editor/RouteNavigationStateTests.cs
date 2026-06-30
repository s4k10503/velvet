// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>Hooks.UseNavigation</c> and the
    /// declarative <c>V.Navigate</c> redirect element.
    /// <list type="bullet">
    /// <item><c>UseNavigation</c> reports <see cref="NavigationLifecycle.Idle"/> when no navigation is in
    /// flight and exposes the current location.</item>
    /// <item><c>UseNavigation</c> re-renders the component and reflects the latest state as the router's
    /// status transitions.</item>
    /// <item><c>V.Navigate</c> navigates to its target once on mount and renders nothing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteNavigationStateTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.Reset();
            NavCapture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static class Capture
        {
            public static NavigationState State;
            public static int RenderCount;

            public static void Reset()
            {
                State = default;
                RenderCount = 0;
            }

            [Component]
            public static VNode Render()
            {
                State = Hooks.UseNavigation();
                RenderCount++;
                return V.Label(text: "capture");
            }
        }

        // Hosts a V.Navigate whose target lives in state, so a test can change the `to` prop by driving the
        // setter and re-rendering this wrapper.
        private static class NavCapture
        {
            public static System.Action<string> SetTarget;

            public static void Reset() => SetTarget = null;

            [Component]
            public static VNode Render()
            {
                var (target, set) = Hooks.UseState("/a");
                SetTarget = set;
                return V.Navigate(target, key: "nav");
            }
        }

        private MountedTree MountWith(Router router, VNode child)
            => V.Mount(_root,
                V.Provider(RouterContext.Location, router.CurrentLocation,
                    children: new[] { child }));

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_StateIsIdle()
        {
            // Arrange
            var router = new Router(new[] { Route("home", element: V.Component(StubA)) });
            router.NavigateSync("/home");

            // Act
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));

            // Assert
            Assert.That(Capture.State.State, Is.EqualTo(NavigationLifecycle.Idle));
        }

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_LocationIsCurrent()
        {
            // Arrange
            var router = new Router(new[] { Route("home", element: V.Component(StubA)) });
            router.NavigateSync("/home");

            // Act
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));

            // Assert
            Assert.That(Capture.State.Location!.Path, Is.EqualTo("/home"));
        }

        [Test]
        public void Given_MountedNavigationHook_When_NavigationOccurs_Then_ComponentReRendersAndStaysIdleAtRest()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("about", element: V.Component(StubB)),
            });
            router.NavigateSync("/home");
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));
            // The status/location subscription is wired in a UseEffect, so it must be flushed before the
            // navigation below can reach the hook.
            mounted.FlushEffectsForTest();
            var rendersBefore = Capture.RenderCount;

            // Act: a settled navigation fires the status/location events, scheduling a re-render.
            router.NavigateSync("/about");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.RenderCount, Is.GreaterThan(rendersBefore));
            Assert.That(Capture.State.State, Is.EqualTo(NavigationLifecycle.Idle));
            Assert.That(Capture.State.Location!.Path, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_NavigateElement_When_Mounted_Then_RedirectsToTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("dest", element: V.Component(StubB)),
            });
            router.NavigateSync("/start");

            // Act: mounting V.Navigate fires the redirect as a mount-time effect.
            using var mounted = MountWith(router, V.Navigate("/dest", key: "nav"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/dest"));
        }

        [Test]
        public void Given_NavigateElementWithReplace_When_Mounted_Then_ReplacesHistoryEntry()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("dest", element: V.Component(StubB)),
            });
            router.NavigateSync("/start");

            // Act
            using var mounted = MountWith(router, V.Navigate("/dest", replace: true, key: "nav"));
            mounted.FlushEffectsForTest();

            // Assert: replace overwrites the single starting entry, leaving nothing to go back to.
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/dest"));
            Assert.That(router.CanGoBack, Is.False);
        }

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_ReRendersExactlyOnce()
        {
            // The status and location events fired during one synchronous navigation collapse to a single
            // distinct state value ({Idle, newLocation}); UseState bails on the equal duplicates, so the
            // component re-renders exactly once rather than flickering through the transient Loading state.
            // Arrange
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("about", element: V.Component(StubB)),
            });
            router.NavigateSync("/home");
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));
            mounted.FlushEffectsForTest();
            var rendersBefore = Capture.RenderCount;

            // Act
            router.NavigateSync("/about");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.RenderCount, Is.EqualTo(rendersBefore + 1));
        }

        [Test]
        public void Given_NavigateElement_When_ToPropChanges_Then_RedirectsToNewTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("a", element: V.Component(StubB)),
                Route("b", element: V.Component(StubA)),
            });
            router.NavigateSync("/start");
            using var mounted = MountWith(router, V.Component(NavCapture.Render, key: "wrap"));
            mounted.FlushEffectsForTest();
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/a"));

            // Act: change the target prop; the Navigate element re-renders with a new `to`, and its effect
            // keyed on To re-runs the redirect.
            NavCapture.SetTarget!("/b");
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/b"));
        }
    }
}
