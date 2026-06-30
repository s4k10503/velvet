namespace Velvet
{
    /// <summary>
    /// Lane priority for a scheduled re-render. The lane is chosen from the scheduling context, not passed
    /// as an argument: <c>StartTransition</c> updates and <c>UseDeferredValue</c> derivations take the
    /// Transition lane, a starved Transition lane is promoted to Deferred, and every other state update
    /// takes the Normal lane. Lower numeric values indicate higher priority; a fiber's lane queue drains
    /// lowest-value-first.
    /// </summary>
    public enum FiberUpdatePriority
    {
        /// <summary>Highest priority. Drains ahead of every other lane within a fiber's lane queue.</summary>
        Urgent = 0,
        /// <summary>Default priority for state updates that are not part of a transition.</summary>
        Normal = 1,
        /// <summary>Low priority. Its flush is delayed by a fixed delay; a starved Transition lane is promoted to it.</summary>
        Deferred = 2,
        /// <summary>
        /// Lowest priority. Taken by <c>StartTransition</c> updates and <c>UseDeferredValue</c> derivations.
        /// Its flush is delayed so higher-priority lanes commit first, and it is promoted to Deferred once it
        /// has been starved for a fixed number of flushes.
        /// </summary>
        Transition = 3,
    }
}
