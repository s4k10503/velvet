using UnityEngine.UIElements;

namespace Velvet
{
    internal static class FiberOutletScope
    {
        // The scope is the route's, not the render's: a factory that throws leaves the route to render
        // without one rather than taking the reconcile with it, which is what the disposal beside every
        // caller already does. Contained rather than swallowed -- PropagateException reaches the nearest
        // error boundary, so an application that wants the route refused still gets to refuse it.
        internal static IRouteScope? CreateOutletScope(ReconcilerContext ctx, RouteDefinition? route,
                                                       VisualElement container)
        {
            var scopeFactory = Router.Current?.ScopeFactory;
            if (scopeFactory == null)
            {
                return null;
            }
            try
            {
                var scope = scopeFactory.CreateScope(route, null);
                ctx.OutletScopes[container] = scope;
                return scope;
            }
            catch (System.Exception exception)
            {
                ReconcilerContext.ContainUserCallbackFailure(ctx.FiberStack.Current, exception);
                return null;
            }
        }

    }
}
