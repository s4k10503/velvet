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

            public static void Reset()
            {
                Navigate = null;
                SetSearchParams = null;
                Navigation = default;
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
                _ = Hooks.UseLocation();
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
    }
}
