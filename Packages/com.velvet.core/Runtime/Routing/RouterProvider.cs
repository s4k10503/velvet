#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    internal static class RouterProvider
    {
        public sealed record Props(Router Router);

        [Component]
        public static VNode Render(Props p)
        {
            var router = p.Router;
            var (location, setLocation) = Hooks.UseState(router.CurrentLocation);

            Hooks.UseEffect(() =>
            {
                void Handle(RouterLocation next) => setLocation.Invoke(next);

                router.OnLocationChanged += Handle;
                // A navigation that commits between this render and the effect attaching raises its event
                // with nobody subscribed, so the location is re-read once the subscription is in place.
                setLocation.Invoke(router.CurrentLocation);
                return (Action)(() => router.OnLocationChanged -= Handle);
            }, new object[] { router });

            // Loader data and errors are read from the router at render rather than held in state of their
            // own: what re-renders this component is the location above, and
            // Router.RepublishCurrentLocation is what gives a loader resolving after the commit a location
            // identity to re-render on.
            return V.Provider(RouterContext.Location, location,
                children: new VNode[]
                {
                    V.Provider(
                        RouterContext.LoaderData,
                        (IReadOnlyDictionary<string, object>)router.CurrentLoaderData,
                        children: new VNode[]
                        {
                            V.Provider(
                                RouterContext.Errors,
                                (IReadOnlyDictionary<string, Exception>)router.CurrentLoaderErrors,
                                children: new VNode[] { V.Outlet() }),
                        }),
                });
        }
    }
}
