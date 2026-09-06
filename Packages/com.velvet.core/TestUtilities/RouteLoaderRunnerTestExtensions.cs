using System.Collections.Generic;
using System.Threading;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Sync wrapper for <see cref="RouteLoaderRunner"/>'s async API, for tests and benchmarks where awaiting
    /// is unergonomic.
    /// </summary>
    internal static class RouteLoaderRunnerTestExtensions
    {
        internal static RouteLoaderRunner.LoaderRound RunLoadersSync(
            this RouteLoaderRunner runner, IReadOnlyList<RouteMatch> matches, CancellationToken cancellationToken)
            => runner.RunLoadersAsync(matches, cancellationToken).GetAwaiter().GetResult();
    }
}
