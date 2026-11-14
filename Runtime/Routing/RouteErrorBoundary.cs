#nullable enable
using System;

namespace Velvet
{
    // React Router's RenderErrorBoundary: what an Outlet wraps a route's element in where the route declares an
    // errorElement, and the root route in any case, so an error below the element renders that errorElement,
    // which reads the error through Hooks.UseRouteError. Not beside its factory's callers in RouteOutlet: this
    // body calls hooks.
    internal static class RouteErrorBoundary
    {
        // React Router's RouteErrorContext, for an error this boundary caught.
        internal static readonly ComponentContext<Exception?> RenderError = ComponentContext<Exception?>.Create(null);

        internal sealed record Props(RouterLocation? Location, VNode ErrorElement, VNode Element);

        // A boundary keeps what it caught until it remounts (FiberErrorBoundary.OutputOf); this one lets it go on
        // each location the router publishes, as React Router's getDerivedStateFromProps does on a new location
        // object. The router publishes one on a navigation and when a Suspend loader of the current location
        // settles. The boundary itself stays mounted, as React Router's does, so the elements below it are not
        // remounted by a navigation that leaves no error.
        [Component(IsErrorBoundary = true)]
        public static VNode Render(Props p)
        {
            FiberAmbientStack.Current!.IsFrameworkErrorBoundary = true;
            var shownAt = Hooks.UseRef<RouterLocation>();
            if (!ReferenceEquals(shownAt.Current, p.Location))
            {
                FiberAmbientStack.Current!.CaughtError = null;
                shownAt.Set(p.Location);
            }
            Hooks.UseFallback(error => V.Provider(RenderError, error, new VNode?[] { p.ErrorElement }));
            return p.Element;
        }
    }
}
