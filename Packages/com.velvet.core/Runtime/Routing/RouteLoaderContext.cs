#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>Describes the route matched for a Guard or Loader.</summary>
    public sealed class RouteLoaderContext
    {
        /// <summary>Parameters captured across the full matched route branch.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>The matched route pattern, equal to <see cref="RouteMatch.MatchedPath"/>.</summary>
        public string? Path { get; init; }

        /// <summary>The path being navigated to, with its query string: React Router's <c>request.url</c> without its origin.</summary>
        public string Url { get; init; } = string.Empty;

        /// <summary>
        /// The query string of <see cref="Url"/>, read as React Router's loader reads
        /// <c>new URL(request.url).searchParams</c>. Parsed on first read.
        /// </summary>
        public ISearchParams SearchParams => _searchParams ??= RouteQuery.ParseQuery(Url);

        private ISearchParams? _searchParams;
    }
}
