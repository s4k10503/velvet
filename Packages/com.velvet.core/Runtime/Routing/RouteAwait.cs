#nullable enable
using System;
using System.Runtime.ExceptionServices;

namespace Velvet
{
    // Not beside its factory in Component/V.cs: same reason Navigate is not — this body calls hooks.
    internal static class RouteAwait
    {
        // What UseAsyncValue and UseAsyncError read: React Router's AwaitContext.
        internal static readonly ComponentContext<DeferredOutcome?> Outcome =
            ComponentContext<DeferredOutcome?>.Create(null);

        public sealed record Props(IDeferred Resolve, Func<object?, VNode?>? RenderValue, VNode? Children,
            VNode? ErrorElement);

        // React Router's AwaitErrorBoundary: one boundary around both outcomes, so a throw while rendering the
        // resolved value lands on the errorElement and keeps it there, until the Await remounts, for whatever it is
        // handed next — a pending deferred included, since the read that would suspend on it is the boundary's
        // child and a boundary showing what it caught does not render its children. A deferred that fails renders
        // the errorElement without catching. With no errorElement there is no boundary, and a failure propagates.
        [Component]
        public static VNode Render(Props p)
        {
            if (p.ErrorElement == null)
            {
                return V.Component(Settled, p);
            }

            return V.FrameworkErrorBoundary(
                error => V.Provider(Outcome, new DeferredOutcome(null, error), new VNode?[] { p.ErrorElement }),
                new VNode?[] { V.Component(Settled, p) });
        }

        // React Router's AwaitErrorBoundary.render past its error check: the deferred's outcome.
        [Component]
        private static VNode Settled(Props p)
        {
            var outcome = Hooks.Use<DeferredOutcome>(p.Resolve.OutcomeTask, p.Resolve);
            if (outcome.Error != null && p.ErrorElement == null)
            {
                ExceptionDispatchInfo.Capture(outcome.Error).Throw();
            }

            return outcome.Error != null
                ? V.Provider(Outcome, outcome, new VNode?[] { p.ErrorElement })
                : V.Provider(Outcome, outcome, new VNode?[] { V.Component(Resolved, p) });
        }

        // React Router's ResolveAwait: a component of its own, so a throw from the render function is a
        // descendant's throw the boundary above can catch.
        [Component]
        private static VNode Resolved(Props p)
        {
            var value = Hooks.UseAsyncValue<object>();
            var content = p.RenderValue != null ? p.RenderValue(value) : p.Children;
            return content ?? V.Fragment(Array.Empty<VNode>());
        }
    }
}
