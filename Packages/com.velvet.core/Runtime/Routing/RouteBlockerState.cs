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
        /// <summary>
        /// <see cref="RouteBlockerState.Proceed"/> has released the block and the navigation it held is
        /// running. <see cref="RouteBlockerState.Attempt"/> still holds that attempt, and this Blocker is not
        /// consulted again until the navigation commits.
        /// </summary>
        Proceeding,
    }

    /// <summary>
    /// State object held by an individual Blocker.
    /// UI components observe this object to drive the display of a block dialog.
    /// </summary>
    public sealed class RouteBlockerState
    {
        /// <summary>Current block state.</summary>
        public RouteBlockerStatus Status { get; internal set; } = RouteBlockerStatus.Idle;
        /// <summary>
        /// Information about the navigation attempt this Blocker holds — the one it blocked, and the one
        /// <see cref="Proceed"/> resumes. null when <see cref="RouteBlockerStatus.Idle"/>.
        /// </summary>
        public NavigationAttempt? Attempt { get; internal set; }

        private Action? _resume;

        /// <summary>
        /// Releases the block and re-issues the blocked navigation, which runs without consulting this
        /// Blocker again. Does nothing unless <see cref="Status"/> is <see cref="RouteBlockerStatus.Blocked"/>.
        /// A Back or Forward attempt resumes as the same history step, not as a navigation to its path.
        /// </summary>
        public void Proceed()
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            // Proceeding must be visible before the re-issued navigation reaches the Blocker check, which it
            // does synchronously for a synchronous Blocker: that check is what reads this status to leave the
            // attempt alone, so setting it afterwards would let the navigation be blocked a second time.
            Status = RouteBlockerStatus.Proceeding;
            var resume = _resume;
            _resume = null;
            resume?.Invoke();
        }

        /// <summary>
        /// Releases the block and abandons the blocked navigation. Does nothing unless <see cref="Status"/>
        /// is <see cref="RouteBlockerStatus.Blocked"/>.
        /// </summary>
        public void Reset()
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            InternalReset();
        }

        /// <summary>
        /// Resets the state without re-issuing anything (internal use before a navigation starts, and when
        /// the navigation a <see cref="RouteBlockerStatus.Proceeding"/> Blocker released has committed).
        /// </summary>
        internal void InternalReset()
        {
            Status = RouteBlockerStatus.Idle;
            Attempt = null;
            _resume = null;
        }

        internal void Block(NavigationAttempt attempt, Action resume)
        {
            Status = RouteBlockerStatus.Blocked;
            Attempt = attempt;
            _resume = resume;
        }
    }
}
