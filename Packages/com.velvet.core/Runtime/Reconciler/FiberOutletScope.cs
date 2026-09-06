#nullable enable

namespace Velvet
{
    // The route scope an Outlet holds, and where it is kept: on the Outlet's own ComponentFiber, in
    // ReconcilerContext.OutletScopes. The entry pairs the scope with the identity of the route element it
    // was built for, which is what decides a route change here.
    internal static class FiberOutletScope
    {
        internal readonly struct Entry
        {
            internal object RouteIdentity { get; init; }
            internal IRouteScope Scope { get; init; }
        }

        // Called from the Outlet's render for the route it is about to render. Reaching a route the held
        // scope was not built for is what replaces it.
        internal static void SyncRenderingOutletScope(RouteDefinition? route, object routeIdentity)
        {
            var fiber = FiberAmbientStack.Current;
            var ctx = fiber?.Reconciler?.Context;
            if (fiber == null || ctx == null)
            {
                return;
            }

            if (ctx.OutletScopes.TryGetValue(fiber, out var held))
            {
                if (Equals(held.RouteIdentity, routeIdentity))
                {
                    return;
                }
                Release(ctx, fiber, held);
            }

            var scope = CreateOutletScope(ctx, route);
            if (scope != null)
            {
                ctx.OutletScopes[fiber] = new Entry { RouteIdentity = routeIdentity, Scope = scope };
            }
        }

        // Called from the Outlet's render when it resolves no route: the scope goes with the route it was
        // built for, matching the disposal a route change makes.
        internal static void ReleaseRenderingOutletScope()
        {
            var fiber = FiberAmbientStack.Current;
            if (fiber != null)
            {
                Release(fiber);
            }
        }

        // Called from FiberRenderer.Unmount, beside the fiber's other held resources.
        internal static void Release(ComponentFiber fiber)
        {
            var ctx = fiber.Reconciler?.Context;
            if (ctx != null && ctx.OutletScopes.TryGetValue(fiber, out var held))
            {
                Release(ctx, fiber, held);
            }
        }

        // Dropped and logged where CreateOutletScope below propagates: the application decides what a
        // scope-less route does, and has no decision left to make about a scope it has finished with --
        // which is what every path into here hands it: a route replaced, a route resolved away, and an
        // Outlet unmounting.
        private static void Release(ReconcilerContext ctx, ComponentFiber fiber, Entry held)
        {
            ctx.OutletScopes.Remove(fiber);
            try
            {
                held.Scope.Dispose();
            }
            catch (System.Exception exception)
            {
                FiberLogger.LogException("FiberOutletScope", exception);
            }
        }

        // The scope is the route's, not the render's: a factory that throws leaves the route to render
        // without one rather than taking the reconcile with it. Contained rather than swallowed --
        // ContainUserCallbackFailure reaches the nearest error boundary, so an application that wants a
        // scope-less route refused still gets to refuse it. A drop-and-log -- what Release above does with
        // the departing scope's Dispose -- would have decided that for it.
        private static IRouteScope? CreateOutletScope(ReconcilerContext ctx, RouteDefinition? route)
        {
            var scopeFactory = Router.Current?.ScopeFactory;
            if (scopeFactory == null)
            {
                return null;
            }
            try
            {
                return scopeFactory.CreateScope(route, null);
            }
            catch (System.Exception exception)
            {
                ReconcilerContext.ContainUserCallbackFailure(ctx.FiberStack.Current, exception);
                return null;
            }
        }
    }
}
