namespace Velvet.Tests
{
    /// <summary>
    /// Mount shapes for a routing fixture that renders the component under test directly rather than as a
    /// route element.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="RouteTestStubs"/>, which most routing fixtures import, so that a fixture
    /// publishing no router by hand does not depend on <see cref="RouterContext.Router"/>.
    /// </remarks>
    internal static class RouteTestMounts
    {
        /// <summary>
        /// Publishes <paramref name="router"/> and its current location above <paramref name="child"/> without
        /// the Outlet <c>V.RouterProvider</c> renders.
        /// </summary>
        public static VNode WithRouter(Router router, VNode child)
            => V.Provider(RouterContext.Router, router, children: new VNode[]
            {
                V.Provider(RouterContext.Location, router.CurrentLocation, children: new[] { child }),
            });
    }
}
