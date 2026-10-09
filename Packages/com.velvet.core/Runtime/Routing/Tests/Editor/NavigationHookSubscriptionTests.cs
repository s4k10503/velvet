using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestMounts;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>Hooks.UseNavigation</c> reports a navigation that started between its render and the effect that
    /// subscribes it to the router.
    /// </summary>
    [TestFixture]
    internal sealed class NavigationHookSubscriptionTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.State = default;
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

            [Component]
            public static VNode Render()
            {
                State = Hooks.UseNavigation();
                return V.Label(text: "capture");
            }
        }

        // GREEN_ON_BASE(characterization): the base reconciles at the subscription the same way.
        // Its subscriber reads a status where this branch's reads the navigation itself.
        [Test]
        public void Given_ANavigationStartedBeforeTheHookSubscribes_When_TheSubscriptionIsCommitted_Then_TheHookReportsIt()
        {
            // The navigation starts between the render and the effect that subscribes, so no event reaches
            // the hook for it.
            // Arrange
            var loader = new VelvetTaskCompletionSource<object>();
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("data", element: V.Component(StubB), loader: (ctx, ct) => loader.Task),
            });
            router.NavigateSync("/home");
            using var mounted = V.Mount(_root, WithRouter(router, V.Component(Capture.Render, key: "cap")));
            router.NavigateAsync("/data").Forget();

            // Act
            mounted.FlushEffectsForTest();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.State.State, Is.EqualTo(NavigationLifecycle.Loading));
        }
    }
}
