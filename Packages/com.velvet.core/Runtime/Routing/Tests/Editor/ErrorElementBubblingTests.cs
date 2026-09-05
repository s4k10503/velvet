// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class ErrorElementBubblingTests
    {
        private VisualElement _root = null!;
        private static Exception? s_capturedRouteError;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_capturedRouteError = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        #region Render targets

        [Component]
        private static VNode ParentLayoutRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "parent-layout"),
                V.Outlet(),
            });

        [Component]
        private static VNode ChildRender() => V.Label(text: "child");

        [Component]
        private static VNode ParentErrorRender() => V.Label(text: "parent-error");

        [Component]
        private static VNode ParentErrorCaptureRender()
        {
            s_capturedRouteError = Hooks.UseRouteError();
            return V.Label(text: "parent-error-capture");
        }

        [Component]
        private static VNode ChildErrorRender() => V.Label(text: "child-error");

        [Component]
        private static VNode GrandparentLayoutRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "grandparent-layout"),
                V.Outlet(),
            });

        #endregion

        #region Helpers

        private MountedTree MountWithRouter(Router router) => V.Mount(_root, V.RouterProvider(router));

        private static bool HasLabel(VisualElement element, string text) => element.FindLabelByText(text) != null;

        private static Func<RouteLoaderContext, System.Threading.CancellationToken, VelvetTask<object>> ThrowingLoader(string message)
            => (_, _) => throw new InvalidOperationException(message);

        private Router BuildParentBoundaryRouter()
        {
            var routes = V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    errorElement: V.Component(ParentErrorRender, key: "parent-error"),
                    children: new[]
                    {
                        V.Route(
                            path: "child",
                            element: V.Component(ChildRender, key: "child"),
                            loader: ThrowingLoader("child-boom")),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/parent/child");
            return router;
        }

        #endregion

        #region Child error bubbles to parent boundary

        [Test]
        public void Given_ChildErrorNoChildBoundary_When_Rendered_Then_ParentErrorElementSurfaces()
        {
            // Arrange
            var router = BuildParentBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-error"), Is.True,
                "The parent's ErrorElement catches the child loader error");
        }

        [Test]
        public void Given_ChildErrorNoChildBoundary_When_Rendered_Then_ParentNormalElementIsReplaced()
        {
            // Arrange
            var router = BuildParentBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-layout"), Is.False,
                "The parent's normal Element is replaced by its ErrorElement at the boundary");
        }

        [Test]
        public void Given_ChildErrorNoChildBoundary_When_Rendered_Then_RoutesBelowBoundaryDoNotRender()
        {
            // Arrange
            var router = BuildParentBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child"), Is.False, "Routes below the boundary do not render");
        }

        [Test]
        public void Given_ChildErrorBubbledToParent_When_UseRouteErrorAtBoundary_Then_ResolvesDescendantError()
        {
            // Arrange
            var routes = V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    errorElement: V.Component(ParentErrorCaptureRender, key: "parent-error"),
                    children: new[]
                    {
                        V.Route(
                            path: "child",
                            element: V.Component(ChildRender, key: "child"),
                            loader: ThrowingLoader("child-boom")),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/parent/child");

            // Act
            using var mounted = MountWithRouter(router);
            Assume.That(HasLabel(_root, "parent-error-capture"), Is.True,
                "Precondition: the parent boundary rendered its ErrorElement");

            // Assert
            Assert.That(s_capturedRouteError!.Message, Does.Contain("child-boom"),
                "UseRouteError returns the child loader's thrown exception, not the boundary route's");
        }

        #endregion

        #region Child error caught at child boundary

        [Test]
        public void Given_ChildHasOwnBoundary_When_Rendered_Then_ParentRendersNormally()
        {
            // Arrange
            var router = BuildChildBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-layout"), Is.True,
                "The parent renders normally because the boundary is the child, not the parent");
        }

        [Test]
        public void Given_ChildHasOwnBoundary_When_Rendered_Then_ChildErrorElementRendersAtChild()
        {
            // Arrange
            var router = BuildChildBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child-error"), Is.True,
                "The child's own ErrorElement renders at the child position (no over-bubbling)");
        }

        [Test]
        public void Given_ChildHasOwnBoundary_When_Rendered_Then_ParentErrorElementDoesNotFire()
        {
            // Arrange
            var router = BuildChildBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-error"), Is.False,
                "The parent's ErrorElement does not fire because the child caught its own error");
        }

        [Test]
        public void Given_ChildHasOwnBoundary_When_Rendered_Then_ChildNormalElementIsReplaced()
        {
            // Arrange
            var router = BuildChildBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child"), Is.False, "The child's normal Element is replaced by its ErrorElement");
        }

        private Router BuildChildBoundaryRouter()
        {
            var routes = V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    errorElement: V.Component(ParentErrorRender, key: "parent-error"),
                    children: new[]
                    {
                        V.Route(
                            path: "child",
                            element: V.Component(ChildRender, key: "child"),
                            errorElement: V.Component(ChildErrorRender, key: "child-error"),
                            loader: ThrowingLoader("child-boom")),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/parent/child");
            return router;
        }

        #endregion

        #region Parent error at parent boundary

        [Test]
        public void Given_ParentErrorsWithOwnBoundary_When_Rendered_Then_ParentErrorElementRendersAtParent()
        {
            // Arrange
            var router = BuildParentErrorsRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-error"), Is.True, "The parent's ErrorElement renders at the parent's position");
        }

        [Test]
        public void Given_ParentErrorsWithOwnBoundary_When_Rendered_Then_ParentNormalElementIsReplaced()
        {
            // Arrange
            var router = BuildParentErrorsRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-layout"), Is.False, "The parent's normal Element is replaced by its ErrorElement");
        }

        [Test]
        public void Given_ParentErrorsWithOwnBoundary_When_Rendered_Then_ChildBelowBoundaryDoesNotRender()
        {
            // Arrange
            var router = BuildParentErrorsRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child"), Is.False, "The child below the boundary does not render");
        }

        private Router BuildParentErrorsRouter()
        {
            var routes = V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    errorElement: V.Component(ParentErrorRender, key: "parent-error"),
                    loader: ThrowingLoader("parent-boom"),
                    children: new[]
                    {
                        V.Route(
                            path: "child",
                            element: V.Component(ChildRender, key: "child")),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/parent/child");
            return router;
        }

        #endregion

        #region Deep child error bubbles past ancestor without boundary

        [Test]
        public void Given_DeepChildError_When_Rendered_Then_AncestorAboveBoundaryRendersNormally()
        {
            // Arrange
            var router = BuildDeepChainRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "grandparent-layout"), Is.True, "Ancestors above the boundary render normally");
        }

        [Test]
        public void Given_DeepChildError_When_Rendered_Then_NearestAncestorBoundaryCatchesIt()
        {
            // Arrange
            var router = BuildDeepChainRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-error"), Is.True,
                "The nearest ancestor errorElement (parent) catches the deep child error");
        }

        [Test]
        public void Given_DeepChildError_When_Rendered_Then_BoundaryNormalElementIsReplaced()
        {
            // Arrange
            var router = BuildDeepChainRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-layout"), Is.False, "The boundary route's normal Element is replaced by its ErrorElement");
        }

        [Test]
        public void Given_DeepChildError_When_Rendered_Then_RoutesBelowBoundaryDoNotRender()
        {
            // Arrange
            var router = BuildDeepChainRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child"), Is.False, "Routes below the boundary do not render");
        }

        private Router BuildDeepChainRouter()
        {
            var routes = V.Routes(
                V.Route(
                    path: "grand",
                    element: V.Component(GrandparentLayoutRender, key: "grand"),
                    children: new[]
                    {
                        V.Route(
                            path: "parent",
                            element: V.Component(ParentLayoutRender, key: "parent"),
                            errorElement: V.Component(ParentErrorRender, key: "parent-error"),
                            children: new[]
                            {
                                V.Route(
                                    path: "child",
                                    element: V.Component(ChildRender, key: "child"),
                                    loader: ThrowingLoader("child-boom")),
                            }),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/grand/parent/child");
            return router;
        }

        #endregion

        #region No ancestor boundary bubbles to root

        [Test]
        public void Given_ChildErrorNoAncestorBoundary_When_Rendered_Then_ParentLayoutBlanks()
        {
            // Arrange
            var router = BuildNoBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "parent-layout"), Is.False,
                "The implicit root boundary renders nothing, so even the parent layout blanks");
        }

        [Test]
        public void Given_ChildErrorNoAncestorBoundary_When_Rendered_Then_ErroredChildDoesNotRender()
        {
            // Arrange
            var router = BuildNoBoundaryRouter();

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(HasLabel(_root, "child"), Is.False,
                "The errored child does not render when no ancestor errorElement exists");
        }

        private Router BuildNoBoundaryRouter()
        {
            var routes = V.Routes(
                V.Route(
                    path: "parent",
                    element: V.Component(ParentLayoutRender, key: "parent"),
                    children: new[]
                    {
                        V.Route(
                            path: "child",
                            element: V.Component(ChildRender, key: "child"),
                            loader: ThrowingLoader("child-boom")),
                    }));
            var router = new Router(routes);
            router.NavigateSync("/parent/child");
            return router;
        }

        #endregion
    }
}
