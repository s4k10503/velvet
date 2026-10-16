#nullable enable
namespace Velvet
{
    /// <summary>How a relative <c>to</c> is resolved: React Router's <c>relative</c> option.</summary>
    public enum RelativeRoutingType
    {
        /// <summary>Against the route hierarchy: each <c>..</c> drops one matched route.</summary>
        Route,
        /// <summary>Against the URL: each <c>..</c> drops one segment of the current path.</summary>
        Path,
    }

    /// <summary>
    /// The per-call options of a <see cref="NavigateFunction"/>: React Router's second argument to
    /// <c>navigate</c>, less <c>flushSync</c> and <c>viewTransition</c>, which <c>routing.md</c> lists among its
    /// deviations.
    /// </summary>
    public sealed class NavigateOptions
    {
        /// <summary>Replaces the current history entry instead of pushing a new one.</summary>
        public bool Replace { get; init; }

        /// <summary>Stored on the history entry and read back from <see cref="RouterLocation.State"/>.</summary>
        public object? State { get; init; }

        /// <summary>How a relative target is resolved. Absolute targets ignore it.</summary>
        public RelativeRoutingType Relative { get; init; }

        /// <summary>Accepted for source parity with React Router and read by nothing: the router has no scroll restoration.</summary>
        public bool PreventScrollReset { get; init; }
    }

    /// <summary>
    /// The target of a <see cref="NavigateFunction"/> call: a path, or a number of history entries to move by
    /// (negative for back). A <see cref="string"/> or an <see cref="int"/> converts implicitly, so
    /// <c>navigate("/a")</c> and <c>navigate(-1)</c> both read as React Router's.
    /// </summary>
    public readonly struct NavigateTarget
    {
        internal NavigateTarget(string? to, int delta)
        {
            To = to;
            Delta = delta;
        }

        internal string? To { get; }
        internal int Delta { get; }

        public static implicit operator NavigateTarget(string to) => new(to, 0);
        public static implicit operator NavigateTarget(int delta) => new(null, delta);
    }

    /// <summary>
    /// What <c>Hooks.UseNavigate</c> returns: React Router's <c>navigate</c>. The options apply to a path
    /// target and are ignored for a delta, as React Router ignores them.
    /// </summary>
    public delegate VelvetTask<NavigationResult> NavigateFunction(NavigateTarget to, NavigateOptions? options = null);
}
