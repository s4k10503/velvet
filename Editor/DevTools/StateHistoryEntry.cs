using System;

namespace Velvet.Editor.DevTools
{
    /// <summary>
    /// A single state-change history entry.
    /// Stored in a 64-entry ring buffer that is populated only in debug builds.
    /// </summary>
    public sealed class StateHistoryEntry
    {
        /// <summary>Time at which the state change occurred.</summary>
        public DateTime Timestamp { get; }

        /// <summary>String representation of the post-change state via ToString().</summary>
        public string StateString { get; }

        /// <summary>Render count at this point.</summary>
        public int RenderCount { get; }

        public StateHistoryEntry(DateTime timestamp, string stateString, int renderCount)
        {
            Timestamp = timestamp;
            StateString = stateString;
            RenderCount = renderCount;
        }
    }
}
