using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Awaits a rendezvous with the code under test, and fails rather than waits forever when the code
    /// under test stops arriving.
    /// </summary>
    /// <remarks>
    /// An unbounded <c>await source.Task</c> is a case that cannot report the defect it exists to find: a
    /// change that stops reaching the completion leaves the await pending, the case never returns, and the
    /// run hangs where it should have gone red. A mutation campaign then reads the wedge as a timeout,
    /// which is unmeasured rather than survived and fails the campaign.
    /// <para>
    /// The bound is generous rather than tuned. What separates a wedge from a slow rendezvous here is not
    /// a hand-picked margin — every one of these completes in the same frame the code under test reaches
    /// it, or never — so any bound above scheduling noise reports the same thing, and a large one cannot
    /// redden a healthy run on a loaded machine.
    /// </para>
    /// </remarks>
    public static class BoundedAwait
    {
        private const int DefaultSeconds = 20;

        // Built on AttachExternalCancellation rather than on a combinator against a delay: a test
        // helper is not where a combinator earns its place on the production type.
        public static async VelvetTask Bounded(this VelvetTask task, [CallerMemberName] string caller = "",
                                               [CallerLineNumber] int line = 0, int seconds = DefaultSeconds)
        {
            using var expiry = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                await task.AttachExternalCancellation(expiry.Token);
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested)
            {
                throw new TimeoutException(Wedged(caller, line, seconds));
            }
        }

        public static async VelvetTask<T> Bounded<T>(this VelvetTask<T> task,
                                                     [CallerMemberName] string caller = "",
                                                     [CallerLineNumber] int line = 0,
                                                     int seconds = DefaultSeconds)
        {
            using var expiry = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                return await task.AttachExternalCancellation(expiry.Token);
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested)
            {
                throw new TimeoutException(Wedged(caller, line, seconds));
            }
        }

        // The coroutine form, for a UnityTest driven by VelvetTask.ToCoroutine: its enumerator answers
        // MoveNext with true for as long as the task is pending, so a wedge otherwise ends only at the
        // runner's per-test timeout.
        public static IEnumerator Bounded(this IEnumerator coroutine, [CallerMemberName] string caller = "",
                                          [CallerLineNumber] int line = 0, int seconds = DefaultSeconds)
        {
            var clock = Stopwatch.StartNew();
            while (coroutine.MoveNext())
            {
                if (clock.Elapsed.TotalSeconds > seconds)
                {
                    throw new TimeoutException(Wedged(caller, line, seconds));
                }

                yield return coroutine.Current;
            }
        }

        private static string Wedged(string caller, int line, int seconds) =>
            $"{caller} (line {line}) waited {seconds}s for a completion the code under test never "
            + "reached. An await that cannot end is a case that cannot fail.";
    }
}
