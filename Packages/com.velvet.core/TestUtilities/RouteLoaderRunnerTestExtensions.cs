using System.Collections.Generic;
using System.Threading;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Sync wrapper for <see cref="RouteLoaderRunner"/>'s async API, for tests and benchmarks where awaiting
    /// is unergonomic. Only valid where every <see cref="LoaderMode.Await"/> loader of the round hands back
    /// an already-completed task — a Suspend loader is launched rather than awaited, so an unresolved one is
    /// fine — because the awaiter throws on a task that has not completed. A test whose Await loader is
    /// unresolved awaits the real API.
    /// </summary>
    internal static class RouteLoaderRunnerTestExtensions
    {
        internal static RouteLoaderRunner.LoaderRound RunLoadersSync(
            this RouteLoaderRunner runner, IReadOnlyList<RouteMatch> matches, CancellationToken cancellationToken)
            => runner.RunLoadersAsync(matches, cancellationToken).GetAwaiter().GetResult();
    }
}
