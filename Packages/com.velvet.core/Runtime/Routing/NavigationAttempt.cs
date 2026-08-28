namespace Velvet
{
    /// <summary>Describes the transition presented to a navigation Blocker.</summary>
    public sealed class NavigationAttempt
    {
        /// <summary>Current path before the transition. Empty string on the first navigation.</summary>
        public string CurrentPath { get; init; } = "";
        public string NextPath { get; init; } = "";
        public NavigationMode NavigationMode { get; init; } = NavigationMode.Push;
    }
}
