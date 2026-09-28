using System;

namespace Velvet
{
    public enum RouteBlockerStatus
    {
        Idle,
        Blocked,
        /// <summary>
        /// <see cref="RouteBlockerState.Proceed"/> has released the block and the navigation it held is on
        /// its way. <see cref="RouteBlockerState.Location"/> still reports that navigation's destination, and
        /// this Blocker is not consulted until a navigation commits or none is left under way.
        /// </summary>
        Proceeding,
    }

    /// <summary>Observable state for an individual navigation Blocker: React Router's <c>Blocker</c>.</summary>
    public sealed class RouteBlockerState
    {
        public RouteBlockerStatus Status { get; private set; } = RouteBlockerStatus.Idle;

        /// <summary>
        /// Where the navigation this Blocker holds, or released, is heading; null when
        /// <see cref="RouteBlockerStatus.Idle"/>.
        /// </summary>
        public RouterLocation? Location { get; private set; }

        /// <summary>
        /// Releases the block and sends the navigation it holds through again, without consulting this
        /// Blocker. Null unless <see cref="Status"/> is <see cref="RouteBlockerStatus.Blocked"/>. The delegate
        /// is bound to the navigation it was handed out for: invoked once the Blocker has left that block it
        /// throws <see cref="InvalidOperationException"/>, and invoked while the Blocker holds a newer
        /// navigation it releases the one it was handed out for.
        /// </summary>
        public Action? Proceed { get; private set; }

        /// <summary>
        /// Releases the block and abandons the navigation, leaving the router where it is. Null unless
        /// <see cref="Status"/> is <see cref="RouteBlockerStatus.Blocked"/>; a delegate kept from then returns
        /// the Blocker to <see cref="RouteBlockerStatus.Idle"/> whatever its status is by the time it runs.
        /// </summary>
        public Action? Reset { get; private set; }

        // Raised after every change, so UseBlocker can re-render the component reading this state.
        internal Action? StateChanged;

        internal void Block(RouterLocation location, Action resume)
        {
            void ProceedWithThisNavigation()
            {
                if (Status != RouteBlockerStatus.Blocked)
                {
                    throw new InvalidOperationException(
                        $"Invalid blocker state transition: {Status} -> {RouteBlockerStatus.Proceeding}");
                }

                // Set before the re-issue rather than after: the navigation it starts consults this Blocker,
                // and this status is what tells that consultation to pass it over.
                Set(RouteBlockerStatus.Proceeding, location, null, null);
                resume();
            }

            Set(RouteBlockerStatus.Blocked, location, ProceedWithThisNavigation, ToIdle);
        }

        internal void ToIdle() => Set(RouteBlockerStatus.Idle, null, null, null);

        private void Set(RouteBlockerStatus status, RouterLocation? location, Action? proceed, Action? reset)
        {
            Status = status;
            Location = location;
            Proceed = proceed;
            Reset = reset;
            StateChanged?.Invoke();
        }
    }
}
