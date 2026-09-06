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
    /// Specifies the contract of <see cref="V.Outlet"/>, the slot where a router renders the route matched at
    /// the current depth.
    /// <list type="bullet">
    /// <item>An Outlet emits no element of its own: the matched route's output takes the Outlet's position in
    /// the parent's child list, and an Outlet that matches nothing leaves no position at all.</item>
    /// <item>An Outlet carries the key supplied to <see cref="V.Outlet"/>.</item>
    /// <item>A location whose match chain does not reach the Outlet's depth — no matches at all, or fewer than
    /// the depth indexes — renders nothing, and a route already mounted there leaves with its scope.</item>
    /// <item>A nested route navigation accumulates one match per route segment in parent-to-child order.</item>
    /// <item>An Outlet keeps resolving its matched route across a standalone setState re-render — both when the
    /// enclosing layout re-renders and when the route component itself re-renders — because the enclosing
    /// Provider spine is reconstructed from the committed tree for an isolated re-render.</item>
    /// <item>The route scope is held on the Outlet's own fiber: a route change replaces it, and the Outlet
    /// unmounting releases it — including the unmount a whole-reconciler teardown drives.</item>
    /// <item>A route change leaves nothing of the departed route behind, including the children of an
    /// <c>V.AnimatePresence</c> in its body — which the arriving route never declares, so only the
    /// departing route's own boundary could have put them on the diff's old side.</item>
    /// </list>
    /// <see cref="ComponentSwapElementOwnershipTests"/> owns that last reading without a router above it.
    /// </summary>
    [TestFixture]
    internal sealed class OutletTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        #region No element of its own

        [Test]
        public void Given_NoRouter_When_OutletMounted_Then_NothingIsMounted()
        {
            // Arrange
            var tree = new VNode[] { V.Outlet() };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_AHeaderBeforeAMatchedOutlet_When_Mounted_Then_TheRouteBodyIsTheContainersSecondChild()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            var tree = new[]
            {
                WrapInRouter(location, depth: 0, V.Div(name: "layout", children: new VNode[]
                {
                    V.Label(text: "header"),
                    V.Outlet(),
                })),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            var layout = _root.Q<VisualElement>("layout");
            Assert.That(
                (layout.childCount, (layout.ElementAt(1) as Label)?.text),
                Is.EqualTo((2, "simple")),
                "The route's own element sits at the Outlet's position, with nothing between them");
        }

        [Test]
        public void Given_AnUnmatchedOutletBetweenTwoSiblings_When_Mounted_Then_TheSiblingsAreAdjacent()
        {
            // Arrange
            var tree = new[]
            {
                WrapInRouter(LocationWithNoMatches(), depth: 0, V.Div(name: "layout", children: new VNode[]
                {
                    V.Label(text: "before"),
                    V.Outlet(),
                    V.Label(text: "after"),
                })),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            var layout = _root.Q<VisualElement>("layout");
            Assert.That(
                (layout.childCount, (layout.ElementAt(0) as Label)?.text, (layout.ElementAt(1) as Label)?.text),
                Is.EqualTo((2, "before", "after")),
                "An Outlet with nothing to render occupies no slot between its siblings");
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
        public void Given_RouterLocationWithNoMatches_When_OutletMounted_Then_RendersNothing()
        {
            // Arrange
            var tree = OutletUnderRouter(LocationWithNoMatches(), depth: 0);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_DepthExceedingMatches_When_OutletMounted_Then_RendersNothing()
        {
            // Arrange — depth 1 references Matches[1] but only one match exists, so the slot is empty
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            var tree = OutletUnderRouter(location, depth: 1);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        #endregion

        #region Leaving a route

        [Test]
        public void Given_AMountedRoute_When_TheNewLocationMatchesNoRoute_Then_TheRouteBodyLeavesTheTree()
        {
            // Arrange
            var matched = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(SimpleRender, key: "simple")), depth: 0);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), matched);
            Assume.That(_root.FindLabelByText("simple"), Is.Not.Null,
                "Precondition: the Outlet mounted the matched route");
            var unmatched = OutletUnderRouter(LocationWithNoMatches(), depth: 0);

            // Act
            _reconciler.Reconcile(_root, matched, unmatched);

            // Assert
            Assert.That(_root.FindLabelByText("simple"), Is.Null,
                "A location the match chain does not reach lets the route it was holding go");
        }

        [Test]
        public void Given_AMountedRouteWithAScope_When_TheNewLocationMatchesNoRoute_Then_TheScopeIsDisposed()
        {
            // Arrange
            var scopeFactory = new TestRouteScopeFactory();
            using var router = RouterWithScopeFactory(scopeFactory);
            var matched = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(SimpleRender, key: "simple")), depth: 0);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), matched);
            Assume.That(scopeFactory.LastScope, Is.Not.Null, "Precondition: the Outlet created a route scope");
            var unmatched = OutletUnderRouter(LocationWithNoMatches(), depth: 0);

            // Act
            _reconciler.Reconcile(_root, matched, unmatched);

            // Assert
            Assert.That(scopeFactory.LastScope!.IsDisposed, Is.True,
                "The scope goes with the route the Outlet stopped rendering");
        }

        // GREEN_ON_BASE(characterization): a route change already replaced the scope, through the
        // identity comparison the Outlet's container carried. This is the reading that says the fiber-held
        // entry decides the same thing.
        [Test]
        public void Given_AMountedRoute_When_ADifferentRouteMatches_Then_TheDepartedScopeIsDisposedAndOneReplacesIt()
        {
            // Arrange
            var scopeFactory = new TestRouteScopeFactory();
            using var router = RouterWithScopeFactory(scopeFactory);
            var first = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(SimpleRender, key: "simple")), depth: 0);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), first);
            var departing = scopeFactory.LastScope;
            var second = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(OtherRender, key: "other")), depth: 0);

            // Act
            _reconciler.Reconcile(_root, first, second);

            // Assert — the departed scope is torn down and the factory was asked a second time, which is
            // what separates a replacement from a scope the Outlet merely kept holding.
            Assert.That(
                (departing!.IsDisposed, scopeFactory.CreateScopeCount),
                Is.EqualTo((true, 2)));
        }

        [Test]
        public void Given_AMountedRouteHoldingAPresence_When_ADifferentRouteMatches_Then_ItsChildrenLeaveWithIt()
        {
            // Arrange — with the route rendered at the Outlet's own position, a route change is a slot whose
            // component changes, and the departing route's body holds children only its own presence
            // boundary can put on a diff's old side.
            var first = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(PresenceRouteRender, key: "presence")), depth: 0);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), first);
            var rowsBefore = _root.ElementAt(0).childCount;
            var second = OutletUnderRouter(
                LocationWithSingleMatch(V.Component(PlainRouteRender, key: "plain")), depth: 0);

            // Act
            _reconciler.Reconcile(_root, first, second);

            // Assert — the departing row count rides along, because an arrangement that mounted no rows
            // would satisfy the arriving route's own count while saying nothing about a departure.
            Assert.That(
                (rowsBefore, _root.ElementAt(0).name, _root.ElementAt(0).childCount),
                Is.EqualTo((3, "plain-route", 1)),
                "The arriving route's body is all the Outlet's position holds");
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

        // GREEN_ON_BASE(characterization): the depth an Outlet supplies its route already reached a
        // nested Outlet, through a push around the mount rather than through a Provider it renders.
        [Test]
        public void Given_NestedOutlets_When_Mounted_Then_TheInnerOutletRendersTheFollowingMatch()
        {
            // Arrange — the layout route renders an Outlet of its own, which must resolve Matches[1]
            var location = new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>
                {
                    Match(V.Component(RootLayoutRender, key: "root"), "/"),
                    Match(V.Component(HomeRender, key: "home"), "/home"),
                },
            };
            var tree = OutletUnderRouter(location, depth: 0);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(_root.FindLabelByText("Home Page"), Is.Not.Null,
                "The layout route's own Outlet reads the incremented depth and renders the next match");
        }

        #endregion

        #region Standalone re-render

        // GREEN_ON_BASE(characterization): the layout-local Provider already reached the route across its
        // own re-render, through a spine walk that recognised the Outlet's container as the route's host.
        [Test]
        public void Given_LayoutLocalProviderAroundOutlet_When_RouteReRendersStandalone_Then_ProviderStillReachesRoute()
        {
            // The layout body wraps <Outlet/> in a layout-local Provider; the route reads it via UseContext.
            // When the route itself setStates, the isolated re-render reconstructs the enclosing Provider spine
            // from the committed tree, so the layout-local Provider must still reach the route rather than
            // falling back to the context default.
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

        // GREEN_ON_BASE(characterization): the Outlet already resolved its match across the layout's own
        // re-render. What it read the match from is what this change moves.
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
            Assume.That(_root.FindLabelByText("simple"), Is.Not.Null,
                "Precondition: the Outlet resolves the matched route on the initial render");

            // Act
            s_layoutSetState!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("simple"), Is.Not.Null,
                "The Outlet keeps resolving to the matched route's content after the layout's standalone re-render");
        }

        #endregion

        #region Scope lifetime

        // GREEN_ON_BASE(characterization): a whole-reconciler teardown already released the scope, from a
        // sweep over the scope table rather than from the fiber teardown that precedes it.
        [Test]
        public void Given_RoutedOutletWithScope_When_ReconcilerDisposed_Then_ScopeIsDisposed()
        {
            // A whole-Reconciler/root teardown (e.g. closing a mounted UI tree) never reconciles the
            // Outlet away node-by-node: the release rides on the registry teardown disposing every fiber
            // it holds, which is a different entrance from the one a route change or a removal takes.
            // Arrange
            var scopeFactory = new TestRouteScopeFactory();
            var routes = V.Routes(V.Route(path: "/", element: V.Component(SimpleRender, key: "simple")));
            var router = new Router(routes, scopeFactory);
            router.NavigateAsync("/").GetAwaiter().GetResult();
            var tree = OutletUnderRouter(router.CurrentLocation!, depth: 0);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            Assume.That(scopeFactory.LastScope, Is.Not.Null, "Precondition: the Outlet created a route scope");

            // Act — dispose the whole Reconciler, not the individual Outlet
            _reconciler.Dispose();
            _reconciler = new Reconciler();

            // Assert
            Assert.That(scopeFactory.LastScope!.IsDisposed, Is.True);

            router.Dispose();
        }

        private readonly record struct ToggleState(bool Show);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(true)) { }
            public void Set(bool show) => SetState(_ => new ToggleState(show));
            protected override void ResetCore() => SetState(_ => new ToggleState(true));
        }

        private static ToggleStore s_store;

        [Component]
        private static VNode App()
        {
            var show = Hooks.UseStore(s_store, s => s.Show);
            return V.Div(name: "app", children: show
                ? new VNode[] { V.Outlet() }
                : Array.Empty<VNode>());
        }

        // GREEN_ON_BASE(characterization): an unmounting Outlet already released both, from the element
        // cleaner rather than from the fiber's own teardown.
        [Test]
        public void Given_AMountedOutletHoldingAScope_When_ItUnmounts_Then_ItsScopeRegistrationIsReleased()
        {
            // Arrange — a mounted Outlet registers its scope against its own fiber.
            var scopeFactory = new TestRouteScopeFactory();
            using var router = RouterWithScopeFactory(scopeFactory);
            using var store = new ToggleStore();
            s_store = store;
            var location = LocationWithSingleMatch(V.Component(SimpleRender, key: "simple"));
            using var mounted = V.Mount(_root, WrapInRouter(location, depth: 0,
                V.Component(App, key: "app")));
            var context = mounted.Root.Reconciler.Context;
            var scheduler = context.BatchScheduler;
            Assume.That(context.OutletScopes.Count, Is.EqualTo(1),
                "Precondition: the mounted Outlet's scope is registered");

            // Act — the Outlet unmounts.
            store.Set(false);
            scheduler.DrainImmediateForTest();

            // Assert — the registration is gone and the scope with it, so neither accumulates across route
            // changes over a long session.
            Assert.That(
                (context.OutletScopes.Count, scopeFactory.LastScope!.IsDisposed),
                Is.EqualTo((0, true)));
        }

        #endregion

        #region Helpers

        private static RouteMatch Match(ComponentNode element, string matchedPath)
            => new RouteMatch
            {
                Route = new RouteDefinition { Path = matchedPath, Element = element },
                Params = new Dictionary<string, string>(),
                MatchedPath = matchedPath,
            };

        private static RouterLocation LocationWithSingleMatch(ComponentNode element)
            => new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch> { Match(element, "/") },
            };

        private static RouterLocation LocationWithNoMatches()
            => new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>(),
            };

        // The scope factory is reached through Router.Current, so a fixture that wants one has to have a
        // router standing even when it builds its locations by hand.
        private static Router RouterWithScopeFactory(IRouteScopeFactory scopeFactory)
            => new Router(V.Routes(V.Route(path: "/", element: V.Component(SimpleRender, key: "simple"))),
                scopeFactory);

        private static VNode WrapInRouter(RouterLocation location, int depth, VNode body)
            => V.Provider(RouterContext.Location, location,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Depth, depth, children: new VNode[] { body }),
                });

        private sealed record RoutedAppProps(RouterLocation Location, int Depth);

        // The router spine renders inside a component so a location change on a later reconcile has a fiber
        // under it: reconciled from the walk root instead, GeneralPathReconciler.NotifyContextValueChange
        // finds none to propagate from and asserts that it is skipping live propagation.
        [Component]
        private static VNode RoutedAppRender(RoutedAppProps props)
            => WrapInRouter(props.Location, props.Depth, V.Outlet());

        private static VNode[] OutletUnderRouter(RouterLocation location, int depth)
            => new VNode[] { V.Component(RoutedAppRender, new RoutedAppProps(location, depth), key: "app") };

        #endregion

        #region Render targets (functional component)

        [Component]
        private static VNode SimpleRender() => V.Label(text: "simple");

        [Component]
        private static VNode OtherRender() => V.Label(text: "other");

        private static readonly string[] PresenceRows = { "a", "b", "c" };

        [Component]
        private static VNode PresenceRouteRender()
            => V.Div(name: "presence-route", children: new VNode[]
            {
                V.AnimatePresence(children: V.List(PresenceRows, row => row, row => V.Motion(
                    name: "row-" + row, children: new VNode[] { V.Label(text: row) }))),
            });

        [Component]
        private static VNode PlainRouteRender()
            => V.Div(name: "plain-route", children: new VNode[] { V.Label(text: "plain") });

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
