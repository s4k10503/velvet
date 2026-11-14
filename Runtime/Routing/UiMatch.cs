#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>One entry of <c>Hooks.UseMatches</c>: a matched route with the data it carries, as React Router's <c>UIMatch</c>.</summary>
    public sealed class UiMatch
    {
        /// <summary>The route's <see cref="RouteMatch.RouteId"/>.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>The route's <see cref="RouteMatch.PathnameBase"/>: the pathname up to and including the route.</summary>
        public string Pathname { get; init; } = "/";

        /// <summary>The parameters captured across the whole matched branch.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;

        /// <summary>The route's loader data; null when it has none.</summary>
        public object? Data { get; init; }

        /// <summary>The route's <see cref="RouteDefinition.Handle"/>.</summary>
        public object? Handle { get; init; }
    }
}
