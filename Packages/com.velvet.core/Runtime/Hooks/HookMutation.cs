#nullable enable annotations
using System;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// Lifecycle status of a mutation.
    /// </summary>
    public enum MutationStatus
    {
        /// <summary>No mutation has run yet (or it was reset).</summary>
        Idle,
        /// <summary>A mutation is in flight.</summary>
        Pending,
        /// <summary>The last mutation completed successfully.</summary>
        Success,
        /// <summary>The last mutation threw.</summary>
        Error,
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseMutation{TVariables, TData}"/>. The <see cref="MutationFn"/>
    /// is the async function invoked by <see cref="MutationResult{TVariables, TData}.Mutate"/> /
    /// <see cref="MutationResult{TVariables, TData}.MutateAsync"/>. For a value carried from before the call to
    /// its callbacks, use <see cref="MutationOptions{TVariables, TData, TContext}"/>.
    /// </summary>
    public sealed record MutationOptions<TVariables, TData>(
        Func<TVariables, CancellationToken, VelvetTask<TData>> MutationFn,
        Action<TData, TVariables>? OnSuccess = null,
        Action<Exception, TVariables>? OnError = null)
    {
        /// <summary>
        /// Runs after <see cref="OnSuccess"/> or <see cref="OnError"/>, with the data and a null exception on
        /// success and default data and the exception on failure.
        /// </summary>
        public Action<TData?, Exception?, TVariables>? OnSettled { get; init; }

        /// <summary>
        /// Retries a failed <see cref="MutationFn"/> within the same call: <see cref="MutationResult{TVariables, TData}.Status"/>,
        /// <see cref="MutationResult{TVariables, TData}.Data"/> and <see cref="MutationResult{TVariables, TData}.Error"/>
        /// stay uncommitted between attempts while <see cref="MutationResult{TVariables, TData}.FailureCount"/>,
        /// <see cref="MutationResult{TVariables, TData}.FailureReason"/> and
        /// <see cref="MutationResult{TVariables, TData}.IsPaused"/> follow them, and <see cref="OnSuccess"/> /
        /// <see cref="OnError"/> / <see cref="OnSettled"/> run once, for the last attempt's outcome. When null,
        /// the default, a failure is not retried, as TanStack Query's mutations default to <c>retry: 0</c>.
        /// </summary>
        public RetryPolicy? Retry { get; init; }
    }

    /// <summary>
    /// Options passed to <see cref="Hooks.UseMutation{TVariables, TData, TContext}"/>: TanStack Query's
    /// <c>onMutate</c> / <c>onSuccess</c> / <c>onError</c> / <c>onSettled</c> quartet. <see cref="OnMutate"/> runs
    /// before <see cref="MutationFn"/>, and what it returns is the context each later callback of that call
    /// receives — the place to snapshot and write an optimistic value, roll it back in <see cref="OnError"/>,
    /// and finish in <see cref="OnSettled"/>. Where <see cref="OnMutate"/> throws, or the options declare none,
    /// the callbacks receive a default context.
    /// </summary>
    public sealed record MutationOptions<TVariables, TData, TContext>(
        Func<TVariables, CancellationToken, VelvetTask<TData>> MutationFn,
        Func<TVariables, TContext>? OnMutate = null,
        Action<TData, TVariables, TContext?>? OnSuccess = null,
        Action<Exception, TVariables, TContext?>? OnError = null,
        Action<TData?, Exception?, TVariables, TContext?>? OnSettled = null)
    {
        /// <summary>As <see cref="MutationOptions{TVariables, TData}.Retry"/>.</summary>
        public RetryPolicy? Retry { get; init; }
    }

    /// <summary>
    /// Options for a void mutation that takes <typeparamref name="TVariables"/> input but returns no data.
    /// Use this overload when the mutation is fire-and-forget (typical for Store actions that update state internally).
    /// </summary>
    public sealed record MutationOptions<TVariables>(
        Func<TVariables, CancellationToken, VelvetTask> MutationFn,
        Action<TVariables>? OnSuccess = null,
        Action<Exception, TVariables>? OnError = null)
    {
        /// <summary>
        /// Runs after <see cref="OnSuccess"/> or <see cref="OnError"/>, with the exception on failure and null on
        /// success.
        /// </summary>
        public Action<Exception?, TVariables>? OnSettled { get; init; }

        /// <summary>As <see cref="MutationOptions{TVariables, TData}.Retry"/>.</summary>
        public RetryPolicy? Retry { get; init; }
    }

    /// <summary>
    /// Options for a void mutation that takes no input and returns no data. Common for "save current state" /
    /// "logout" / "reset" actions where everything is captured in closure.
    /// </summary>
    public sealed record MutationOptions(
        Func<CancellationToken, VelvetTask> MutationFn,
        Action? OnSuccess = null,
        Action<Exception>? OnError = null)
    {
        /// <summary>
        /// Runs after <see cref="OnSuccess"/> or <see cref="OnError"/>, with the exception on failure and null on
        /// success.
        /// </summary>
        public Action<Exception?>? OnSettled { get; init; }

        /// <summary>As <see cref="MutationOptions{TVariables, TData}.Retry"/>.</summary>
        public RetryPolicy? Retry { get; init; }
    }

    /// <summary>
    /// Callbacks passed to one <see cref="MutationResult{TVariables, TData}.Mutate(TVariables, MutateOptions{TVariables, TData})"/>
    /// or <see cref="MutationResult{TVariables, TData}.MutateAsync(TVariables, MutateOptions{TVariables, TData})"/>
    /// call: TanStack Query's <c>mutate(variables, { onSuccess, onError, onSettled })</c>. They run after the
    /// hook options' own callbacks, once the call's outcome is on the handle, and only while the component is
    /// mounted and the call is still the one the handle follows: the component unmounting, a newer call
    /// starting and <see cref="MutationResult{TVariables, TData}.Reset"/> each drop them. Every context
    /// parameter is the call's <c>OnMutate</c> result boxed, and null for the context-free option records.
    /// </summary>
    public sealed record MutateOptions<TVariables, TData>
    {
        /// <summary>Runs when the call succeeds.</summary>
        public Action<TData, TVariables, object?>? OnSuccess { get; init; }

        /// <summary>Runs when the call fails.</summary>
        public Action<Exception, TVariables, object?>? OnError { get; init; }

        /// <summary>
        /// Runs after <see cref="OnSuccess"/> or <see cref="OnError"/>, with the data and a null exception on
        /// success and default data and the exception on failure.
        /// </summary>
        public Action<TData?, Exception?, TVariables, object?>? OnSettled { get; init; }

        // Contained one by one, as TanStack's observer contains them: a throwing callback is reported and
        // costs neither the next callback nor the call's outcome.
        internal void Deliver(TData? data, Exception? error, TVariables variables, object? context)
        {
            try
            {
                if (error == null) OnSuccess?.Invoke(data!, variables, context);
                else OnError?.Invoke(error, variables, context);
            }
            catch (Exception callbackFailure)
            {
                VelvetTask.FromException(callbackFailure).Forget();
            }
            try
            {
                OnSettled?.Invoke(data, error, variables, context);
            }
            catch (Exception callbackFailure)
            {
                VelvetTask.FromException(callbackFailure).Forget();
            }
        }
    }

    /// <summary>
    /// Mutation handle returned by <see cref="Hooks.UseMutation{TVariables, TData}"/>. Exposes
    /// <see cref="Status"/> flags + <see cref="Data"/> / <see cref="Error"/>
    /// / <see cref="Variables"/> snapshots + <see cref="Mutate"/> / <see cref="MutateAsync"/> / <see cref="Reset"/>
    /// imperative API.
    /// </summary>
    public sealed class MutationResult<TVariables, TData>
    {
        /// <summary>Current lifecycle status of the mutation.</summary>
        public MutationStatus Status { get; private set; } = MutationStatus.Idle;
        /// <summary>True when no mutation has run yet (or it was reset).</summary>
        public bool IsIdle => Status == MutationStatus.Idle;
        /// <summary>True while a mutation is in flight.</summary>
        public bool IsPending => Status == MutationStatus.Pending;
        /// <summary>True when the last mutation completed successfully.</summary>
        public bool IsSuccess => Status == MutationStatus.Success;
        /// <summary>True when the last mutation threw.</summary>
        public bool IsError => Status == MutationStatus.Error;
        /// <summary>Result of the most recent call, and default under every status but
        /// <see cref="MutationStatus.Success"/>: starting a call clears it, so a pending call never shows an
        /// earlier call's result, and a call that fails — its <c>OnSuccess</c> throwing included — leaves
        /// none behind.</summary>
        public TData? Data { get; private set; }
        /// <summary>Exception from the last failed mutation, and null under every other status.</summary>
        public Exception? Error { get; private set; }
        /// <summary>Variables passed to the most recent mutation invocation, or default.</summary>
        public TVariables? Variables { get; private set; }
        /// <summary>
        /// How many attempts of the most recent call have failed, as TanStack's <c>failureCount</c>: zero when a
        /// call starts or succeeds, one more for each failed attempt a <see cref="MutationOptions{TVariables, TData}.Retry"/>
        /// retries, and one more again when the call fails for good.
        /// </summary>
        public int FailureCount { get; private set; }
        /// <summary>
        /// The exception of the most recent failed attempt of the call, as TanStack's <c>failureReason</c>: null
        /// when a call starts or succeeds, and still the last attempt's failure while the call waits to retry.
        /// </summary>
        public Exception? FailureReason { get; private set; }
        /// <summary>
        /// True while the call waits for a connection or for the application to regain focus, as TanStack's
        /// <c>isPaused</c>. See <see cref="RetryPolicy.NetworkMode"/>.
        /// </summary>
        public bool IsPaused { get; private set; }

        // Each of these writes a whole outcome rather than the field it is named for: Data belongs to
        // a call that succeeded and Error to one that failed, so no sequence of them leaves either
        // standing under a status that disowns it. The setters are private to keep the writing here,
        // and MutationStateTransitionTests holds that reach and this invariant together, so a
        // transition added later answers for both without being enumerated anywhere.
        internal void MarkPending(TVariables variables)
        {
            Status = MutationStatus.Pending;
            Variables = variables;
            Error = null;
            FailureCount = 0;
            FailureReason = null;
            IsPaused = false;
            // The handle follows the newest call, and the previous call's result reads as this one's
            // while this one is still pending.
            Data = default;
        }

        internal void MarkSuccess(TData data)
        {
            Data = data;
            Error = null;
            FailureCount = 0;
            FailureReason = null;
            IsPaused = false;
            Status = MutationStatus.Success;
        }

        internal void MarkFailed(Exception error)
        {
            Data = default;
            Error = error;
            FailureCount++;
            FailureReason = error;
            IsPaused = false;
            Status = MutationStatus.Error;
        }

        // The count is the handle's own, one per attempt that failed under a call that still owns it: a
        // pending call starts it at zero, so a call that lost the handle to a newer one never reaches here.
        internal void MarkRetrying(Exception error)
        {
            FailureCount++;
            FailureReason = error;
        }

        internal void MarkPaused() => IsPaused = true;

        internal void MarkContinued() => IsPaused = false;

        internal void MarkIdle()
        {
            Status = MutationStatus.Idle;
            Data = default;
            Error = null;
            Variables = default;
            FailureCount = 0;
            FailureReason = null;
            IsPaused = false;
        }

        internal Action<TVariables, MutateOptions<TVariables, TData>?>? MutateAction;
        internal Func<TVariables, MutateOptions<TVariables, TData>?, VelvetTask<TData>>? MutateAsyncFunc;
        internal Action? ResetAction;

        /// <summary>
        /// Fire-and-forget mutation that does not return a task.
        /// </summary>
        public void Mutate(TVariables variables) => MutateAction?.Invoke(variables, null);

        /// <summary>
        /// Fire-and-forget mutation whose own <paramref name="options"/> callbacks run after the hook options'.
        /// </summary>
        public void Mutate(TVariables variables, MutateOptions<TVariables, TData>? options) =>
            MutateAction?.Invoke(variables, options);

        /// <summary>
        /// Awaitable mutation. Rethrows the underlying exception on failure so callers can <c>try</c> /
        /// <c>catch</c>; <see cref="Error"/> is also populated. Rejects with an
        /// <see cref="OperationCanceledException"/> when the component unmounts while the call is in flight and
        /// the mutation function honours its token, and when the call is made after the unmount; a function that
        /// ignores the token completes with its own result.
        /// </summary>
        public VelvetTask<TData> MutateAsync(TVariables variables) => MutateAsync(variables, null);

        /// <summary>
        /// Awaitable mutation whose own <paramref name="options"/> callbacks run after the hook options'.
        /// Settles as <see cref="MutateAsync(TVariables)"/> does.
        /// </summary>
        public VelvetTask<TData> MutateAsync(TVariables variables, MutateOptions<TVariables, TData>? options) =>
            MutateAsyncFunc?.Invoke(variables, options) ?? VelvetTask.FromResult(default(TData)!);

        /// <summary>
        /// Resets status to <see cref="MutationStatus.Idle"/> and clears <see cref="Data"/> / <see cref="Error"/> /
        /// <see cref="Variables"/>. In-flight mutations are not cancelled by Reset, and they no longer write
        /// this handle: a call reset out of runs to completion and delivers its own <c>OnSuccess</c> /
        /// <c>OnError</c> / <c>OnSettled</c>, but its outcome is not the one the handle shows.
        /// </summary>
        public void Reset() => ResetAction?.Invoke();
    }

    /// <summary>
    /// Convenience extensions for mutations that take <see cref="Unit"/> as input.
    /// Allows callers to omit the explicit <c>Unit.Default</c> argument: <c>mutation.Mutate()</c> instead of
    /// <c>mutation.Mutate(Unit.Default)</c>.
    /// </summary>
    public static class MutationResultExtensions
    {
        /// <summary>Fire-and-forget mutation with no input. Shorthand for <c>Mutate(Unit.Default)</c>.</summary>
        public static void Mutate<TData>(this MutationResult<Unit, TData> result) =>
            result.Mutate(Unit.Default);

        /// <summary>Awaitable mutation with no input. Shorthand for <c>MutateAsync(Unit.Default)</c>.</summary>
        public static VelvetTask<TData> MutateAsync<TData>(this MutationResult<Unit, TData> result) =>
            result.MutateAsync(Unit.Default);

        /// <summary>Fire-and-forget mutation with no input and per-call callbacks.</summary>
        public static void Mutate<TData>(
            this MutationResult<Unit, TData> result, MutateOptions<Unit, TData>? options) =>
            result.Mutate(Unit.Default, options);

        /// <summary>Awaitable mutation with no input and per-call callbacks.</summary>
        public static VelvetTask<TData> MutateAsync<TData>(
            this MutationResult<Unit, TData> result, MutateOptions<Unit, TData>? options) =>
            result.MutateAsync(Unit.Default, options);
    }
}
