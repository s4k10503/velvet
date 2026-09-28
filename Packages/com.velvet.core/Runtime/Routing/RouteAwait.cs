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

        [Component]
        public static VNode Render(Props p)
        {
            var outcome = Hooks.Use<DeferredOutcome>(p.Resolve.OutcomeTask, p.Resolve);
            if (outcome.Error != null)
            {
                if (p.ErrorElement == null)
                {
                    ExceptionDispatchInfo.Capture(outcome.Error).Throw();
                }

                return V.Provider(Outcome, outcome, new VNode?[] { p.ErrorElement });
            }

            var resolved = V.Provider(Outcome, outcome, new VNode?[] { V.Component(Resolved, p) });
            if (p.ErrorElement == null)
            {
                return resolved;
            }

            // A throw while rendering the resolved value lands on the errorElement too, as React Router's
            // AwaitErrorBoundary catches it.
            return V.ErrorBoundary(
                error => V.Provider(Outcome, new DeferredOutcome(null, error), new VNode?[] { p.ErrorElement }),
                new VNode?[] { resolved });
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
