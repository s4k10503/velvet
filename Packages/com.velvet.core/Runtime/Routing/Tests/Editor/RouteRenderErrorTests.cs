// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a route's errorElement does for an error its element throws while rendering, as React
    /// Router's RenderErrorBoundary does.
    /// <list type="bullet">
    /// <item>The errorElement of the route, or of the nearest route above it that declares one, renders in
    /// place of the element, and <c>Hooks.UseRouteError</c> returns the error beneath it; with none in the
    /// chain the root renders the default one, which shows the error's message.</item>
    /// <item>Navigating away from the errored route renders the route navigated to, and navigating back renders
    /// the errored route afresh; a render at the same location keeps the errorElement.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteRenderErrorTests
    {
        private VisualElement _root = null!;
        private static bool s_throws;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throws = false;
            s_router = null;
            s_setHostTick = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private bool HasLabel(string text) => _root.FindLabelByText(text) != null;

        [Test]
        public void Given_ARouteWhoseElementThrowsWhileRendering_When_ItDeclaresAnErrorElement_Then_TheErrorElementRendersTheError()
        {
            // Arrange
            s_throws = true;
            var router = SiblingRoutes("/a");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);

            // Assert
            Assert.That(HasLabel("a-error:a-boom"), Is.True);
        }

        [Test]
        public void Given_AChildRouteWhoseElementThrowsWhileRendering_When_OnlyItsParentDeclaresAnErrorElement_Then_TheParentsErrorElementRendersTheError()
        {
            // Arrange
            s_throws = true;
            var router = new Router(V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    errorElement: V.Component(ParentErrorRender, key: "parent-error"),
                    children: new[] { V.Route(path: "child", element: V.Component(ThrowingRender, key: "child")) })));
            router.NavigateSync("/parent/child");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);

            // Assert — the layout is read with it, since the parent's errorElement renders in place of it
            Assert.That((HasLabel("parent-error:a-boom"), HasLabel("parent-layout")), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ARootRouteWhoseElementThrowsWhileRendering_When_NoRouteDeclaresAnErrorElement_Then_TheDefaultErrorElementShowsTheMessage()
        {
            // Arrange
            s_throws = true;
            var router = new Router(V.Routes(V.Route(path: "a", element: V.Component(ThrowingRender, key: "a"))));
            router.NavigateSync("/a");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: a-boom");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(HasLabel("a-boom"), Is.True);
        }

        [Test]
        public void Given_AnErroredRoute_When_TheRouterNavigatesAway_Then_TheRouteNavigatedToRenders()
        {
            // Arrange
            s_throws = true;
            var router = SiblingRoutes("/a");
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);
            var errored = HasLabel("a-error:a-boom");

            // Act
            router.NavigateSync("/b");
            mounted.FlushStateForTest();

            // Assert — the errored reading is folded in, since a route that never errored navigates away as well
            Assert.That((errored, HasLabel("b")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnErroredRouteNavigatedAwayFrom_When_TheRouterNavigatesBackWithTheElementNoLongerThrowing_Then_TheRouteRendersAfresh()
        {
            // Arrange
            s_throws = true;
            var router = SiblingRoutes("/a");
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);
            var errored = HasLabel("a-error:a-boom");
            s_throws = false;
            router.NavigateSync("/b");
            mounted.FlushStateForTest();

            // Act
            router.NavigateSync("/a");
            mounted.FlushStateForTest();

            // Assert
            Assert.That((errored, HasLabel("a"), HasLabel("a-error:a-boom")), Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_AnErroredRoute_When_TheRouterProviderRendersAgainAtTheSameLocation_Then_TheErrorElementStays()
        {
            // Arrange
            s_throws = true;
            s_router = SiblingRoutes("/a");
            using var mounted = V.Mount(_root, V.Component(ProviderHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;

            // Act
            s_setHostTick!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the host's tick is read too, since a host that never rendered again leaves the error as well
            Assert.That((HasLabel("host:1"), HasLabel("a-error:a-boom")), Is.EqualTo((true, true)));
        }

        private static Router? s_router;
        private static Action<int>? s_setHostTick;

        [Component(Compiler = false)]
        private static VNode ProviderHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHostTick = setTick;
            return V.Div(children: new VNode[] { V.Label(text: "host:" + tick), V.RouterProvider(s_router!) });
        }

        private static Router SiblingRoutes(string start)
        {
            var router = new Router(V.Routes(
                V.Route(
                    path: "a",
                    element: V.Component(ThrowingRender, key: "a"),
                    errorElement: V.Component(AErrorRender, key: "a-error")),
                V.Route(path: "b", element: V.Component(BRender, key: "b"))));
            router.NavigateSync(start);
            return router;
        }

        [Component(Compiler = false)]
        private static VNode ThrowingRender()
        {
            if (s_throws) throw new InvalidOperationException("a-boom");
            return V.Label(text: "a");
        }

        [Component(Compiler = false)]
        private static VNode AErrorRender() => V.Label(text: "a-error:" + Hooks.UseRouteError()?.Message);

        [Component(Compiler = false)]
        private static VNode BRender() => V.Label(text: "b");

        [Component(Compiler = false)]
        private static VNode ParentLayoutRender()
            => V.Div(children: new VNode[] { V.Label(text: "parent-layout"), V.Outlet() });

        [Component(Compiler = false)]
        private static VNode ParentErrorRender() => V.Label(text: "parent-error:" + Hooks.UseRouteError()?.Message);
    }
}
