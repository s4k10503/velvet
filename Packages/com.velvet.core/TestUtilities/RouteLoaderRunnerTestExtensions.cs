using System.Collections.Generic;
using System.Threading;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Sync wrapper for <see cref="RouteLoaderRunner"/>'s async API, for tests and benchmarks where awaiting
    /// is unergonomic. Only valid where every loader of the round hands back an already-completed task: the
    /// awaiter throws on one that has not, rather than blocking, because a frame-bound continuation cannot
    /// run while the caller holds the thread. A test whose loader is unresolved awaits the real API.
    /// </summary>
    internal static class RouteLoaderRunnerTestExtensions
    {
        internal static RouteLoaderRunner.LoaderRound RunLoadersSync(
            this RouteLoaderRunner runner, IReadOnlyList<RouteMatch> matches, CancellationToken cancellationToken)
            => runner.RunLoadersAsync(matches, cancellationToken).GetAwaiter().GetResult();
    }
}
