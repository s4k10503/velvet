// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the router context hooks (<see cref="Hooks.UseParams"/>,
    /// <see cref="Hooks.UseLocation"/> and <c>UseLoaderData</c>) reading from
    /// <see cref="RouterContext.Location"/> and <see cref="RouterContext.LoaderData"/>.
    /// <list type="bullet">
    /// <item>Reading the router location without any <see cref="RouterContext.Location"/> Provider yields the
    /// context default: a null location, and therefore an empty parameter dictionary.</item>
    /// <item>When a <see cref="RouterContext.Location"/> Provider supplies a location, a descendant component
    /// observes that exact location and its route parameters.</item>
    /// <item><see cref="Hooks.UseLocation"/> declares that null return, while <see cref="Hooks.UseParams"/>
    /// declares the empty dictionary it substitutes instead.</item>
    /// <item>Loader data is read for the route matched at the reader's own Outlet depth: at a depth the
    /// match list does not reach the default comes back even while the map holds entries, and at a nested
    /// depth the answer is that depth's route rather than the outermost or the innermost. Reading it
    /// through a real <c>Router</c> and a real Outlet is specified by <c>RoutingHooksTests</c>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Reads the context through the live context cursor that the <c>V.Mount</c> render path establishes; a raw
    /// <c>Reconciler.Reconcile</c> does not own that cursor lifecycle, so the no-Provider cases drive the bare
    /// reconciler and the with-Provider cases drive <c>V.Mount</c>. Per-component captures are exposed via static
    /// fields reset together in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class RouterHooksTests
    {
        private VisualElement _root = null!;
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _reconciler = new Reconciler();
            ParamsCapture.Reset();
            LocationCapture.Reset();
            LoaderDataCapture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        #region UseParams

        [Test]
        public void Given_NoRouterLocationProvider_When_Rendered_Then_ParamsAreEmpty()
        {
            // Arrange
            var tree = new VNode[] { V.Component(ParamsCapture.Render) };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            Assume.That(ParamsCapture.LastParams, Is.Not.Null, "Precondition: the params hook produced a dictionary");

            // Assert
            Assert.That(ParamsCapture.LastParams, Is.Empty, "Without a Provider the location is null, so params are empty");
        }

        [Test]
        public void Given_RouterLocationProviderWithParams_When_Rendered_Then_ParamsAreObserved()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/avatar/123",
                Params = new Dictionary<string, string> { { "id", "123" } },
                Matches = Array.Empty<RouteMatch>(),
            };

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(RouterContext.Location, location, new VNode[]
                {
                    V.Component(ParamsCapture.Render),
                }));

            // Assert
            Assert.That(ParamsCapture.LastParams!["id"], Is.EqualTo("123"), "The provided route param is observed by the descendant");
        }

        #endregion

        #region UseLocation

        [Test]
        public void Given_NoRouterLocationProvider_When_Rendered_Then_LocationIsNull()
        {
            // Arrange
            var tree = new VNode[] { V.Component(LocationCapture.Render) };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(LocationCapture.LastLocation, Is.Null, "Without a Provider the location context default is null");
        }

        [Test]
        public void Given_RouterLocationProvider_When_Rendered_Then_LocationIsObserved()
        {
            // Arrange
            var location = new RouterLocation
            {
                Path = "/room",
                Params = new Dictionary<string, string>(),
                Matches = Array.Empty<RouteMatch>(),
            };

            // Act
            using var mounted = V.Mount(_root,
                V.Provider(RouterContext.Location, location, new VNode[]
                {
                    V.Component(LocationCapture.Render),
                }));

            // Assert
            Assert.That(LocationCapture.LastLocation, Is.SameAs(location), "The descendant observes the exact provided location instance");
        }

        #endregion

        #region Declared nullability

        [Test]
        public void Given_RouterContextHooks_When_ReturnAnnotationsRead_Then_UseLocationIsNullableAndUseParamsIsNot()
        {
            // Arrange
            var useLocation = typeof(Hooks).GetMethod(nameof(Hooks.UseLocation), Type.EmptyTypes)!;
            var useParams = typeof(Hooks).GetMethod(nameof(Hooks.UseParams), Type.EmptyTypes)!;

            // Act
            var annotations = (
                location: NullableAnnotationProbe.ReturnAnnotation(useLocation),
                parameters: NullableAnnotationProbe.ReturnAnnotation(useParams));

            // Assert
            Assert.That(
                annotations,
                Is.EqualTo((
                    NullableAnnotationProbe.Annotation.Nullable,
                    NullableAnnotationProbe.Annotation.NotNullable)),
                "UseLocation hands back the context default with no Provider above the caller, so its "
                + "declaration must admit null; UseParams substitutes an empty dictionary and is the control "
                + "showing the probe separates the two states");
        }

        #endregion

        #region UseLoaderData

        [Test]
        public void Given_ADepthPastTheMatchedRoutes_When_Rendered_Then_LoaderDataIsDefault()
        {
            // Arrange — read at depth 1 from a location that matched nothing, with loader data present so
            // an entry is there to be wrongly returned. The depth has to be supplied: the hook answers a
            // depth of 0 before it ever looks at the match list, so a reader outside an Outlet would take
            // that arm instead of this one whatever the list held.
            var loaderData = new Dictionary<string, object> { { "route", "loaded" } };

            // Act
            using var mounted = V.Mount(_root, RouterProviders(
                LocationMatching(),
                loaderData,
                depth: 1,
                V.Component(LoaderDataCapture.Render)));

            // Assert
            Assert.That(LoaderDataCapture.LastData, Is.Null,
                "No route matched at the reader's depth, so there is no key to read loader data by and the "
                + "default is returned rather than an entry the map happens to hold");
        }

        [Test]
        public void Given_LoaderDataForEveryMatch_When_ReadAtANestedDepth_Then_ReturnsThatDepthsRoute()
        {
            // Arrange — three matched routes, each with its own loader entry, read at depth 2. The three
            // values differ so the answer separates this depth's route from the outermost and the innermost.
            var loaderData = new Dictionary<string, object>
            {
                { "root", "root-data" },
                { "middle", "middle-data" },
                { "leaf", "leaf-data" },
            };

            // Act
            using var mounted = V.Mount(_root, RouterProviders(
                LocationMatching("root", "middle", "leaf"),
                loaderData,
                depth: 2,
                V.Component(LoaderDataCapture.Render)));

            // Assert
            Assert.That(LoaderDataCapture.LastData, Is.EqualTo("middle-data"),
                "Loader data is read for the route matched at the reader's own Outlet depth, not for the "
                + "outermost or the innermost match");
        }

        #endregion

        // A location whose match list carries one entry per routeId, parent first.
        private static RouterLocation LocationMatching(params string[] routeIds)
            => new()
            {
                Path = "/" + string.Join("/", routeIds),
                Params = new Dictionary<string, string>(),
                Matches = Array.ConvertAll(routeIds, id => new RouteMatch { RouteId = id }),
            };

        // The three contexts an Outlet-rendered route reads loader data through. Depth is supplied
        // directly because V.Outlet is what writes it, and an Outlet renders the route it matched — so a
        // depth the match list does not reach cannot be arranged by mounting one.
        private static VNode RouterProviders(
            RouterLocation location, IReadOnlyDictionary<string, object> loaderData, int depth, VNode child)
            => V.Provider(RouterContext.Location, location, new VNode[]
            {
                V.Provider(RouterContext.LoaderData, loaderData, new VNode[]
                {
                    V.Provider(RouterContext.Depth, depth, new[] { child }),
                }),
            });

        #region Capture components

        private static class ParamsCapture
        {
            public static IReadOnlyDictionary<string, string>? LastParams;

            public static void Reset() => LastParams = null;

            [Component]
            public static VNode Render()
            {
                var location = Hooks.UseContext(RouterContext.Location);
                LastParams = location?.Params ?? new Dictionary<string, string>();
                return V.Label(text: "params");
            }
        }

        private static class LoaderDataCapture
        {
            // A non-null starting value, so a mount that never reached the component cannot satisfy an
            // assertion that the hook returned the default.
            public static string? LastData = Unrendered;

            private const string Unrendered = "unrendered";

            public static void Reset() => LastData = Unrendered;

            [Component]
            public static VNode Render()
            {
                LastData = Hooks.UseLoaderData<string>();
                return V.Label(text: "loader-data");
            }
        }

        private static class LocationCapture
        {
            public static RouterLocation? LastLocation;

            public static void Reset() => LastLocation = null;

            [Component]
            public static VNode Render()
            {
                LastLocation = Hooks.UseContext(RouterContext.Location);
                return V.Label(text: "location");
            }
        }

        #endregion
    }
}
