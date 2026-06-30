using System.Collections.Generic;

namespace Velvet
{
    /// <summary>Current processing state of the router.</summary>
    public enum RouterStatus
    {
        /// <summary>Idle. Waiting for a navigation request.</summary>
        Idle,
        /// <summary>Matching the path against route definitions.</summary>
        Matching,
        /// <summary>Loaders are running.</summary>
        Loading,
        /// <summary>Navigation completed. The current location is valid.</summary>
        Ready,
        /// <summary>No matching route was found.</summary>
        NotFound,
        /// <summary>An error occurred in a loader or in internal processing.</summary>
        Error,
    }

    /// <summary>Navigation mode that determines how entries are pushed onto the history stack.</summary>
    public enum NavigationMode
    {
        /// <summary>Push a new entry on top of the current entry.</summary>
        Push,
        /// <summary>Replace the current entry (no history entry left behind).</summary>
        Replace,
        /// <summary>Move one step back on the history stack.</summary>
        Back,
        /// <summary>Move one step forward on the history stack.</summary>
        Forward,
    }

    /// <summary>Outcome of a navigation attempt.</summary>
    public enum NavigationResult
    {
        /// <summary>Navigation succeeded.</summary>
        Success,
        /// <summary>No matching route exists.</summary>
        NotFound,
        /// <summary>An error occurred in a loader or in internal processing.</summary>
        Error,
        /// <summary>Cancelled via the cancellation token, or because a concurrent navigation was detected.</summary>
        Cancelled,
        /// <summary>Navigation was blocked by a Blocker.</summary>
        Blocked,
    }

    /// <summary>Execution mode for a route loader.</summary>
    public enum LoaderMode
    {
        /// <summary>Wait for the loader to complete before navigation finishes. The loader must complete synchronously.</summary>
        Await,
        /// <summary>Let navigation proceed and run the loader in the background. <see cref="Router.OnLocationChanged"/> is re-emitted on completion.</summary>
        Suspend,
    }

    /// <summary>Information about the current location after a navigation.</summary>
    public sealed class RouterLocation
    {
        /// <summary>Matched path string.</summary>
        public string Path { get; init; }
        /// <summary>Path parameters collected from every matched route.</summary>
        public IReadOnlyDictionary<string, string> Params { get; init; }
        /// <summary>Hierarchical list of matched routes (parent first).</summary>
        public IReadOnlyList<RouteMatch> Matches { get; init; }
    }

    /// <summary>
    /// Lifecycle phase of the active navigation, as reported by <c>UseNavigation().State</c>.
    /// </summary>
    /// <remarks>
    /// <c>submitting</c> is intentionally absent: Velvet has no route action / form-submission model,
    /// so the only in-flight phase is a location transition.
    /// </remarks>
    public enum NavigationLifecycle
    {
        /// <summary>No navigation is in flight.</summary>
        Idle,
        /// <summary>A navigation is matching or loading the next location.</summary>
        Loading,
    }

    /// <summary>
    /// Snapshot of the active navigation exposed by <c>Hooks.UseNavigation</c>, restricted to
    /// <see cref="NavigationLifecycle.Idle"/> / <see cref="NavigationLifecycle.Loading"/>.
    /// </summary>
    public readonly struct NavigationState
    {
        /// <summary>The current navigation lifecycle (idle / loading).</summary>
        public NavigationLifecycle State { get; init; }
        /// <summary>The current router location (or null before the first navigation).</summary>
        public RouterLocation Location { get; init; }
    }
}
