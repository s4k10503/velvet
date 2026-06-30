// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a loader error bubbles to the nearest ancestor route (at or above the errored route) that
    /// defines an <c>ErrorElement</c>.
    /// <list type="bullet">
    /// <item>A child loader error with no child <c>ErrorElement</c> surfaces in the nearest ancestor boundary:
    /// the boundary route renders its <c>ErrorElement</c> in place of its own Element and descendant Outlet
    /// subtree, ancestors above the boundary render normally, and routes below the boundary render nothing.</item>
    /// <item>The error is keyed by the descendant's <see cref="RouteMatch.RouteId"/>, yet <c>UseRouteError</c>
    /// at the bubble-target boundary resolves the caught descendant error rather than the boundary route's own
    /// id.</item>
    /// <item>A child with its own <c>ErrorElement</c> is the boundary itself: the parent renders normally and
    /// the child's <c>ErrorElement</c> renders at the child position (no over-bubbling).</item>
    /// <item>A parent that errors and defines an <c>ErrorElement</c> is its own boundary: its
    /// <c>ErrorElement</c> replaces its Element and the whole descendant subtree.</item>
    /// <item>A deep child error bubbles past ancestors without an <c>ErrorElement</c> to the nearest ancestor
    /// that has one.</item>
    /// <item>When no route at or above the errored route defines an <c>ErrorElement</c>, the error bubbles to
    /// the implicit root boundary, which renders nothing (there is no default error surface).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The tests drive a real <see cref="Router"/> and mount the router-root provider chain (Location /
    /// LoaderData / Errors) above an Outlet, then assert on rendered Label text to verify which element
    /// surfaced. <c>ParentErrorCaptureRender</c> records <c>UseRouteError</c> into a static field.
    /// </remarks>
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

        private MountedTree MountWithRouter(Router router)
        {
            var location = router.CurrentLocation;
            var loaderData = router.CurrentLoaderData;
            var errors = router.CurrentLoaderErrors;

            return V.Mount(_root,
                V.Provider(RouterContext.Location, location,
                    children: new VNode[]
                    {
                        V.Provider(RouterContext.LoaderData, loaderData,
                            children: new VNode[]
                            {
                                V.Provider(RouterContext.Errors, errors,
                                    children: new VNode[] { V.Outlet() }),
                            }),
                    }));
        }

        private static bool HasLabel(VisualElement element, string text) => element.FindLabelByText(text) != null;

        private static Func<RouteLoaderContext, System.Threading.CancellationToken, UniTask<object>> ThrowingLoader(string message)
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
            // The error is keyed by the child's RouteId, but the ErrorElement renders at the parent boundary;
            // UseRouteError there resolves the caught descendant error, not the boundary route's own id.
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
            // grandparent (no errorElement) -> parent (errorElement) -> child (errors, no errorElement).
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
            // With no errorElement anywhere, the error bubbles to the implicit root boundary, which has no
            // default error surface and renders nothing.
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
