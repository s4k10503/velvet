using System;
using System.Diagnostics;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Retries a failed async operation: TanStack Query's <c>retry</c> / <c>retryDelay</c> pair as a value an
    /// operation opts into. <see cref="MutationOptions{TVariables, TData}.Retry"/> hands one to a mutation;
    /// <see cref="RunAsync{T}"/> wraps another operation, such as a
    /// <see cref="Hooks.Use{T}(Func{CancellationToken, VelvetTask{T}}, object)"/> loader.
    /// </summary>
    /// <remarks>
    /// After the operation fails, it runs again while fewer than <see cref="MaxRetries"/> retries have been
    /// made and <see cref="ShouldRetry"/>, when given, accepts the failure, after waiting
    /// <see cref="RetryDelay"/>. The last failure is the one the caller receives. An
    /// <see cref="OperationCanceledException"/> is never retried, and once the token passed to
    /// <see cref="RunAsync{T}"/> is cancelled no further attempt starts: a failure then propagates as it is, and
    /// a cancellation during a wait rejects as cancelled.
    /// </remarks>
    public sealed record RetryPolicy
    {
        private static readonly Func<TimeSpan, CancellationToken, VelvetTask> s_waitRealtime = WaitRealtime;

        /// <summary>
        /// The most retries after the first attempt, so an operation runs at most <c>MaxRetries + 1</c> times.
        /// Zero or less makes no retry. Defaults to 3, TanStack Query's default for a query.
        /// </summary>
        public int MaxRetries { get; init; } = 3;

        /// <summary>
        /// Decides from the number of failures before this one and this failure's exception whether to retry,
        /// as TanStack's <c>retry</c> function does. It narrows <see cref="MaxRetries"/> rather than replacing
        /// it. When null, <see cref="MaxRetries"/> alone decides.
        /// </summary>
        public Func<int, Exception, bool>? ShouldRetry { get; init; }

        /// <summary>
        /// The wait before the next attempt, from the number of failures before this one and this failure's
        /// exception. When null, TanStack's default: one second doubled at each retry, capped at thirty seconds.
        /// </summary>
        public Func<int, Exception, TimeSpan>? RetryDelay { get; init; }

        /// <summary>
        /// Waits out a <see cref="RetryDelay"/>, and is handed the token passed to <see cref="RunAsync{T}"/>.
        /// When null, wall-clock time measured by <see cref="Stopwatch"/> on the main thread, checked once per
        /// frame and for at least one frame, which <c>Time.timeScale</c> does not slow. Supply one to wait on
        /// game time, or until the network is back.
        /// </summary>
        public Func<TimeSpan, CancellationToken, VelvetTask>? Wait { get; init; }

        /// <summary>
        /// Runs <paramref name="operation"/>, retrying it under this policy, and returns the first successful
        /// attempt's result. Each attempt receives <paramref name="cancellationToken"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is null.</exception>
        public VelvetTask<T> RunAsync<T>(
            Func<CancellationToken, VelvetTask<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            return RunWithStateAsync<Func<CancellationToken, VelvetTask<T>>, T>(
                static (run, token) => run(token), operation, cancellationToken);
        }

        internal async VelvetTask<T> RunWithStateAsync<TState, T>(
            Func<TState, CancellationToken, VelvetTask<T>> operation,
            TState state,
            CancellationToken cancellationToken)
        {
            for (var failureCount = 0; ; failureCount++)
            {
                TimeSpan delay;
                try
                {
                    return await operation(state, cancellationToken);
                }
                catch (Exception error) when (error is not OperationCanceledException
                                              && !cancellationToken.IsCancellationRequested)
                {
                    if (failureCount >= MaxRetries || ShouldRetry?.Invoke(failureCount, error) == false)
                    {
                        throw;
                    }

                    delay = RetryDelay?.Invoke(failureCount, error) ?? DefaultRetryDelay(failureCount);
                }

                await (Wait ?? s_waitRealtime)(delay, cancellationToken);
                // A Wait that ignores the token still starts no attempt for a caller who has abandoned it.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        internal static TimeSpan DefaultRetryDelay(int failureCount) =>
            TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2d, failureCount), 30000d));

        private static async VelvetTask WaitRealtime(TimeSpan delay, CancellationToken cancellationToken)
        {
            // Off the main thread a yield resumes as Task.Yield does (OffMainThreadYieldVelvetTaskSource)
            // rather than at the next frame, so polling there would spin.
            await VelvetTask.SwitchToMainThread();
            var start = Stopwatch.GetTimestamp();
            // At least one frame, a zero delay included, as v5 sleeps through a timer even for zero: a retry
            // that started on the failing attempt's own stack would let a synchronously failing operation
            // under a large MaxRetries hold the main thread for every attempt.
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                await VelvetTask.Yield();
            }
            // MUTANT_SURVIVES(equivalent): `<=` differs only where the elapsed time equals the delay exactly,
            // and there it waits one frame more, within the frame this polled wait already overshoots by.
            while (TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency) < delay);
        }
    }
}
