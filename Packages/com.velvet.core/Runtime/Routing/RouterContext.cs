using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Static contexts that propagate router information through the Velvet component tree.
    /// <c>Router</c> writes to them as the Provider; child components read via UseContext.
    /// </summary>
    public static class RouterContext
    {
        /// <summary>Current navigation location (path, parameters, match information).</summary>
        public static readonly ComponentContext<RouterLocation> Location =
            ComponentContext<RouterLocation>.Create(null);

        /// <summary>Loader data corresponding to the current location. Keyed by <see cref="RouteMatch.RouteId"/>.</summary>
        public static readonly ComponentContext<IReadOnlyDictionary<string, object>> LoaderData =
            ComponentContext<IReadOnlyDictionary<string, object>>.Create(
                new Dictionary<string, object>());

        /// <summary>Loader errors for the current location, keyed by <see cref="RouteMatch.RouteId"/>.</summary>
        public static readonly ComponentContext<IReadOnlyDictionary<string, Exception>> Errors =
            ComponentContext<IReadOnlyDictionary<string, Exception>>.Create(
                new Dictionary<string, Exception>());

        /// <summary>Nested-route depth. Used during Outlet rendering to pick the correct match.</summary>
        public static readonly ComponentContext<int> Depth =
            ComponentContext<int>.Create(0);

        /// <summary>
        /// Context value supplied by an <c>Outlet</c> to its rendered child route, consumed by
        /// <c>UseOutletContext</c>.
        /// </summary>
        public static readonly ComponentContext<object> OutletContext =
            ComponentContext<object>.Create(null);
    }
}
