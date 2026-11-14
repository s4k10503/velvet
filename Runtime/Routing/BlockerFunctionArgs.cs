namespace Velvet
{
    /// <summary>
    /// What a navigation Blocker's predicate is asked about: React Router's <c>BlockerFunction</c> argument.
    /// </summary>
    public sealed class BlockerFunctionArgs
    {
        /// <summary>The location the router is on; null before its first navigation commits.</summary>
        public RouterLocation? CurrentLocation { get; init; }

        /// <summary>
        /// The location being navigated to, carrying its path and query string. Blockers are consulted before
        /// the path is matched, so it carries no <see cref="RouterLocation.Matches"/> and no parameters.
        /// </summary>
        public RouterLocation NextLocation { get; init; } = null!;

        /// <summary>How the navigation would change the history stack.</summary>
        public NavigationMode HistoryAction { get; init; }
    }
}
