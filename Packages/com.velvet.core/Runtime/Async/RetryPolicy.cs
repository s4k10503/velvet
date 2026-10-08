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
    /// After the operation fails, it runs again when <see cref="Retry"/> accepts the failure, after waiting
    /// <see cref="RetryDelay"/>. The last failure is the one the caller receives. An
    /// <see cref="OperationCanceledException"/> is never retried, and once the token passed to
    /// <see cref="RunAsync{T}"/> is cancelled no further attempt starts: a failure then propagates as it is, and
    /// a cancellation during a wait rejects as cancelled.
    /// </remarks>
    public sealed record RetryPolicy
    {
        private static readonly Func<TimeSpan, CancellationToken, VelvetTask> s_waitRealtime = WaitRealtime;

        /// <summary>
        /// Which failures are retried, as TanStack's <c>retry</c> option. Defaults to 3 retries, TanStack Query's
        /// default for a query.
        /// </summary>
        public RetryRule Retry { get; init; } = 3;

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
                    if (!Retry.Allows(failureCount, error))
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

    /// <summary>
    /// TanStack's <c>retry</c> option as a value: a number of retries, every failure, no failure, or a function
    /// of the failure. A number or a bool converts implicitly, and <see cref="When"/> takes the function.
    /// </summary>
    /// <remarks>
    /// The default value retries nothing.
    /// </remarks>
    public readonly struct RetryRule
    {
        private readonly int _count;
        private readonly Func<int, Exception, bool>? _shouldRetry;

        private RetryRule(int count, Func<int, Exception, bool>? shouldRetry)
        {
            _count = count;
            _shouldRetry = shouldRetry;
        }

        /// <summary>
        /// Retries up to <paramref name="count"/> times after the first attempt, so an operation runs at most
        /// <c>count + 1</c> times. Zero or less makes no retry.
        /// </summary>
        public static RetryRule Times(int count) => new(count, null);

        /// <summary>
        /// Decides from the number of failures before this one and this failure's exception whether to retry.
        /// The function replaces a count rather than narrowing one, so it alone ends the retries: one that
        /// always accepts retries without end.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="shouldRetry"/> is null.</exception>
        public static RetryRule When(Func<int, Exception, bool> shouldRetry) =>
            new(0, shouldRetry ?? throw new ArgumentNullException(nameof(shouldRetry)));

        /// <summary><c>true</c> retries every failure without end, <c>false</c> none.</summary>
        public static implicit operator RetryRule(bool retry) => retry ? When(static (_, _) => true) : default;

        /// <summary>As <see cref="Times"/>.</summary>
        public static implicit operator RetryRule(int count) => Times(count);

        internal bool Allows(int failureCount, Exception error) =>
            _shouldRetry != null ? _shouldRetry(failureCount, error) : failureCount < _count;
    }
}
