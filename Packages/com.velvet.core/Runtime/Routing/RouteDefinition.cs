using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// A directly constructed route that combines <see cref="RedirectTo"/> and <see cref="Guard"/> throws
    /// <see cref="InvalidOperationException"/> when its redirect configuration is evaluated during navigation.
    /// </summary>
    public sealed class RouteDefinition
    {
        /// <summary>
        /// Route pattern matched against the URL, e.g. <c>"users/:id"</c> (<c>"/"</c> for the root,
        /// <c>""</c> for an index route). Supports <c>:param</c>, optional <c>:param?</c>, and splat <c>*</c>.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>
        /// Function-type component to mount when this route matches.
        /// Pass a <see cref="ComponentNode"/> built via V.Component().
        /// </summary>
        public ComponentNode? Element { get; init; }

        /// <summary>Identifier for the per-route DI scope created via <see cref="IRouteScopeFactory"/>; null for no scope.</summary>
        public string? ScopeId { get; init; }

        /// <summary>Async data loader run when this route matches; its result is read via <c>UseLoaderData</c>. See <see cref="LoaderMode"/>.</summary>
        public Func<RouteLoaderContext, CancellationToken, UniTask<object>>? Loader { get; init; }

        /// <summary>How <see cref="Loader"/> is sequenced relative to the navigation commit. Defaults to <see cref="LoaderMode.Await"/>.</summary>
        public LoaderMode LoaderMode { get; init; } = LoaderMode.Await;

        /// <summary>
        /// Component shown when the Loader fails.
        /// </summary>
        public ComponentNode? ErrorElement { get; init; }

        /// <summary>Nested child routes rendered into this route's <c>Outlet</c>.</summary>
        public RouteDefinition[]? Children { get; init; }

        /// <summary>
        /// Static redirect target without parameter interpolation. Use <see cref="Guard"/> to derive a target
        /// from matched parameters.
        /// </summary>
        public string? RedirectTo { get; init; }

        /// <summary>
        /// Returning null continues navigation; returning a path redirects. Runs after matching and before
        /// Blockers and Loaders.
        /// </summary>
        public Func<RouteLoaderContext, string>? Guard { get; init; }

        /// <summary>
        /// Whether this route's literal path segments match case-sensitively. Defaults to false
        /// (case-insensitive). Set true for a case-sensitive route.
        /// </summary>
        public bool CaseSensitive { get; init; } = false;
    }
}
