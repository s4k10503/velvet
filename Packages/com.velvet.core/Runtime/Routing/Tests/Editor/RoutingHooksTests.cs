// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the descendant routing hooks observed from a route Component rendered through an Outlet:
    /// <c>UseLocation</c> / <c>UseParams</c> / <c>UseLoaderData</c> / <c>UseRouteError</c> /
    /// <c>UseOutletContext</c>.
    /// <list type="bullet">
    /// <item><c>UseLocation</c> returns the router's current location.</item>
    /// <item><c>UseParams</c> returns the params captured for the matched route.</item>
    /// <item><c>UseLoaderData</c> returns the loader result of the current route.</item>
    /// <item><c>UseRouteError</c> returns null when the route loaded cleanly, and the loader's thrown exception
    /// when the loader failed.</item>
    /// <item><c>UseOutletContext</c> returns the value the enclosing Outlet supplies.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The hooks need the live router-root Provider chain (Location / LoaderData / Errors) above an Outlet, so
    /// each test navigates a real <see cref="Router"/> and mounts that chain via <c>MountWithRouter</c>. The
    /// captured values are exposed through the <c>Capture</c> static component, reset in <c>SetUp</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class RoutingHooksTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static class Capture
        {
            public static RouterLocation? Location;
            public static IReadOnlyDictionary<string, string>? Params;
            public static string? LoaderData;
            public static Exception? Error;
            public static object? OutletContext;

            public static void Reset()
            {
                Location = null;
                Params = null;
                LoaderData = null;
                Error = null;
                OutletContext = null;
            }

            [Component]
            public static VNode Render()
            {
                Location = Hooks.UseLocation();
                Params = Hooks.UseParams();
                LoaderData = Hooks.UseLoaderData<string>();
                Error = Hooks.UseRouteError();
                OutletContext = Hooks.UseOutletContext<object>();
                return V.Label(text: "capture");
            }
        }

        /// <summary>
        /// Mounts the router-root provider chain (Location / LoaderData / Errors) above an Outlet, exactly as
        /// the application's router root does, driving rendering of the matched route at depth 1.
        /// </summary>
        private MountedTree MountWithRouter(Router router, object? outletContext = null)
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
                                    children: new VNode[] { V.Outlet(context: outletContext) }),
                            }),
                    }));
        }

        [Test]
        public void Given_NavigatedRoute_When_UseLocation_Then_ReturnsCurrentLocationPath()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("dash", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/dash");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Location!.Path, Is.EqualTo("/dash"));
        }

        [Test]
        public void Given_ParamRoute_When_UseParams_Then_ReturnsCapturedParam()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("user/:id", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/user/abc");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Params!["id"], Is.EqualTo("abc"));
        }

        [Test]
        public void Given_LoadedRoute_When_UseLoaderData_Then_ReturnsLoaderResult()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("data",
                    element: V.Component(Capture.Render, key: "cap"),
                    loader: (ctx, ct) => UniTask.FromResult((object)"hello")),
            });
            router.NavigateSync("/data");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.LoaderData, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_CleanlyLoadedRoute_When_UseRouteError_Then_ReturnsNull()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("ok", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/ok");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Error, Is.Null);
        }

        [Test]
        public void Given_ThrowingLoaderRoute_When_UseRouteError_Then_ReturnsThrownException()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("boom",
                    element: V.Component(Capture.Render, key: "cap"),
                    errorElement: V.Component(Capture.Render, key: "cap-error"),
                    loader: (ctx, ct) => throw new InvalidOperationException("loader-boom")),
            });
            router.NavigateSync("/boom");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Error!.Message, Does.Contain("loader-boom"));
        }

        [Test]
        public void Given_OutletSuppliesContext_When_UseOutletContext_Then_ReturnsSuppliedValue()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("ctx", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/ctx");

            // Act
            using var mounted = MountWithRouter(router, outletContext: "from-outlet");

            // Assert
            Assert.That(Capture.OutletContext, Is.EqualTo("from-outlet"));
        }
    }
}
