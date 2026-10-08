using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Retries a failed async operation: TanStack Query's <c>retry</c> / <c>retryDelay</c> pair as a value an
    /// operation opts into. <see cref="MutationOptions{TVariables, TData}.Retry"/> hands one to a mutation;
    /// <see cref="RunAsync{T}"/> wraps another operation, such as a
    /// <see cref="Hooks.Use{T}(Func{CancellationToken, VelvetTask{T}}, object)"/> loader.
    /// </summary>
    /// <remarks>
    /// After the operation fails, <see cref="RetryDelay"/> is asked for the wait and then <see cref="Retry"/>
    /// whether to retry, as TanStack's retryer asks them, and an accepted failure is retried once the wait is
    /// over and <see cref="NetworkMode"/> allows. The last failure is the one the caller receives. Once the token
    /// passed to <see cref="RunAsync{T}"/> is cancelled no further attempt starts: a failure then propagates as it
    /// is, and a cancellation during a wait rejects as cancelled. An <see cref="OperationCanceledException"/>
    /// the operation throws while the token is not cancelled is a failure like any other.
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
        /// The wait before the next attempt, as TanStack's <c>retryDelay</c> option: a constant, or a function of
        /// the number of failures before this one and this failure's exception. Defaults to one second doubled at
        /// each retry, capped at thirty seconds.
        /// </summary>
        public RetryDelayRule RetryDelay { get; init; }

        /// <summary>
        /// Whether going offline pauses the operation, as TanStack's <c>networkMode</c> option. Defaults to
        /// <see cref="Velvet.NetworkMode.Online"/>.
        /// </summary>
        public NetworkMode NetworkMode { get; init; }

        /// <summary>
        /// Reads whether the device is online, for <see cref="NetworkMode"/>. When null,
        /// <c>Application.internetReachability</c> is not <c>NotReachable</c>, which is read on the main thread.
        /// </summary>
        public Func<bool>? IsOnline { get; init; }

        /// <summary>
        /// Reads whether the application has focus. A retry that is due while it has none waits until it has,
        /// as TanStack's retryer waits on its focus manager, in every <see cref="NetworkMode"/>. When null,
        /// <c>Application.isFocused</c>, which is read on the main thread.
        /// </summary>
        public Func<bool>? IsFocused { get; init; }

        /// <summary>
        /// Waits out a <see cref="RetryDelay"/>, and is handed the token passed to <see cref="RunAsync{T}"/>.
        /// When null, wall-clock time measured by <see cref="Stopwatch"/> on the main thread, checked once per
        /// frame and for at least one frame, which <c>Time.timeScale</c> does not slow. Supply one to wait on
        /// game time.
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
                static (run, token) => run(token), operation, default, cancellationToken);
        }

        internal async VelvetTask<T> RunWithStateAsync<TState, T>(
            Func<TState, CancellationToken, VelvetTask<T>> operation,
            TState state,
            RetryCallbacks<TState> callbacks,
            CancellationToken cancellationToken)
        {
            if (NetworkMode == NetworkMode.Online)
            {
                if (IsOnline == null) await VelvetTask.SwitchToMainThread();
                if (!(IsOnline?.Invoke() ?? ApplicationIsOnline()))
                {
                    await PauseAsync(state, callbacks, cancellationToken);
                }
            }

            for (var failureCount = 0; ; failureCount++)
            {
                TimeSpan delay;
                try
                {
                    return await operation(state, cancellationToken);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested)
                {
                    delay = RetryDelay.Resolve(failureCount, error);
                    if (!Retry.Allows(failureCount, error))
                    {
                        throw;
                    }

                    callbacks.OnFail?.Invoke(state, error);
                }

                await (Wait ?? s_waitRealtime)(delay, cancellationToken);
                // A Wait that ignores the token still starts no attempt for a caller who has abandoned it.
                cancellationToken.ThrowIfCancellationRequested();
                if (IsOnline == null || IsFocused == null) await VelvetTask.SwitchToMainThread();
                if (!CanContinue())
                {
                    await PauseAsync(state, callbacks, cancellationToken);
                }
            }
        }

        internal static TimeSpan DefaultRetryDelay(int failureCount) =>
            TimeSpan.FromMilliseconds(Math.Min(1000d * Math.Pow(2d, failureCount), 30000d));

        private static bool ApplicationIsOnline() => Application.internetReachability != NetworkReachability.NotReachable;

        private bool CanContinue() =>
            (IsFocused?.Invoke() ?? Application.isFocused)
            && (NetworkMode == NetworkMode.Always || (IsOnline?.Invoke() ?? ApplicationIsOnline()));

        // Polled once per frame rather than woken by Application.focusChanged, so one loop serves a supplied
        // IsOnline / IsFocused as well, which have no event to subscribe to.
        private async VelvetTask PauseAsync<TState>(
            TState state,
            RetryCallbacks<TState> callbacks,
            CancellationToken cancellationToken)
        {
            callbacks.OnPause?.Invoke(state);
            await VelvetTask.SwitchToMainThread();
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                await VelvetTask.Yield();
            }
            while (!CanContinue());
            cancellationToken.ThrowIfCancellationRequested();
            callbacks.OnContinue?.Invoke(state);
        }

        private static async VelvetTask WaitRealtime(TimeSpan delay, CancellationToken cancellationToken)
        {
            // Off the main thread a yield resumes as Task.Yield does (OffMainThreadYieldVelvetTaskSource)
            // rather than at the next frame, so polling there would spin.
            await VelvetTask.SwitchToMainThread();
            var start = Stopwatch.GetTimestamp();
            // At least one frame, a zero delay included, as v5 sleeps through a timer even for zero: a retry
            // that started on the failing attempt's own stack would let a synchronously failing operation
            // under a large retry count hold the main thread for every attempt.
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
    /// TanStack's <c>networkMode</c> option: whether a lost connection pauses an operation.
    /// </summary>
    public enum NetworkMode
    {
        /// <summary>
        /// Waits for a connection before the first attempt and before each retry. TanStack's default.
        /// </summary>
        Online,
        /// <summary>Never waits for a connection.</summary>
        Always,
        /// <summary>
        /// Makes the first attempt whether or not a connection exists, and waits for one before each retry.
        /// </summary>
        OfflineFirst,
    }

    /// <summary>
    /// What a run reports to its caller as it goes, in TanStack's retryer terms: <c>onFail</c>, <c>onPause</c>
    /// and <c>onContinue</c>.
    /// </summary>
    internal readonly struct RetryCallbacks<TState>
    {
        public Action<TState, Exception>? OnFail { get; init; }
        public Action<TState>? OnPause { get; init; }
        public Action<TState>? OnContinue { get; init; }
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

    /// <summary>
    /// TanStack's <c>retryDelay</c> option as a value: a constant wait, or a function of the failure. A
    /// <see cref="TimeSpan"/> converts implicitly, and <see cref="By"/> takes the function. The default value
    /// is the default schedule: one second doubled at each retry, capped at thirty seconds.
    /// </summary>
    public readonly struct RetryDelayRule
    {
        private readonly TimeSpan _constant;
        private readonly bool _isConstant;
        private readonly Func<int, Exception, TimeSpan>? _compute;

        private RetryDelayRule(TimeSpan constant, bool isConstant, Func<int, Exception, TimeSpan>? compute)
        {
            _constant = constant;
            _isConstant = isConstant;
            _compute = compute;
        }

        /// <summary>Waits <paramref name="delay"/> before every retry.</summary>
        public static RetryDelayRule Constant(TimeSpan delay) => new(delay, true, null);

        /// <summary>
        /// Waits what <paramref name="compute"/> returns for the number of failures before this one and this
        /// failure's exception.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="compute"/> is null.</exception>
        public static RetryDelayRule By(Func<int, Exception, TimeSpan> compute) =>
            new(default, false, compute ?? throw new ArgumentNullException(nameof(compute)));

        /// <summary>As <see cref="Constant"/>.</summary>
        public static implicit operator RetryDelayRule(TimeSpan delay) => Constant(delay);

        internal TimeSpan Resolve(int failureCount, Exception error) =>
            _compute != null ? _compute(failureCount, error)
            : _isConstant ? _constant
            : RetryPolicy.DefaultRetryDelay(failureCount);
    }
}
