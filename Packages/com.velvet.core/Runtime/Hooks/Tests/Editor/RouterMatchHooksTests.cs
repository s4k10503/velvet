// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the hooks that read the matched routes and resolve targets against them, over a real
    /// <c>Router</c> and the Outlets <c>V.RouterProvider</c> renders.
    /// <list type="bullet">
    /// <item><c>UseMatches</c> lists every matched route, parent first, with its id, pathname, loader data and
    /// handle; <c>UseRouteLoaderData</c> reads one route's loader data by its id from any depth.</item>
    /// <item><c>UseResolvedPath</c> and <c>UseHref</c> resolve a target from the route the caller renders in,
    /// as <c>UseNavigate</c> does.</item>
    /// <item><c>UseInRouterContext</c> and <c>UseNavigationType</c> answer from the router the provider
    /// publishes.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterMatchHooksTests
    {
        private VisualElement _root = null!;
        private MountedTree? _mounted;
        private static IReadOnlyList<UiMatch>? s_matches;
        private static string? s_routeData;
        private static string? s_missingData;
        private static ResolvedPath? s_resolved;
        private static string? s_href;
        private static string? s_parentHref;
        private static bool? s_inRouter;
        private static NavigationType? s_navigationType;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_matches = null;
            s_routeData = null;
            s_missingData = null;
            s_resolved = null;
            s_href = null;
            s_parentHref = null;
            s_inRouter = null;
            s_navigationType = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            Router.Current?.Dispose();
            _root = null!;
        }

        [Component]
        private static VNode Layout()
        {
            s_matches = Hooks.UseMatches();
            s_inRouter = Hooks.UseInRouterContext();
            s_navigationType = Hooks.UseNavigationType();
            return V.Div(children: new VNode[] { V.Outlet() });
        }

        [Component]
        private static VNode Leaf()
        {
            s_routeData = Hooks.UseRouteLoaderData<string>("root");
            s_missingData = Hooks.UseRouteLoaderData<string>("absent");
            s_resolved = Hooks.UseResolvedPath("?q=1#top");
            s_href = Hooks.UseHref("edit");
            s_parentHref = Hooks.UseHref("..");
            return V.Label(text: "leaf");
        }

        [Component]
        private static VNode Outside()
        {
            s_inRouter = Hooks.UseInRouterContext();
            return V.Label(text: "outside");
        }

        private void MountRouter()
        {
            var router = new Router(V.Routes(
                V.Route("/", V.Component(Layout), id: "root", handle: "crumb-root",
                    loader: (_, _) => VelvetTask.FromResult<object>("root-data"),
                    children: new[]
                    {
                        V.Route("users/:id", V.Component(Leaf), handle: "crumb-user",
                            loader: (_, _) => VelvetTask.FromResult<object>("user-data")),
                    })));
            router.NavigateSync("/users/7");
            _mounted = V.Mount(_root, V.RouterProvider(router));
        }

        [Test]
        public void Given_NestedRoutesWithLoadersAndHandles_When_UseMatchesRenders_Then_EachMatchCarriesItsIdPathnameDataAndHandle()
        {
            // Arrange
            MountRouter();

            // Act
            var described = string.Join(";", s_matches!.Select(m => $"{m.Id}|{m.Pathname}|{m.Data}|{m.Handle}"));

            // Assert
            Assert.That(described, Is.EqualTo("root|/|root-data|crumb-root;0-0|/users/7|user-data|crumb-user"));
        }

        [Test]
        public void Given_NestedRoutes_When_UseMatchesRenders_Then_EveryMatchSeesTheCapturedParams()
        {
            // Arrange
            MountRouter();

            // Act
            var ids = string.Join(";", s_matches!.Select(m => m.Params["id"]));

            // Assert
            Assert.That(ids, Is.EqualTo("7;7"));
        }

        [Test]
        public void Given_AnAncestorsLoaderData_When_ADescendantReadsItById_Then_ItIsReturned()
        {
            // Arrange
            MountRouter();

            // Act
            var data = s_routeData;

            // Assert
            Assert.That(data, Is.EqualTo("root-data"));
        }

        [Test]
        public void Given_NoRouteWithTheId_When_UseRouteLoaderDataRenders_Then_ItIsDefault()
        {
            // Arrange
            MountRouter();

            // Act
            var data = s_missingData;

            // Assert
            Assert.That(data, Is.Null);
        }

        [Test]
        public void Given_ALeafRoute_When_UseResolvedPathRendersASearchAndHash_Then_ItKeepsTheCurrentPathname()
        {
            // Arrange
            MountRouter();

            // Act
            var described = $"{s_resolved!.Pathname}|{s_resolved.Search}|{s_resolved.Hash}";

            // Assert
            Assert.That(described, Is.EqualTo("/users/7|?q=1|#top"));
        }

        [Test]
        public void Given_ALeafRoute_When_UseHrefRendersARelativeTarget_Then_ItResolvesFromTheLeafRoute()
        {
            // Arrange
            MountRouter();

            // Act
            var href = s_href;

            // Assert
            Assert.That(href, Is.EqualTo("/users/7/edit"));
        }

        [Test]
        public void Given_ALeafRoute_When_UseHrefRendersDotDot_Then_ItDropsTheLeafRoute()
        {
            // Arrange
            MountRouter();

            // Act
            var href = s_parentHref;

            // Assert
            Assert.That(href, Is.EqualTo("/"));
        }

        [Test]
        public void Given_ARouterProvider_When_UseInRouterContextRenders_Then_ItIsTrue()
        {
            // Arrange
            MountRouter();

            // Act
            var inRouter = s_inRouter;

            // Assert
            Assert.That(inRouter, Is.True);
        }

        [Test]
        public void Given_NoRouterProvider_When_UseInRouterContextRenders_Then_ItIsFalse()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Outside));

            // Act
            var inRouter = s_inRouter;

            // Assert
            Assert.That(inRouter, Is.False);
        }

        [Test]
        public void Given_ALocationReachedByAPush_When_UseNavigationTypeRenders_Then_ItIsPush()
        {
            // Arrange
            MountRouter();

            // Act
            var type = s_navigationType;

            // Assert
            Assert.That(type, Is.EqualTo(NavigationType.Push));
        }
    }
}
