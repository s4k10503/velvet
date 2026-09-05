// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the anchor <see cref="VisualElement"/> the reconciler emits for <see cref="V.Outlet"/>,
    /// and for the wrapper-mount a VirtualList item takes, takes part in the layout of the container it is
    /// written into.
    /// <list type="bullet">
    /// <item>A route body rendered through an Outlet declared after a sibling starts below that sibling in a
    /// column container, and to the right of it in a row container, rather than over it.</item>
    /// <item>A route body sized as a percentage of its container still resolves against the container's
    /// box, on the main axis and on the cross axis of both container directions, and a container that
    /// centres its items does not take the cross axis away.</item>
    /// <item>An Outlet that matched no route leaves the container's main-axis space to the siblings
    /// declared beside it, takes its share once a route resolves into it, and hands it back when the route
    /// body renders itself away.</item>
    /// <item>Consecutive VirtualList items whose renderer returns a Component stack rather than sharing one
    /// position, and so do items whose renderer returns a Provider.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The router is stood up the way <c>OutletNodeTests</c> does it — a location carrying one match, pushed
    /// through the Location and Depth Providers. What these cases add over that fixture is a real panel: each
    /// reads a resolved rect, which is why each one acts through
    /// <see cref="PanelTestBase.ForcePanelUpdate"/>. Two cases change what is rendered after the mount, and
    /// drive it through a store the component reads plus a drained immediate tier.
    /// </remarks>
    [TestFixture]
    internal sealed class LayoutAnchorFlowTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Component]
        private static VNode HeaderThenOutletLayoutRender()
            => V.Div(className: "flex flex-col", name: "layout",
                children: new VNode[]
                {
                    V.Div(className: "h-[40px]", name: "header"),
                    V.Outlet(),
                });

        [Component]
        private static VNode SidebarThenOutletLayoutRender()
            => V.Div(className: "flex flex-row w-[400px] h-[300px]", name: "layout",
                children: new VNode[]
                {
                    V.Div(className: "w-[100px]", name: "sidebar"),
                    V.Outlet(),
                });

        [Component]
        private static VNode OutletOnlyLayoutRender()
            => V.Div(className: "flex flex-col h-[300px]", name: "layout",
                children: new VNode[] { V.Outlet() });

        [Component]
        private static VNode CentredColumnLayoutRender()
            => V.Div(className: "flex flex-col items-center w-[400px] h-[300px]", name: "layout",
                children: new VNode[] { V.Outlet() });

        [Component]
        private static VNode CentredRowLayoutRender()
            => V.Div(className: "flex flex-row items-center w-[400px] h-[300px]", name: "layout",
                children: new VNode[] { V.Outlet() });

        [Component]
        private static VNode GrowingSiblingThenOutletLayoutRender()
            => V.Div(className: "flex flex-col w-[400px] h-[300px]", name: "layout",
                children: new VNode[]
                {
                    V.Div(className: "flex-1", name: "main"),
                    V.Outlet(),
                });

        [Component]
        private static VNode RouteBodyRender() => V.Div(className: "h-[20px]", name: "body");

        [Component]
        private static VNode FullHeightRouteBodyRender() => V.Div(className: "h-full", name: "body");

        [Component]
        private static VNode FullWidthRouteBodyRender() => V.Div(className: "w-full h-[20px]", name: "body");

        [Component]
        private static VNode NarrowFullHeightRouteBodyRender() => V.Div(className: "w-[20px] h-full", name: "body");

        [Component]
        private static VNode VirtualItemRender(string label) => V.Div(className: "h-[30px]", name: $"item-{label}");

        [Component]
        private static VNode VanishingRouteBodyRender()
            => Hooks.UseStore(s_store, s => s.On) ? V.Div(className: "h-[20px]", name: "body") : null!;

        private static readonly ComponentContext<string> ItemContext = ComponentContext<string>.Create("default");

        [Test]
        public void Given_AnOutletDeclaredAfterASibling_When_TheMatchedRouteRenders_Then_TheRouteBodyStartsBelowThatSibling()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(RouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(HeaderThenOutletLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").worldBound.yMin,
                Is.GreaterThanOrEqualTo(_window.rootVisualElement.Q<VisualElement>("header").worldBound.yMax));
        }

        [Test]
        public void Given_AnOutletDeclaredAfterASiblingInARowContainer_When_TheMatchedRouteRenders_Then_TheRouteBodyStartsBesideThatSibling()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(RouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(SidebarThenOutletLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").worldBound.xMin,
                Is.GreaterThanOrEqualTo(_window.rootVisualElement.Q<VisualElement>("sidebar").worldBound.xMax));
        }

        // GREEN_ON_BASE(characterization): a route body asking for its container's full height gets it today.
        // The anchor used to be pinned to its container's edges, which is what resolved the percentage; an
        // anchor that takes a flex slot has to grow into the leftover space to keep the same reading.
        [Test]
        public void Given_ARouteBodySizedAsAPercentageOfItsContainer_When_TheOutletRendersIt_Then_ItResolvesAgainstThatContainer()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(FullHeightRouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(OutletOnlyLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").layout.height,
                Is.EqualTo(300f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): a percentage width on a route body resolves against its container.
        // The edge-to-edge insets the anchor used to carry are what resolved it on the cross axis; an anchor
        // that takes a flex slot has to hold that cross size itself, since the align-items its container
        // declares would otherwise shrink-wrap it to the body's own content.
        [Test]
        public void Given_AColumnRouteContainerThatCentresItsItems_When_TheOutletRendersAFullWidthRouteBody_Then_ItResolvesAgainstThatContainer()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(FullWidthRouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(CentredColumnLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").layout.width,
                Is.EqualTo(400f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the cross axis of a row container resolves a percentage height.
        // Same reading as the column case above, on the axis a row container puts it: the pair is here because
        // one anchor style answers for both directions, and a case in one direction leaves the other
        // unmeasured.
        [Test]
        public void Given_ARowRouteContainerThatCentresItsItems_When_TheOutletRendersAFullHeightRouteBody_Then_ItResolvesAgainstThatContainer()
        {
            // Arrange
            var location = LocationWithSingleMatch(V.Component(NarrowFullHeightRouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(CentredRowLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").layout.height,
                Is.EqualTo(300f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): an Outlet that matched no route leaves its siblings the container.
        // The anchor was out of the flow entirely before this change, so it could not take a share; in the
        // flow it has to decline one while it holds nothing.
        [Test]
        public void Given_AnOutletThatMatchesNoRoute_When_ItSitsBesideAGrowingSibling_Then_ThatSiblingKeepsTheWholeContainer()
        {
            // Arrange
            var location = LocationWithNoMatch();
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(GrowingSiblingThenOutletLayoutRender, key: "layout")));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("main").layout.height,
                Is.EqualTo(300f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): a route resolving after the mount fills its container the same way
        // one matched at the mount does. The mount path and the patch path each hand the anchor its share of
        // the container, and only the patch one runs here.
        [Test]
        public void Given_AnOutletThatMatchedNothingAtMount_When_ALocationThatMatchesArrives_Then_TheRouteBodyFillsTheContainer()
        {
            // Arrange
            using var store = new SwitchStore();
            s_store = store;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(NavigatingRootRender, key: "root"));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            store.Set(true);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("body").layout.height,
                Is.EqualTo(300f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): a route body that renders itself away leaves the container to its
        // siblings. The anchor holding it was out of the flow before this change, so the space was theirs
        // whatever the body did; in the flow it has to hand the space back when the body goes.
        [Test]
        public void Given_AMatchedRouteBodyThatRendersItselfAway_When_ItDoes_Then_ItsGrowingSiblingTakesTheWholeContainer()
        {
            // Arrange
            using var store = new SwitchStore();
            s_store = store;
            store.Set(true);
            var location = LocationWithSingleMatch(V.Component(VanishingRouteBodyRender, key: "body"));
            _mounted = V.Mount(_window.rootVisualElement,
                WrapInRouter(location, V.Component(GrowingSiblingThenOutletLayoutRender, key: "layout")));
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Act
            store.Set(false);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("main").layout.height,
                Is.EqualTo(300f).Within(0.01f));
        }

        [Test]
        public void Given_AVirtualListWhoseRendererReturnsAProvider_When_ItemsRender_Then_TheSecondStartsBelowTheFirst()
        {
            // Arrange
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: 30f,
                    renderer: item => V.Provider(ItemContext, item,
                        children: new VNode[] { V.Div(className: "h-[30px]", name: $"item-{item}") }),
                    name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("item-b").worldBound.yMin,
                Is.GreaterThanOrEqualTo(_window.rootVisualElement.Q<VisualElement>("item-a").worldBound.yMax));
        }

        [Test]
        public void Given_AVirtualListWhoseRendererReturnsAComponent_When_ItemsRender_Then_TheSecondStartsBelowTheFirst()
        {
            // Arrange
            var items = new List<string> { "a", "b", "c", "d" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: 30f,
                    renderer: item => V.Component(VirtualItemRender, item, key: item),
                    name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("item-b").worldBound.yMin,
                Is.GreaterThanOrEqualTo(_window.rootVisualElement.Q<VisualElement>("item-a").worldBound.yMax));
        }

        private readonly record struct SwitchState(bool On);

        // One switch for both cases that change what is rendered after the mount: the navigation case reads
        // it to choose the location, the vanishing-body case to choose whether to render at all.
        private sealed class SwitchStore : Store<SwitchState>
        {
            public SwitchStore() : base(new SwitchState(false)) { }
            public void Set(bool on) => SetState(_ => new SwitchState(on));
            protected override void ResetCore() => SetState(_ => new SwitchState(false));
        }

        private static SwitchStore s_store;

        private static readonly RouterLocation MatchedFullHeightLocation =
            LocationWithSingleMatch(V.Component(FullHeightRouteBodyRender, key: "body"));

        private static readonly RouterLocation UnmatchedLocation = LocationWithNoMatch();

        [Component]
        private static VNode NavigatingRootRender()
        {
            var matched = Hooks.UseStore(s_store, s => s.On);
            return WrapInRouter(
                matched ? MatchedFullHeightLocation : UnmatchedLocation,
                V.Component(OutletOnlyLayoutRender, key: "layout"));
        }

        private static RouterLocation LocationWithNoMatch()
            => new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>(),
            };

        private static RouterLocation LocationWithSingleMatch(ComponentNode element)
            => new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>
                {
                    new RouteMatch
                    {
                        Route = new RouteDefinition { Path = "/", Element = element },
                        Params = new Dictionary<string, string>(),
                        MatchedPath = "/",
                    },
                },
            };

        private static VNode WrapInRouter(RouterLocation location, VNode body)
            => V.Provider(RouterContext.Location, location,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Depth, 0, children: new VNode[] { body }),
                });
    }
}
