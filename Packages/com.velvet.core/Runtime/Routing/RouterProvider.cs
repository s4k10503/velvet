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
            var enclosing = Hooks.UseContext(RouterContext.Router);
            var (_, setLocation) = Hooks.UseState(router.CurrentLocation);

            Hooks.UseEffect(() =>
            {
                void Handle(RouterLocation next) => setLocation.Invoke(next);

                router.OnLocationChanged += Handle;
                // A navigation that commits between this render and the effect attaching raises its event
                // with nobody subscribed, so the location is re-read once the subscription is in place.
                setLocation.Invoke(router.CurrentLocation);
                return (Action)(() => router.OnLocationChanged -= Handle);
            }, new object[] { router });

            // After the hooks rather than above them, so every render calls the same ones. React Router's
            // Router refuses the same nesting: the Outlets beneath the inner one would otherwise index its
            // matches with the depth the outer one's Outlets published.
            if (enclosing != null)
            {
                throw new InvalidOperationException(
                    "You cannot render a V.RouterProvider inside another V.RouterProvider.");
            }

            // The location, loader data and errors are read from the router at render, so the render in which
            // the router prop changes publishes the new router's rather than the location last held from the
            // old one. The state above only schedules re-renders, and Router.RepublishCurrentLocation is what
            // gives a loader resolving after the commit a location identity to re-render on.
            return V.Provider(RouterContext.Router, router,
                children: new VNode[]
                {
                    V.Provider(RouterContext.Location, router.CurrentLocation,
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
                        }),
                });
        }
    }
}
