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
    /// Specifies the render count of an Outlet-mounted route Component across reconciles, pinning how
    /// opt-in memoization gates the props-equality bail.
    /// <list type="bullet">
    /// <item>A matched route Component renders exactly once on the initial Outlet mount.</item>
    /// <item>A plain (non-memo) route Component re-renders exactly once per location-invariant, slot-stable
    /// Outlet reconcile: the churn is bounded to one render per reconcile and never compounds.</item>
    /// <item>A memoized (<c>Memoize = true</c>) route Component with shallow-equal props bails the re-render of
    /// a preserved fiber on a location-invariant, slot-stable Outlet reconcile.</item>
    /// <item>Memoization bails a preserved fiber's re-render, not a mount: when a slot shift remounts the route
    /// fiber, even a memoized route Component renders again.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The matched route Component is wrapper-mounted on the Outlet container. On the existing-fiber path the
    /// wrapper-mount reconcile treats a non-memo Component's props as always changed, so each Outlet reconcile
    /// schedules an async re-render of the route Component; a memoized Component bails on shallow-equal props.
    /// The slot-stable layout (<c>LayoutWithOutletRender</c>) patches the Outlet in place so the route fiber is
    /// preserved, while the slot-shifting layout (<c>LayoutWithShiftingOutletRender</c>) drops the leading
    /// sibling so the unkeyed position mismatch remounts the Outlet container.
    /// </remarks>
    [TestFixture]
    internal sealed class OutletRouteRerenderTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_nonMemoRouteRenderCount = 0;
            s_memoRouteRenderCount = 0;
            s_layoutSetTick = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static int s_nonMemoRouteRenderCount;
        private static int s_memoRouteRenderCount;
        private static Action<int>? s_layoutSetTick;

        // Plain (non-memo) route Component, mounted by the Outlet as the matched route's Element.
        [Component]
        private static VNode NonMemoRouteRender()
        {
            s_nonMemoRouteRenderCount++;
            return V.Label(name: "route", text: "non-memo-route");
        }

        // Memoized route Component. With no props (both-null), shallow-equal props bail the re-render of a
        // preserved fiber, so it does not re-render on a location-invariant parent reconcile that keeps the
        // Outlet's slot stable.
        [Component(Memoize = true)]
        private static VNode MemoRouteRender()
        {
            s_memoRouteRenderCount++;
            return V.Label(name: "route", text: "memo-route");
        }

        // Layout that owns the Outlet and a tick state. Bumping the tick forces a top-down re-render of the
        // layout, which reconciles its Outlet without changing the router location. Only the leading sibling's
        // text changes; the children structure (Label, Outlet) is stable, so the Outlet patches in place at the
        // same slot. This keeps the wrapper-mounted route fiber preserved, exercising the existing-fiber
        // re-render path where memoization decides bail vs re-render.
        [Component]
        private static VNode LayoutWithOutletRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_layoutSetTick = setTick;
            return V.Div(children: new VNode[] { V.Label(text: $"header-{tick}"), V.Outlet() });
        }

        // Layout that shifts the Outlet's slot on re-render by dropping the leading sibling. The unkeyed
        // Label->Outlet position mismatch forces a remount of the Outlet container (and the route fiber
        // wrapper-mounted on it), pinning that memoization does not bail a remount.
        [Component]
        private static VNode LayoutWithShiftingOutletRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_layoutSetTick = setTick;
            return tick == 0
                ? V.Div(children: new VNode[] { V.Label(text: "header"), V.Outlet() })
                : V.Div(children: new VNode[] { V.Outlet() });
        }

        /// <summary>
        /// Mounts the router-root Provider chain above a layout Component that owns the Outlet, mirroring the
        /// application's router root. The route table's matched Element is supplied by the caller, and
        /// <paramref name="layout"/> selects the slot-stable or slot-shifting layout.
        /// </summary>
        private MountedTree MountLayoutWithRouter(Router router, Func<VNode> layout)
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
                                    children: new VNode[]
                                    {
                                        V.Component(layout, key: "layout"),
                                    }),
                            }),
                    }));
        }

        private Router BuildRouterFor(Func<VNode> routeComponent)
        {
            var router = new Router(new[]
            {
                Route("home", element: V.Component(routeComponent, key: "route")),
            });
            router.NavigateSync("/home");
            return router;
        }

        [Test]
        public void Given_NonMemoRoute_When_InitialMount_Then_RendersExactlyOnce()
        {
            // Arrange
            var router = BuildRouterFor(NonMemoRouteRender);

            // Act
            using var mounted = MountLayoutWithRouter(router, LayoutWithOutletRender);

            // Assert
            Assert.That(s_nonMemoRouteRenderCount, Is.EqualTo(1),
                "The matched route Component renders exactly once on the initial Outlet mount");
        }

        [Test]
        public void Given_NonMemoRoute_When_LocationInvariantReconcile_Then_ReRendersExactlyOnce()
        {
            // The slot-stable layout patches the Outlet in place, so the wrapper-mounted route fiber is
            // preserved; a non-memo Component treats its props as changed and re-renders once per reconcile.
            // Arrange
            var router = BuildRouterFor(NonMemoRouteRender);
            using var mounted = MountLayoutWithRouter(router, LayoutWithOutletRender);
            Assume.That(s_nonMemoRouteRenderCount, Is.EqualTo(1), "Precondition: the initial mount rendered once");

            // Act
            s_layoutSetTick!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_nonMemoRouteRenderCount, Is.EqualTo(2),
                "A non-memo route Component re-renders once per location-invariant Outlet reconcile");
        }

        [Test]
        public void Given_NonMemoRoute_When_SecondLocationInvariantReconcile_Then_ChurnStaysBounded()
        {
            // Each subsequent location-invariant reconcile adds exactly one render; the churn does not compound.
            // Arrange
            var router = BuildRouterFor(NonMemoRouteRender);
            using var mounted = MountLayoutWithRouter(router, LayoutWithOutletRender);
            s_layoutSetTick!.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_nonMemoRouteRenderCount, Is.EqualTo(2), "Precondition: one reconcile added one render");

            // Act
            s_layoutSetTick!.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_nonMemoRouteRenderCount, Is.EqualTo(3),
                "A second reconcile adds exactly one more render (bounded, non-compounding churn)");
        }

        [Test]
        public void Given_MemoRoute_When_LocationInvariantSlotStableReconcile_Then_BailsReRender()
        {
            // A memoized route Component with both-null props is shallow-equal across reconciles, so the
            // preserved-fiber re-render is bailed.
            // Arrange
            var router = BuildRouterFor(MemoRouteRender);
            using var mounted = MountLayoutWithRouter(router, LayoutWithOutletRender);
            Assume.That(s_memoRouteRenderCount, Is.EqualTo(1), "Precondition: the initial mount rendered the memo route once");

            // Act
            s_layoutSetTick!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoRouteRenderCount, Is.EqualTo(1),
                "A memoized route Component bails the location-invariant, slot-stable Outlet reconcile");
        }

        [Test]
        public void Given_MemoRoute_When_SlotShiftRemountsIt_Then_StillRenders()
        {
            // Dropping the leading sibling shifts the Outlet's slot; the unkeyed position mismatch remounts the
            // Outlet container and the route fiber. Memoization bails a preserved fiber's re-render, not a
            // mount, so the remounted memo route Component renders again.
            // Arrange
            var router = BuildRouterFor(MemoRouteRender);
            using var mounted = MountLayoutWithRouter(router, LayoutWithShiftingOutletRender);
            Assume.That(s_memoRouteRenderCount, Is.EqualTo(1), "Precondition: the initial mount rendered the memo route once");

            // Act
            s_layoutSetTick!.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_memoRouteRenderCount, Is.EqualTo(2),
                "A memoized route Component still renders when a slot shift remounts it (mount is not a bail)");
        }
    }
}
