#nullable enable
using System;

namespace Velvet
{
    internal sealed class RouteBlockerSlot : HookBlockerSlot
    {
        public IDisposable? Registration { get; set; }
        public RouteBlockerState State { get; init; } = null!;
        // The router the registration is held against: a render under a different router registers anew
        // whatever the deps say.
        public Router? LastRouter { get; set; }
        public Router? NextRouter { get; set; }
        // The predicate the settle hands the registration; null when the committed one stands.
        public Func<BlockerFunctionArgs, bool>? NextPredicate { get; set; }

        public override void Commit()
        {
            var predicate = NextPredicate;
            if (predicate != null)
            {
                if (Registration != null && ReferenceEquals(LastRouter, NextRouter))
                {
                    // Swapped in place rather than re-registered: the router consults the Blocker
                    // registered last, and a re-registration would move this one to the end.
                    RouteBlockerManager.UpdatePredicate(Registration, predicate);
                }
                else
                {
                    Registration?.Dispose();
                    Registration = NextRouter!.RouteBlockerManager.Register(predicate, State);
                }
            }
            LastRouter = NextRouter;
            NextPredicate = null;
        }

        public override void Dispose()
        {
            // Detached first: the disposal returns the state to Idle, and a fiber being torn down has no render
            // left to run for it.
            State.StateChanged = null;
            Registration?.Dispose();
            Registration = null;
        }
    }
}
