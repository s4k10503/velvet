namespace Velvet
{
    /// <summary>
    /// Information about a navigation attempt passed to a Blocker.
    /// Provides the basis (current path, target path, mode) for the block decision.
    /// </summary>
    public sealed class NavigationAttempt
    {
        /// <summary>Current path before the transition. Empty string on the first navigation.</summary>
        public string CurrentPath { get; init; } = "";
        /// <summary>Target path of the navigation.</summary>
        public string NextPath { get; init; } = "";
        /// <summary>Kind of navigation (Push / Replace / Back / Forward).</summary>
        public NavigationMode NavigationMode { get; init; } = NavigationMode.Push;
    }
}
