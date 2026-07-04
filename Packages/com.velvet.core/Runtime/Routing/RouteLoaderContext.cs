using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Context passed to Guard / Loader functions.
    /// Provides path parameters and the resolved path of the matched route.
    /// </summary>
    public sealed class RouteLoaderContext
    {
        /// <summary>Path parameters extracted from this route segment.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>Matched path segment string (same value as <see cref="RouteMatch.MatchedPath"/>).</summary>
        public string? Path { get; init; }
    }
}
