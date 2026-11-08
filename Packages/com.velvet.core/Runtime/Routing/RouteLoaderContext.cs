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

        /// <summary>
        /// The navigation as a request, React Router's <c>request</c>: a <c>GET</c> whose
        /// <see cref="RouteRequest.Url"/> carries the location's path and query, so a loader reads the search from
        /// it.
        /// </summary>
        public RouteRequest Request
        {
            // Built at the first read, so a loader that never reads it serializes no URL.
            get => _request ??= new RouteRequest(RequestPath);
            init => _request = value;
        }

        private RouteRequest? _request;

        // The path and query Request is built from.
        internal string RequestPath { get; init; } = "/";
    }
}
