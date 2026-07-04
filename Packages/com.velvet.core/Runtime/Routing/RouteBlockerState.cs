using System;

namespace Velvet
{
    /// <summary>Current state of a Blocker.</summary>
    public enum RouteBlockerStatus
    {
        /// <summary>Idle, not currently blocking.</summary>
        Idle,
        /// <summary>Currently blocking a navigation. <see cref="RouteBlockerState.Attempt"/> holds the attempt details.</summary>
        Blocked,
    }

    /// <summary>
    /// State object held by an individual Blocker.
    /// UI components observe this object to drive the display of a block dialog.
    /// </summary>
    public sealed class RouteBlockerState
    {
        /// <summary>Current block state.</summary>
        public RouteBlockerStatus Status { get; internal set; } = RouteBlockerStatus.Idle;
        /// <summary>Information about the navigation attempt being blocked. null when <see cref="RouteBlockerStatus.Idle"/>.</summary>
        public NavigationAttempt? Attempt { get; internal set; }

        /// <summary>Callback invoked when <see cref="Proceed"/> is called.</summary>
        internal Action? OnProceed { get; set; }

        /// <summary>Callback invoked when <see cref="Reset"/> is called.</summary>
        internal Action? OnReset { get; set; }

        /// <summary>
        /// Releases the block and signals intent to allow the transition.
        /// After calling Proceed() you must invoke NavigateAsync manually again.
        /// </summary>
        public void Proceed() => Release(OnProceed);

        /// <summary>Releases the block and signals intent to cancel the transition.</summary>
        public void Reset() => Release(OnReset);

        private void Release(Action? callback)
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            Status = RouteBlockerStatus.Idle;
            Attempt = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Resets the state without invoking callbacks (internal use before a navigation starts).
        /// </summary>
        internal void InternalReset()
        {
            Status = RouteBlockerStatus.Idle;
            Attempt = null;
        }

        internal void Block(NavigationAttempt attempt)
        {
            Status = RouteBlockerStatus.Blocked;
            Attempt = attempt;
        }
    }
}
