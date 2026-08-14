using System.Collections.Generic;

namespace Velvet
{
    public enum RouterStatus
    {
        Idle,
        Matching,
        Loading,
        Ready,
        NotFound,
        Error,
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
        /// <summary>The committed path, including its query string.</summary>
        public string? Path { get; init; }
        /// <summary>Path parameters captured across the full matched branch.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; } = null!;
        /// <summary>Hierarchical list of matched routes (parent first).</summary>
        public IReadOnlyList<RouteMatch>? Matches { get; init; }
    }

    /// <summary><c>Hooks.UseNavigation</c> reports these phases.</summary>
    /// <remarks>
    /// <c>submitting</c> is intentionally absent: Velvet has no route action / form-submission model,
    /// so the only in-flight phase is a location transition.
    /// </remarks>
    public enum NavigationLifecycle
    {
        Idle,
        Loading,
    }

    /// <summary>
    /// Snapshot of the active navigation exposed by <c>Hooks.UseNavigation</c>, restricted to
    /// <see cref="NavigationLifecycle.Idle"/> / <see cref="NavigationLifecycle.Loading"/>.
    /// </summary>
    public readonly struct NavigationState
    {
        public NavigationLifecycle State { get; init; }
        /// <summary>
        /// While <see cref="State"/> is <see cref="NavigationLifecycle.Loading"/>, the location being
        /// navigated to; while it is <see cref="NavigationLifecycle.Idle"/>, the committed location (null
        /// before the first navigation). Branch on <see cref="State"/> rather than on this being null: the
        /// routing guide states where the idle half sits relative to React Router's
        /// <c>navigation.location</c>.
        /// </summary>
        public RouterLocation? Location { get; init; }
    }
}
