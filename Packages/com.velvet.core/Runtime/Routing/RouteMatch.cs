using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// One level in a parent-first route matching result.
    /// </summary>
    public sealed class RouteMatch
    {
        public RouteDefinition? Route { get; init; }
        /// <summary>Parameters captured across the full matched branch, shared by every level. A <c>:id</c>
        /// segment keys its capture as <c>id</c>, and a splat as <c>*</c>.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>The route's pattern without surrounding slashes; root remains <c>/</c>.</summary>
        public string? MatchedPath { get; init; }
        /// <summary>
        /// Cumulative rooted pathname used for route-relative navigation. A <c>..</c> removes this route
        /// level's contribution, which may span multiple URL segments. Defaults to <c>/</c> for hand-built
        /// matches.
        /// </summary>
        public string PathnameBase { get; init; } = "/";
        /// <summary>
        /// Pattern identifier used as the key for loader data and errors. Index routes are disambiguated
        /// from their parent.
        /// </summary>
        public string? RouteId { get; init; }
    }
}
