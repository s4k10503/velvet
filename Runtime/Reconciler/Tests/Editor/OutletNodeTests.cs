// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.Outlet"/> and its reconciled node, the slot where a router renders
    /// the route matched at the current depth.
    /// <list type="bullet">
    /// <item>With no router context, an Outlet renders a single empty container; the container ignores picking
    /// and is tagged with <see cref="FiberNodeFactory.OutletContainerClass"/>.</item>
    /// <item>Removing the Outlet clears its container; patching an Outlet with another Outlet retains the same
    /// container element.</item>
    /// <item>An Outlet carries the key supplied to <see cref="V.Outlet"/>.</item>
    /// <item>Given a router location, the Outlet renders the route matched at its depth, and renders an empty
    /// container when there is no match or the depth exceeds the available matches.</item>
    /// <item>A nested route navigation accumulates one match per route segment in parent-to-child order.</item>
    /// <item>An Outlet keeps resolving its matched route across a standalone setState re-render — both when the
    /// enclosing layout re-renders and when the route component itself re-renders — because the enclosing
    /// Provider spine is reconstructed from the committed tree for an isolated re-render.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class OutletNodeTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        #region Basic Outlet

        [Test]
        public void Given_NoRouter_When_OutletMounted_Then_RendersSingleEmptyContainer()
        {
            // Arrange
            var tree = new VNode[] { V.Outlet() };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That((_root.childCount, _root.ElementAt(0).childCount), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_MountedOutlet_When_Removed_Then_ContainerIsCleared()
        {
            // Arrange
            var tree1 = new VNode[] { V.Outlet() };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree1);
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the Outlet container is mounted");

            // Act
            _reconciler.Reconcile(_root, tree1, Array.Empty<VNode>());

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_MountedOutlet_When_PatchedWithAnotherOutlet_Then_RetainsSameContainer()
        {
            // Arrange
            var tree1 = new VNode[] { V.Outlet() };
            var tree2 = new VNode[] { V.Outlet() };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree1);
            var elementBefore = _root.ElementAt(0);

            // Act
            _reconciler.Reconcile(_root, tree1, tree2);

            // Assert
            Assert.That(_root.ElementAt(0), Is.SameAs(elementBefore));
        }

        [Test]
        public void Given_NoRouter_When_OutletMounted_Then_ContainerIgnoresPicking()
        {
            // Arrange
            var tree = new VNode[] { V.Outlet() };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(_root.ElementAt(0).pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Test]
        public void Given_KeyedOutlet_When_Created_Then_NodeCarriesTheKey()
        {
            // Arrange
            var expectedKey = "test-outlet";

            // Act
            var node = V.Outlet(key: expectedKey);

            // Assert
            Assert.That(node.Key, Is.EqualTo(expectedKey));
        }

        #endregion

        #region Router integration

        [Test]
        public void Given_RouterLocationWithMatch_When_OutletMounted_Then_RendersMatchedContent()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            var tree = OutletUnderRouter(location, depth: 0);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            var outletContainer = FindOutletContainer(_root);
            Assert.That(outletContainer?.childCount, Is.GreaterThan(0));
        }

        [Test]
        public void Given_RouterLocationWithNoMatches_When_OutletMounted_Then_RendersEmptyContainer()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>(),
            };
            var tree = OutletUnderRouter(location, depth: 0);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            var outletContainer = FindOutletContainer(_root);
            Assert.That(outletContainer?.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_DepthExceedingMatches_When_OutletMounted_Then_RendersEmptyContainer()
        {
            // Arrange — depth 1 references Matches[1] but only one match exists, so the slot is empty
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            var tree = OutletUnderRouter(location, depth: 1);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            var outletContainer = FindOutletContainer(_root);
            Assert.That(outletContainer?.childCount, Is.EqualTo(0));
        }

        #endregion

        #region Nested route matching

        [Test]
        public void Given_NestedRoutes_When_NavigatedToChild_Then_MatchesAccumulateParentThenChild()
        {
            // Arrange
            var rootElement = V.Component(RootLayoutRender, key: "root");
            var homeElement = V.Component(HomeRender, key: "home");
            var aboutElement = V.Component(AboutRender, key: "about");
            var routes = V.Routes(
                V.Route(
                    path: "/",
                    element: rootElement,
                    children: new[]
                    {
                        V.Route(path: "home", element: homeElement),
                        V.Route(path: "about", element: aboutElement),
                    }));
            var router = new Router(routes);

            // Act
            router.NavigateAsync("/home").GetAwaiter().GetResult();

            // Assert
            var matches = router.CurrentLocation!.Matches;
            Assert.That(
                (matches.Count, matches[0].Route.Element, matches[1].Route.Element),
                Is.EqualTo((2, rootElement, homeElement)),
                "Navigating to a nested route yields parent then child matches in order");

            router.Dispose();
        }

        #endregion

        #region Standalone re-render

        [Test]
        public void Given_LayoutLocalProviderAroundOutlet_When_RouteReRendersStandalone_Then_ProviderStillReachesRoute()
        {
            // The layout body wraps <Outlet/> in a layout-local Provider; the wrapper-mounted route reads it via
            // UseContext. When the route itself setStates, the isolated re-render reconstructs the enclosing
            // Provider spine from the committed tree, so the layout-local Provider must still reach the route
            // rather than falling back to the context default.
            // Arrange
            s_themeRouteCount = 0;
            s_themeRouteSetCount = null;
            s_themeRouteLastTheme = null;
            var location = LocationWithSingleMatch(V.Component(ThemeRouteRender, key: "themed-route"));
            using var mounted = V.Mount(_root, WrapInRouter(location, depth: 0,
                V.Component(LayoutWithThemeProviderRender, key: "layout")));
            Assume.That(s_themeRouteLastTheme, Is.EqualTo("dark"),
                "Precondition: the layout-local Provider reaches the route on the initial top-down render");

            // Act
            s_themeRouteSetCount!.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_themeRouteCount, Is.EqualTo(1), "Precondition: the route's own setState committed");

            // Assert
            Assert.That(s_themeRouteLastTheme, Is.EqualTo("dark"),
                "The layout-local Provider keeps reaching the route after the route's standalone re-render");
        }

        [Test]
        public void Given_LayoutWithOutlet_When_LayoutReRendersStandalone_Then_OutletResolvesMatchedRoute()
        {
            // A standalone setState re-render reconciles only the layout's own output; the ancestor Location /
            // Depth Providers are pushed solely during a top-down walk, so the live context stack is empty for
            // this isolated re-render. Resolving the Outlet match must fall back to the layout fiber's context
            // snapshot, otherwise the matched route fiber is swept as an orphan and the container empties.
            // Arrange
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            using var mounted = V.Mount(_root, WrapInRouter(location, depth: 0,
                V.Component(LayoutWithOutletRender, key: "layout")));
            Assume.That(FindOutletContainer(_root)?.childCount, Is.GreaterThan(0),
                "Precondition: the Outlet resolves the matched route on the initial render");

            // Act
            s_layoutSetState!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            var outletAfter = FindOutletContainer(_root);
            Assert.That(outletAfter!.FindLabelByText("simple"), Is.Not.Null,
                "The Outlet keeps resolving to the matched route's content after the layout's standalone re-render");
        }

        #endregion

        #region Helpers

        private static RouterLocation LocationWithSingleMatch(ComponentNode element)
        {
            var matches = new List<RouteMatch>
            {
                new RouteMatch
                {
                    Route = new RouteDefinition { Path = "/", Element = element },
                    Params = new Dictionary<string, string>(),
                    MatchedPath = "/",
                },
            };
            return new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = matches,
            };
        }

        private static VNode WrapInRouter(RouterLocation location, int depth, VNode body)
            => V.Provider(RouterContext.Location, location,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Depth, depth, children: new VNode[] { body }),
                });

        private static VNode[] OutletUnderRouter(RouterLocation location, int depth)
            => new[] { WrapInRouter(location, depth, V.Outlet()) };

        private static VisualElement? FindOutletContainer(VisualElement root)
        {
            // Provider / Component wrappers also use PickingMode.Ignore (they emit no DOM), so the Outlet
            // container is identified unambiguously by FiberNodeFactory.OutletContainerClass.
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.ElementAt(i);
                if (child.ClassListContains(FiberNodeFactory.OutletContainerClass))
                    return child;

                var found = FindOutletContainer(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        #endregion

        #region Render targets (functional component)

        [Component]
        private static VNode SimpleRender() => V.Label(text: "simple");

        [Component]
        private static VNode RootLayoutRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "Root"),
                V.Outlet(),
            });

        private static Action<int>? s_layoutSetState;

        [Component]
        private static VNode LayoutWithOutletRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_layoutSetState = setCount;
            // Drop the leading sibling on re-render so the Outlet's slot shifts and the reconciler re-creates it
            // instead of patching in place — the create path is where a standalone re-render with an empty live
            // context stack would resolve no match.
            return count == 0
                ? V.Div(children: new VNode[] { V.Label(text: "Header"), V.Outlet() })
                : V.Div(children: new VNode[] { V.Outlet() });
        }

        [Component]
        private static VNode HomeRender() => V.Label(text: "Home Page");

        [Component]
        private static VNode AboutRender() => V.Label(text: "About Page");

        private static readonly ComponentContext<string> ThemeContext =
            ComponentContext<string>.Create("light");

        [Component]
        private static VNode LayoutWithThemeProviderRender()
            => V.Provider(ThemeContext, "dark",
                children: new VNode[] { V.Outlet() });

        private static int s_themeRouteCount;
        private static Action<int>? s_themeRouteSetCount;
        private static string? s_themeRouteLastTheme;

        [Component]
        private static VNode ThemeRouteRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_themeRouteCount = count;
            s_themeRouteSetCount = setCount;
            s_themeRouteLastTheme = Hooks.UseContext(ThemeContext);
            return V.Label(text: $"route-{count}");
        }

        #endregion
    }
}
