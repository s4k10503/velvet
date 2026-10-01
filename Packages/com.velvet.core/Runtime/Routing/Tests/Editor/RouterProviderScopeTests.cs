// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which router a routing hook answers to: the one the nearest <c>V.RouterProvider</c>
    /// publishes, as React Router's hooks answer to the nearest <c>RouterProvider</c>'s.
    /// <list type="bullet">
    /// <item>With a second router built after the provider's, the hooks that act on a router still act on the
    /// provider's.</item>
    /// <item>With no provider above them, the hooks whose React Router counterparts refuse to run outside a
    /// router throw, naming themselves.</item>
    /// <item><c>UseParams</c>, <c>UseOutletContext</c> and <c>V.Outlet</c> answer there instead, as React
    /// Router's do.</item>
    /// <item>When a provider's router changes, the render of that change reads the new router.</item>
    /// <item>A <c>V.RouterProvider</c> beneath another throws, as React Router's <c>Router</c> does.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterProviderScopeTests
    {
        private VisualElement _root = null!;
        private Router? _provided;
        private Router? _other;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Probe.Reset();
            s_hostRouter = null;
            s_setHostRouter = default;
        }

        [TearDown]
        public void TearDown()
        {
            _provided?.Dispose();
            _other?.Dispose();
            _provided = null;
            _other = null;
            _root = null!;
        }

        private static class Probe
        {
            public static Func<string, VelvetTask<NavigationResult>>? Navigate;
            public static SearchParamsSetter? SetSearchParams;
            public static NavigationState Navigation;
            public static string? LocationPath;

            public static void Reset()
            {
                Navigate = null;
                SetSearchParams = null;
                Navigation = default;
                LocationPath = null;
            }

            public static ComponentNode For(string hook)
            {
                Func<VNode> body = hook switch
                {
                    "UseLocation" => Location,
                    "UseNavigate" => NavigateHook,
                    "UseMatch" => Match,
                    "UseSearchParams" => SearchParamsHook,
                    "UseNavigation" => NavigationHook,
                    "UseBlocker" => Blocker,
                    "UseLoaderData" => LoaderData,
                    "UseRouteError" => RouteError,
                    "UseSubmit" => Submit,
                    "UseActionData" => ActionData,
                    "UseParams" => Params,
                    "UseOutletContext" => OutletContext,
                    "Outlet" => Outlet,
                    _ => throw new ArgumentException(hook),
                };
                return V.Component(body, key: "probe");
            }

            private static VNode Rendered() => V.Label(text: "probe");

            [Component]
            private static VNode Location()
            {
                LocationPath = Hooks.UseLocation()?.Path;
                return Rendered();
            }

            [Component]
            private static VNode NavigateHook()
            {
                Navigate = Hooks.UseNavigate();
                return Rendered();
            }

            [Component]
            private static VNode Match()
            {
                _ = Hooks.UseMatch("start");
                return Rendered();
            }

            [Component]
            private static VNode SearchParamsHook()
            {
                (_, SetSearchParams) = Hooks.UseSearchParams();
                return Rendered();
            }

            [Component]
            private static VNode NavigationHook()
            {
                Navigation = Hooks.UseNavigation();
                return Rendered();
            }

            [Component]
            private static VNode Blocker()
            {
                _ = Hooks.UseBlocker(_ => true);
                return Rendered();
            }

            [Component]
            private static VNode LoaderData()
            {
                _ = Hooks.UseLoaderData<string>();
                return Rendered();
            }

            [Component]
            private static VNode RouteError()
            {
                _ = Hooks.UseRouteError();
                return Rendered();
            }

            [Component]
            private static VNode Submit()
            {
                _ = Hooks.UseSubmit();
                return Rendered();
            }

            [Component]
            private static VNode ActionData()
            {
                _ = Hooks.UseActionData<string>();
                return Rendered();
            }

            [Component]
            private static VNode Params()
            {
                _ = Hooks.UseParams();
                return Rendered();
            }

            [Component]
            private static VNode OutletContext()
            {
                _ = Hooks.UseOutletContext<object>();
                return Rendered();
            }

            [Component]
            private static VNode Outlet() => V.Div(children: new VNode[] { Rendered(), V.Outlet() });
        }

        // The provider's router, built first, and a second one built after it, which Router.Current then
        // names.
        private (Router provided, Router other) TwoRouters(ComponentNode startElement,
            Func<RouteLoaderContext, System.Threading.CancellationToken, VelvetTask<object>>? nextLoader = null)
        {
            _provided = BuildRouter("/start",
                Route("start", element: startElement),
                Route("next", loader: nextLoader));
            _other = BuildRouter("/start", Route("start"), Route("next"));
            return (_provided, _other);
        }

        private MountedTree MountUnderBoundary(VNode child, Action<Exception> onCaught)
            => V.Mount(_root, V.ErrorBoundary(
                fallback: ex =>
                {
                    onCaught(ex);
                    return V.Label(text: "boundary-fallback");
                },
                children: new[] { child },
                key: "boundary"), CaughtErrors.Unlogged);

        #region Which router the hooks act on

        [Test]
        public void Given_ASecondRouterBuiltAfterTheProvidersRouter_When_UseNavigatesDelegateIsCalled_Then_TheProvidersRouterNavigates()
        {
            // Arrange
            var (provided, other) = TwoRouters(Probe.For("UseNavigate"));
            using var mounted = V.Mount(_root, V.RouterProvider(provided));

            // Act
            Probe.Navigate!("/next").GetAwaiter().GetResult();

            // Assert
            Assert.That($"provided={provided.CurrentLocation!.Path} other={other.CurrentLocation!.Path}",
                Is.EqualTo("provided=/next other=/start"));
        }

        [Test]
        public void Given_ASecondRouterBuiltAfterTheProvidersRouter_When_UseSearchParamsSetterIsCalled_Then_TheProvidersRouterNavigates()
        {
            // Arrange
            var (provided, other) = TwoRouters(Probe.For("UseSearchParams"));
            using var mounted = V.Mount(_root, V.RouterProvider(provided));

            // Act
            Probe.SetSearchParams!.Invoke(_ =>
            {
                var next = new SearchParams();
                next.Append("q", "1");
                return next;
            });

            // Assert
            Assert.That($"provided={provided.CurrentLocation!.Path} other={other.CurrentLocation!.Path}",
                Is.EqualTo("provided=/start?q=1 other=/start"));
        }

        [Test]
        public void Given_ASecondRouterBuiltAfterTheProvidersRouter_When_TheProvidersRouterStartsLoading_Then_UseNavigationReportsIt()
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            var (provided, _) = TwoRouters(Probe.For("UseNavigation"), (_, _) => pending.Task);
            using var mounted = V.Mount(_root, V.RouterProvider(provided));
            mounted.FlushEffectsForTest();

            // Act
            provided.NavigateAsync("/next").Forget();
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Probe.Navigation.State, Is.EqualTo(NavigationLifecycle.Loading));
        }

        [Test]
        public void Given_ASecondRouterBuiltAfterTheProvidersRouter_When_TheProvidersRouterNavigates_Then_UseBlockersPredicateIsConsulted()
        {
            // Arrange
            var (provided, _) = TwoRouters(Probe.For("UseBlocker"));
            using var mounted = V.Mount(_root, V.RouterProvider(provided));
            mounted.FlushEffectsForTest();

            // Act
            var result = provided.NavigateSync("/next");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        #endregion

        #region Outside a router

        [TestCase("UseLocation")]
        [TestCase("UseNavigate")]
        [TestCase("UseMatch")]
        [TestCase("UseSearchParams")]
        [TestCase("UseNavigation")]
        [TestCase("UseBlocker")]
        [TestCase("UseLoaderData")]
        [TestCase("UseRouteError")]
        [TestCase("UseSubmit")]
        [TestCase("UseActionData")]
        public void Given_NoRouterProviderAbove_When_AComponentCallsTheHook_Then_ItsRenderThrowsNamingTheHook(string hook)
        {
            // Arrange
            Exception? caught = null;

            // Act
            using var mounted = MountUnderBoundary(Probe.For(hook), ex => caught = ex);

            // Assert
            Assert.That(caught?.Message, Is.EqualTo($"{hook} may be used only beneath a V.RouterProvider."));
        }

        // GREEN_ON_BASE(characterization): these three already answered without a router, as React Router's
        // useParams, useOutletContext and Outlet do; the branch makes the hooks beside them throw there.
        [TestCase("UseParams")]
        [TestCase("UseOutletContext")]
        [TestCase("Outlet")]
        public void Given_NoRouterProviderAbove_When_AComponentCallsTheHook_Then_ItRenders(string hook)
        {
            // Arrange
            Exception? caught = null;

            // Act
            using var mounted = MountUnderBoundary(Probe.For(hook), ex => caught = ex);

            // Assert
            Assert.That((caught?.Message, _root.FindLabelByText("probe") != null), Is.EqualTo(((string?)null, true)));
        }

        #endregion

        #region A provider whose router changes

        private static Router? s_hostRouter;
        private static StateUpdater<Router> s_setHostRouter;

        [Component]
        private static VNode SwitchingHost()
        {
            var (router, setRouter) = Hooks.UseState(s_hostRouter!);
            s_setHostRouter = setRouter;
            return V.RouterProvider(router);
        }

        [Test]
        public void Given_AProviderWhoseRouterChangesToOneThatIsLoading_When_TheChangeRenders_Then_UseNavigationReportsLoading()
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource<object>();
            var probe = Probe.For("UseNavigation");
            // The loading router is built first, so a hook reading Router.Current rather than the provider's
            // router reads the idle one.
            _other = BuildRouter("/start", Route("start", element: probe), Route("next", loader: (_, _) => pending.Task));
            _other.NavigateAsync("/next").Forget();
            _provided = BuildRouter("/start", Route("start", element: probe));
            s_hostRouter = _provided;
            using var mounted = V.Mount(_root, V.Component(SwitchingHost, key: "host"));
            mounted.FlushEffectsForTest();

            // Act — no passive effect runs between the change and the reading.
            s_setHostRouter.Invoke(_other);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Probe.Navigation.State, Is.EqualTo(NavigationLifecycle.Loading));
        }

        [Test]
        public void Given_AProviderWhoseRouterChanges_When_TheChangeRenders_Then_UseLocationReadsTheNewRoutersLocation()
        {
            // Arrange
            var probe = Probe.For("UseLocation");
            _provided = BuildRouter("/start", Route("start", element: probe));
            _other = BuildRouter("/start?from=other", Route("start", element: probe));
            s_hostRouter = _provided;
            using var mounted = V.Mount(_root, V.Component(SwitchingHost, key: "host"));
            mounted.FlushEffectsForTest();

            // Act — no passive effect runs between the change and the reading.
            s_setHostRouter.Invoke(_other);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Probe.LocationPath, Is.EqualTo("/start?from=other"));
        }

        #endregion

        #region Nested providers

        [Test]
        public void Given_ARouterProviderAsARouteElementOfAnother_When_Mounted_Then_TheRootsDefaultErrorElementShowsItsRenderError()
        {
            // Arrange — the throw is a route element's, so the root route's default errorElement renders it, as
            // React Router's RenderErrorBoundary does, and logs it
            _other = BuildRouter("/inner", Route("inner"));
            _provided = BuildRouter("/start", Route("start", element: V.RouterProvider(_other)));
            Exception? caught = null;
            const string Message = "You cannot render a V.RouterProvider inside another V.RouterProvider.";
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Exception, "InvalidOperationException: " + Message);

            // Act
            using var mounted = MountUnderBoundary(V.RouterProvider(_provided), ex => caught = ex);
            mounted.FlushEffectsForTest();

            // Assert — the boundary around the router is read too: what the route caught never reaches it
            Assert.That((caught?.Message, _root.FindLabelByText(Message) != null), Is.EqualTo(((string?)null, true)));
        }

        #endregion
    }
}
