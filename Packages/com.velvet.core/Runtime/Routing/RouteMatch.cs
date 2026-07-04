using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// One entry in a route matching result. For nested routes, an entry is produced for each level.
    /// </summary>
    public sealed class RouteMatch
    {
        /// <summary>The matched route definition.</summary>
        public RouteDefinition? Route { get; init; }
        /// <summary>Path parameters extracted from this route segment (for example <c>:id</c> -&gt; <c>id</c>).</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>Matched path segment string (the route's own path, trimmed). Used for display / debug.</summary>
        public string? MatchedPath { get; init; }
        /// <summary>
        /// Resolved cumulative URL pathname from the root up to and including this route level (params
        /// substituted, always rooted with a leading <c>/</c>). Drives route-relative navigation: a
        /// <c>..</c> removes this route's entire URL contribution — which may span multiple segments for a
        /// multi-segment route pattern — rather than a single URL segment. Defaults to <c>/</c> when not
        /// computed (e.g. hand-built matches).
        /// </summary>
        public string PathnameBase { get; init; } = "/";
        /// <summary>
        /// Stable identifier for this route within the tree (the cumulative pattern path from the root,
        /// disambiguated so sibling index routes do not collide). Used as the key for loader data and
        /// errors.
        /// </summary>
        public string? RouteId { get; init; }
    }
}
