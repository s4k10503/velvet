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
    /// <item>A route body rendered through an Outlet declared after a sibling starts below that sibling
    /// rather than over it.</item>
    /// <item>A route body sized as a percentage of its container still resolves against the container's
    /// box.</item>
    /// <item>Consecutive VirtualList items whose renderer returns a Component stack rather than sharing one
    /// position.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The router is stood up the way <c>OutletNodeTests</c> does it — a location carrying one match, pushed
    /// through the Location and Depth Providers — because nothing here turns on navigation. What these cases
    /// add over that fixture is a real panel: each reads a resolved rect, which is why each one acts through
    /// <see cref="PanelTestBase.ForcePanelUpdate"/>.
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
        private static VNode OutletOnlyLayoutRender()
            => V.Div(className: "flex flex-col h-[300px]", name: "layout",
                children: new VNode[] { V.Outlet() });

        [Component]
        private static VNode RouteBodyRender() => V.Div(className: "h-[20px]", name: "body");

        [Component]
        private static VNode FullHeightRouteBodyRender() => V.Div(className: "h-full", name: "body");

        [Component]
        private static VNode VirtualItemRender(string label) => V.Div(className: "h-[30px]", name: $"item-{label}");

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
