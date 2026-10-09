using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="Hooks.UseErrorBoundary"/> passes over a route's error boundary, as
    /// react-error-boundary's context passes over React Router's RenderErrorBoundary: under a route with an
    /// errorElement and no boundary of the application's own, it throws, and the route's boundary catches that.
    /// </summary>
    [TestFixture]
    internal sealed class RouteUseErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [Test]
        public void Given_ARouteWithAnErrorElementAndNoBoundaryOfItsOwn_When_ItsElementCallsUseErrorBoundary_Then_TheErrorElementRendersInvalidOperationException()
        {
            // Arrange
            var router = new Router(V.Routes(V.Route(
                path: "a",
                element: V.Component(CallerRender, key: "a"),
                errorElement: V.Component(ErrorRender, key: "a-error"))));
            router.NavigateSync("/a");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router), CaughtErrors.Unlogged);

            // Assert
            Assert.That(_root.FindLabelByText("a-error:" + nameof(InvalidOperationException)), Is.Not.Null);
        }

        [Component(Compiler = false)]
        private static VNode CallerRender()
        {
            Hooks.UseErrorBoundary();
            return V.Label(text: "a");
        }

        [Component(Compiler = false)]
        private static VNode ErrorRender() => V.Label(text: "a-error:" + Hooks.UseRouteError()?.GetType().Name);
    }
}
