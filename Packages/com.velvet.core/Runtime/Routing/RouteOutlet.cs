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

            FiberOutletScope.SyncRenderingOutletScope(match!.Route, routeElement!.ResolvedIdentity,
                router?.ScopeFactory);

            // Depth+1 so a nested Outlet in the route's subtree resolves the following match, and the
            // Outlet's own context value so the route can read it back through Hooks.UseOutletContext.
            return V.Provider(RouterContext.Depth, routeDepth,
                children: new VNode[]
                {
                    V.Provider(RouterContext.OutletContext, outletContext!,
                        children: new VNode[] { WithRenderErrorBoundary(location, match, depth, routeElement!) }),
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

            // A loader error bubbles to the nearest ancestor route (at or above the errored route) that
            // defines an ErrorElement. That boundary route renders its ErrorElement in place of its Element
            // and descendant Outlet subtree; ancestors above the boundary render normally, and routes below
            // the boundary do not render. Errors are keyed by RouteId on RouterContext.Errors.
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

            routeElement = match.Route?.Element;
            if (routeElement == null)
            {
                match = null;
                return false;
            }

            routeDepth = depth + 1;
            return true;
        }

        // The nearest route, scanning from the deepest errored route up toward the root, that defines a
        // RouteDefinition.ErrorElement. Returns -1 when no route errored. When a route errored but no route
        // at or above it defines an ErrorElement, returns the root index 0 as an implicit boundary.
        private static int ResolveErrorBoundaryDepth(
            IReadOnlyList<RouteMatch> matches,
            IReadOnlyDictionary<string, Exception>? errors)
        {
            // MUTANT_SURVIVES(equivalent): a fast path over an answer the scan below reaches anyway.
            // Its one caller has already rejected a null Matches and a depth past its end and indexed
            // Matches at that depth, so both matches terms here are false, and where errors is empty the
            // scan finds no errored route and returns the same -1. The null term is the one that decides
            // something: it short-circuits ahead of a Count read on the same reference.
            if (errors == null || errors.Count == 0 || matches == null || matches.Count == 0)
            {
                return -1;
            }

            // The deepest errored route determines the boundary: a deeper error truncates the chain at the
            // nearest boundary at or above it, which is at least as deep as any shallower error's boundary.
            var deepestErrored = -1;
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var routeId = matches[i].RouteId;
                if (routeId != null && errors.ContainsKey(routeId))
                {
                    deepestErrored = i;
                    break;
                }
            }

            if (deepestErrored < 0)
            {
                return -1;
            }

            return NearestErrorBoundary(matches, deepestErrored);
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
