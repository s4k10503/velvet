using System.Collections.Generic;

namespace Velvet
{
    public enum RouterStatus
    {
        Idle,
        /// <summary>The path has matched, and the matched routes' guards are running.</summary>
        Matching,
        Loading,
        Ready,
        NotFound,
        Error,
        /// <summary>A submission other than <c>get</c> has matched, and its guards or its action are running.</summary>
        Submitting,
    }

    /// <summary>Controls how a successful navigation changes history.</summary>
    public enum NavigationMode
    {
        Push,
        Replace,
        Back,
        Forward,
    }

    public enum NavigationResult
    {
        Success,
        NotFound,
        Error,
        /// <summary>Cancellation, supersession, or an unavailable history step stopped the attempt.</summary>
        Cancelled,
        Blocked,
    }

    /// <summary>Execution mode for a route loader.</summary>
    public enum LoaderMode
    {
        /// <summary>
        /// Await the loader before the navigation commits, so the route already on screen stays there until
        /// the data is in. A loader that never completes never lets the navigation commit.
        /// </summary>
        Await,
        /// <summary>Let navigation proceed and run the loader in the background. <see cref="Router.OnLocationChanged"/> is re-emitted on completion.</summary>
        Suspend,
    }

    public sealed class RouterLocation
    {
        /// <summary>The path, including its query string.</summary>
        public string? Path { get; init; }
        /// <summary>Path parameters captured across the full matched branch.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>Hierarchical list of matched routes (parent first).</summary>
        public IReadOnlyList<RouteMatch>? Matches { get; init; }
    }

    /// <summary><c>Hooks.UseNavigation</c> reports these phases: React Router's <c>navigation.state</c>.</summary>
    public enum NavigationLifecycle
    {
        Idle,
        Loading,
        /// <summary>A submission's guards or action are running.</summary>
        Submitting,
    }

    /// <summary>Snapshot of the active navigation exposed by <c>Hooks.UseNavigation</c>.</summary>
    public readonly struct NavigationState
    {
        public NavigationLifecycle State { get; init; }
        /// <summary>
        /// While <see cref="State"/> is <see cref="NavigationLifecycle.Loading"/>, the location being
        /// navigated to; null while it is <see cref="NavigationLifecycle.Idle"/>, as React Router's
        /// <c>navigation.location</c> is <c>undefined</c> then.
        /// </summary>
        public RouterLocation? Location { get; init; }

        /// <summary>
        /// The lower-case method of the submission in flight, while one is, as React Router 6.28 reports it
        /// without <c>v7_normalizeFormMethod</c>; null otherwise, as
        /// <c>navigation.formMethod</c> is <c>undefined</c> then.
        /// </summary>
        public string? FormMethod { get; init; }

        /// <summary>The path the submission in flight was made to; null while none is.</summary>
        public string? FormAction { get; init; }

        /// <summary>What the submission in flight sent; null while none is.</summary>
        public object? FormData { get; init; }
    }
}
