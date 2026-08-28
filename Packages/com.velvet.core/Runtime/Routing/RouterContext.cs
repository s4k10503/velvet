using System;
using System.Collections.Generic;

namespace Velvet
{
    public static class RouterContext
    {
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

        /// <summary>Tracks Outlet depth for match selection and route-relative navigation.</summary>
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
