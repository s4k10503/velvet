#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Not beside its factory in Component/V.cs: same reason Navigate is not — this body calls hooks.
    internal static class RouteOutlet
    {
        [Component]
        public static VNode Render(object? outletContext)
        {
            var location = Hooks.UseContext(RouterContext.Location);
            var depth = Hooks.UseContext(RouterContext.Depth);
            var errors = Hooks.UseContext(RouterContext.Errors);

            if (!TryResolveMatch(location, depth, errors, out var routeElement, out var routeDepth, out var match))
            {
                FiberOutletScope.ReleaseRenderingOutletScope();
                return V.Fragment(Array.Empty<VNode>());
            }

            FiberOutletScope.SyncRenderingOutletScope(match!.Route, routeElement!.ResolvedIdentity);

            // Depth+1 so a nested Outlet in the route's subtree resolves the following match, and the
            // Outlet's own context value so the route can read it back through Hooks.UseOutletContext.
            return V.Provider(RouterContext.Depth, routeDepth,
                children: new VNode[]
                {
                    V.Provider(RouterContext.OutletContext, outletContext!,
                        children: new VNode[] { routeElement }),
                });
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
                    if (boundaryElement == null)
                    {
                        // Implicit root boundary with no ErrorElement (Velvet has no default error surface):
                        // the error bubbles to the root and, with no boundary defined anywhere in the
                        // chain, the erroring subtree renders nothing.
                        match = null;
                        return false;
                    }

                    // This Outlet renders the boundary route's ErrorElement in place of its Element.
                    routeElement = boundaryElement;
                    routeDepth = depth + 1;
                    return true;
                }

                // MUTANT_SURVIVES(equivalent): the depth == boundaryDepth arm above returns on both paths.
                // This is therefore reached only where the two depths differ.
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
        // at or above it defines an ErrorElement, returns the root index 0 as an implicit boundary (the
        // error bubbles all the way to the root): because Velvet has no default error surface, the caller
        // renders nothing at that boundary, so the erroring matched tree renders nothing.
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

            // MUTANT_SURVIVES(equivalent): the root iteration and the fallthrough below both answer 0.
            // Stopping one short of the root therefore cannot change what this returns.
            for (var i = deepestErrored; i >= 0; i--)
            {
                if (matches[i].Route?.ErrorElement != null)
                {
                    return i;
                }
            }

            // No route at or above the errored route defines an ErrorElement: bubble to the implicit root
            // boundary. The root has no ErrorElement, so the caller renders nothing there.
            return 0;
        }
    }
}
