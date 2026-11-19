// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a matched route is laid out inside the container the <see cref="V.Outlet"/> was written
    /// in: at the Outlet's own position in the parent's flow, after the siblings declared before it. A wrapper
    /// element at the Outlet's position is what a parent's layout would count instead of the route, and this
    /// fixture reads the resolved geometry rather than the child list to say so.
    /// </summary>
    [TestFixture]
    internal sealed class OutletLayoutTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AHeaderBeforeAMatchedOutlet_When_LaidOut_Then_TheRouteBodyStartsBelowTheHeader()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/",
                Params = new Dictionary<string, string>(),
                Matches = new List<RouteMatch>
                {
                    new RouteMatch
                    {
                        Route = new RouteDefinition { Path = "/", Element = V.Component(BodyRender, key: "body") },
                        Params = new Dictionary<string, string>(),
                        MatchedPath = "/",
                    },
                },
            };
            var root = _window.rootVisualElement;

            // Act
            _mounted = V.Mount(root, V.Provider(RouterContext.Location, location, children: new VNode[]
            {
                V.Provider(RouterContext.Depth, 0, children: new VNode[]
                {
                    V.Div(className: "flex flex-col w-[400px] h-[300px]", name: "layout", children: new VNode[]
                    {
                        V.Div(className: "h-[40px]", name: "header"),
                        V.Outlet(),
                    }),
                }),
            }));
            ForcePanelUpdate(root.panel);
            ForcePanelUpdate(root.panel);

            // Assert — the header's own measured height is folded in, so a header that failed to lay out
            // cannot let a route body pinned at the top of the container pass.
            var header = root.Q<VisualElement>("header");
            var body = root.Q<VisualElement>("body");
            Assert.That(
                new[] { header.layout.height, body.layout.y },
                Is.EqualTo(new[] { 40f, 40f }).Within(0.01f),
                "The route body is laid out after the sibling declared before the Outlet");
        }

        [Component]
        private static VNode BodyRender() => V.Div(className: "h-[20px]", name: "body");
    }
}
