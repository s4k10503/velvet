// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a route with no element, as React Router's <c>_renderMatches</c> renders one: in its place, the
    /// matched child route, under the Outlet context the route itself was rendered with.
    /// </summary>
    [TestFixture]
    internal sealed class RouteElementlessTests
    {
        private VisualElement _root = null!;
        private static string? s_context;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_context = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        [Component]
        private static VNode ContextLayout() => V.Outlet(context: "from-layout");

        [Component]
        private static VNode ContextReader()
        {
            s_context = Hooks.UseOutletContext<string>();
            return V.Label(text: "reader");
        }

        [Test]
        public void Given_ARouteWithNoElement_When_ItsChildMatches_Then_TheChildRendersInItsPlace()
        {
            // Arrange
            var router = BuildRouter("/a",
                V.Route("/", children: new[] { V.Route("a", V.Component(StubB)) }));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("stub-b"), Is.Not.Null);
        }

        [Test]
        public void Given_TwoRoutesWithNoElement_When_NavigatingFromOneToTheOther_Then_TheOutletTakesTheNewRoutesScope()
        {
            // Arrange — the two element-less routes sit at one depth over children rendering one component, so
            // only the element-less level can tell them apart.
            var factory = new TestRouteScopeFactory();
            var router = new Router(new[]
            {
                V.Route("/", children: new[]
                {
                    V.Route("a", scopeId: "A", children: new[] { V.Route("x", V.Component(StubA)) }),
                    V.Route("b", scopeId: "B", children: new[] { V.Route("x", V.Component(StubA)) }),
                }),
            }, factory, new RouterOptions { InitialEntries = new[] { "/a/x" } });
            using var mounted = V.Mount(_root, V.RouterProvider(router));
            mounted.FlushEffectsForTest();
            var before = factory.CreateScopeCount;

            // Act
            router.NavigateSync("/b/x");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(factory.CreateScopeCount - before, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARouteWithNoElementUnderALayoutOutletContext_When_ItsChildReadsTheContext_Then_ItIsTheLayouts()
        {
            // Arrange
            var router = BuildRouter("/a/b",
                V.Route("/", V.Component(ContextLayout), children: new[]
                {
                    V.Route("a", children: new[] { V.Route("b", V.Component(ContextReader)) }),
                }));

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(s_context, Is.EqualTo("from-layout"));
        }
    }
}
