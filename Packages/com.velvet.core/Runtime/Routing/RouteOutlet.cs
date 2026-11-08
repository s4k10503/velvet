#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Not beside its factory in Component/V.cs: same reason Navigate is not — this body calls hooks.
    internal static class RouteOutlet
    {
        // The body creates and releases the Outlet's route scope as it renders, which a cache hit would skip.
        [Component(Compiler = false)]
        public static VNode Render(object? outletContext)
        {
            var location = Hooks.UseContext(RouterContext.Location);
            var depth = Hooks.UseContext(RouterContext.Depth);
            var errors = Hooks.UseContext(RouterContext.Errors);
            var loaderData = Hooks.UseContext(RouterContext.LoaderData);
            var router = Hooks.UseContext(RouterContext.Router);
            // Beneath an errorElement a route's boundary shows for an error thrown below it, an Outlet renders
            // nothing, as React Router renders that errorElement with no outlet.
            var renderError = Hooks.UseContext(RouteErrorBoundary.RenderError);

            if (renderError != null
                || !TryResolveMatch(location, depth, errors, out var routeElement, out var routeDepth, out var match))
            {
                FiberOutletScope.ReleaseRenderingOutletScope();
                return V.Fragment(Array.Empty<VNode>());
            }

            // React Router's _renderMatches: the deepest hydrate fallback above a loader still owed data renders in
            // place of the routes from it down, except where an error is keyed, which renders its error instead.
            var fallbackDepth = HydrateFallbackDepth(location!.Matches!, errors, loaderData);
            if (fallbackDepth >= 0 && depth >= fallbackDepth
                && depth != ResolveErrorBoundaryDepth(location.Matches!, errors))
            {
                routeElement = depth == fallbackDepth ? match!.Route?.HydrateFallbackElement : null;
                if (routeElement == null)
                {
                    FiberOutletScope.ReleaseRenderingOutletScope();
                    return V.Fragment(Array.Empty<VNode>());
                }
            }

            // A route with no element renders its child route in its place, as React Router renders the outlet,
            // under the Outlet context it was itself rendered with.
            routeElement ??= V.Outlet(outletContext);
            FiberOutletScope.SyncRenderingOutletScope(match!.Route, routeElement.ResolvedIdentity,
                router?.ScopeFactory);

            // Depth+1 so a nested Outlet in the route's subtree resolves the following match, and the
            // Outlet's own context value so the route can read it back through Hooks.UseOutletContext.
            return V.Provider(RouterContext.Depth, routeDepth,
                children: new VNode[]
                {
                    V.Provider(RouterContext.OutletContext, outletContext!,
                        children: new VNode[] { WithRenderErrorBoundary(location, match, depth, routeElement) }),
                });
        }

        // One key and one place for every route this Outlet wraps, as React Router's boundary has, so a
        // navigation between two such routes resets it rather than remounting it — RouteErrorBoundary owns the
        // reset.
        //
        // What renders for an error the route's element throws while rendering is its own errorElement, and at the
        // root the default one, as React Router wraps a route with either in its RenderErrorBoundary — around
        // an errorElement rendering for a loader error too.
        private static VNode WithRenderErrorBoundary(
            RouterLocation? location, RouteMatch match, int depth, ComponentNode routeElement)
        {
            var errorElement = match.Route?.ErrorElement
                ?? (depth == 0 ? V.Component(RouteDefaultErrorElement.Render, key: "velvet-default-error-element") : null);
            return errorElement == null
                ? routeElement
                : V.Component(RouteErrorBoundary.Render,
                    new RouteErrorBoundary.Props(location, errorElement, routeElement), key: "velvet-route-error-boundary");
        }

        // The route this Outlet renders, or false for none. depth indexes location.Matches; the returned
        // routeDepth is what the rendered route's own descendants read, so a nested Outlet indexes the next
        // match along.
        private static bool TryResolveMatch(
            RouterLocation? location,
            int depth,
            IReadOnlyDictionary<string, Exception>? errors,
            out ComponentNode? routeElement,
            out int routeDepth,
            out RouteMatch? match)
        {
            routeElement = null;
            routeDepth = 0;
            match = null;

            if (location?.Matches == null || depth >= location.Matches.Count)
            {
                return false;
            }

            match = location.Matches[depth];

            // The route an error is keyed by renders its ErrorElement in place of its Element and descendant
            // Outlet subtree; ancestors above it render normally, and routes below it do not render.
            var boundaryDepth = ResolveErrorBoundaryDepth(location.Matches, errors);
            if (boundaryDepth >= 0)
            {
                var boundaryElement = location.Matches[boundaryDepth].Route?.ErrorElement;

                if (depth == boundaryDepth)
                {
                    // This Outlet renders the boundary route's ErrorElement in place of its Element — the
                    // default one at the implicit root boundary, where no route declared one.
                    routeElement = boundaryElement
                        ?? V.Component(RouteDefaultErrorElement.Render, key: "velvet-default-error-element");
                    routeDepth = depth + 1;
                    return true;
                }

                if (depth > boundaryDepth)
                {
                    // Below the boundary: the ErrorElement subtree replaced everything here, so render nothing.
                    match = null;
                    return false;
                }

                // depth < boundaryDepth: an ancestor above the boundary renders normally below.
            }

            // Null for a route with no element, which Render fills.
            routeElement = match.Route?.Element;
            routeDepth = depth + 1;
            return true;
        }

        // React Router's _renderMatches: the highest matched route an error is keyed by renders it, and nothing
        // below it renders. Returns -1 when no matched route has one. The router keys every error by the
        // boundary that renders it, so this is the boundary.
        private static int ResolveErrorBoundaryDepth(
            IReadOnlyList<RouteMatch> matches,
            IReadOnlyDictionary<string, Exception>? errors)
        {
            if (errors == null)
            {
                return -1;
            }

            var depth = 0;
            while (depth < matches.Count && (matches[depth].RouteId == null || !errors.ContainsKey(matches[depth].RouteId!)))
            {
                depth++;
            }

            return depth < matches.Count ? depth : -1;
        }

        // The depth whose hydrate fallback shows, or -1 for none: over the matches down to the one an error is
        // keyed by, the deepest with a fallback above the first whose Await loader has neither data nor an error,
        // else the root, which then shows nothing.
        private static int HydrateFallbackDepth(
            IReadOnlyList<RouteMatch> matches,
            IReadOnlyDictionary<string, Exception>? errors,
            IReadOnlyDictionary<string, object>? loaderData)
        {
            var errorDepth = ResolveErrorBoundaryDepth(matches, errors);
            var last = errorDepth < 0 ? matches.Count - 1 : errorDepth;
            var fallbackDepth = -1;
            for (var i = 0; i <= last; i++)
            {
                var route = matches[i].Route;
                if (route?.HydrateFallbackElement != null)
                {
                    fallbackDepth = i;
                }
                if (OwesData(matches[i], errors, loaderData))
                {
                    return fallbackDepth >= 0 ? fallbackDepth : 0;
                }
            }
            return -1;
        }

        private static bool OwesData(
            RouteMatch match,
            IReadOnlyDictionary<string, Exception>? errors,
            IReadOnlyDictionary<string, object>? loaderData)
        {
            var route = match.Route;
            var id = match.RouteId;
            return route?.Loader != null
                && route.LoaderMode == LoaderMode.Await
                && id != null
                && (loaderData == null || !loaderData.ContainsKey(id))
                && (errors == null || !errors.ContainsKey(id));
        }

        // The route at or above index that renders an error there: the nearest with an ErrorElement, or the
        // root, which renders the default one.
        internal static int NearestErrorBoundary(IReadOnlyList<RouteMatch> matches, int index)
        {
            // MUTANT_SURVIVES(equivalent, boundary): stopping one short of the root changes nothing, since the
            // root's own iteration and the fallthrough below both return 0.
            for (var i = index; i >= 0; i--)
            {
                if (matches[i].Route?.ErrorElement != null)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
