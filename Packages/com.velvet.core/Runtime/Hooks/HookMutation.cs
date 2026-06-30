#nullable enable annotations
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

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
    /// <see cref="MutationResult{TVariables, TData}.MutateAsync"/>.
    /// </summary>
    public sealed record MutationOptions<TVariables, TData>(
        Func<TVariables, CancellationToken, UniTask<TData>> MutationFn,
        Action<TData, TVariables>? OnSuccess = null,
        Action<Exception, TVariables>? OnError = null);

    /// <summary>
    /// Options for a void mutation that takes <typeparamref name="TVariables"/> input but returns no data.
    /// Use this overload when the mutation is fire-and-forget (typical for Store actions that update state internally).
    /// </summary>
    public sealed record MutationOptions<TVariables>(
        Func<TVariables, CancellationToken, UniTask> MutationFn,
        Action<TVariables>? OnSuccess = null,
        Action<Exception, TVariables>? OnError = null);

    /// <summary>
    /// Options for a void mutation that takes no input and returns no data. Common for "save current state" /
    /// "logout" / "reset" actions where everything is captured in closure.
    /// </summary>
    public sealed record MutationOptions(
        Func<CancellationToken, UniTask> MutationFn,
        Action? OnSuccess = null,
        Action<Exception>? OnError = null);

    /// <summary>
    /// Mutation handle returned by <see cref="Hooks.UseMutation{TVariables, TData}"/>. Exposes
    /// <see cref="Status"/> flags + <see cref="Data"/> / <see cref="Error"/>
    /// / <see cref="Variables"/> snapshots + <see cref="Mutate"/> / <see cref="MutateAsync"/> / <see cref="Reset"/>
    /// imperative API.
    /// </summary>
    public sealed class MutationResult<TVariables, TData>
    {
        /// <summary>Current lifecycle status of the mutation.</summary>
        public MutationStatus Status { get; internal set; } = MutationStatus.Idle;
        /// <summary>True when no mutation has run yet (or it was reset).</summary>
        public bool IsIdle => Status == MutationStatus.Idle;
        /// <summary>True while a mutation is in flight.</summary>
        public bool IsPending => Status == MutationStatus.Pending;
        /// <summary>True when the last mutation completed successfully.</summary>
        public bool IsSuccess => Status == MutationStatus.Success;
        /// <summary>True when the last mutation threw.</summary>
        public bool IsError => Status == MutationStatus.Error;
        /// <summary>Result of the last successful mutation, or default when none has succeeded.</summary>
        public TData? Data { get; internal set; }
        /// <summary>Exception from the last failed mutation, or null.</summary>
        public Exception? Error { get; internal set; }
        /// <summary>Variables passed to the most recent mutation invocation, or default.</summary>
        public TVariables? Variables { get; internal set; }

        internal Action<TVariables>? MutateAction;
        internal Func<TVariables, UniTask<TData>>? MutateAsyncFunc;
        internal Action? ResetAction;

        /// <summary>
        /// Fire-and-forget mutation that does not return a task.
        /// </summary>
        public void Mutate(TVariables variables) => MutateAction?.Invoke(variables);

        /// <summary>
        /// Awaitable mutation. Rethrows the underlying exception
        /// on failure so callers can <c>try</c> / <c>catch</c>; <see cref="Error"/> is also populated.
        /// </summary>
        public UniTask<TData> MutateAsync(TVariables variables) =>
            MutateAsyncFunc?.Invoke(variables) ?? UniTask.FromResult(default(TData)!);

        /// <summary>
        /// Resets status to <see cref="MutationStatus.Idle"/> and clears <see cref="Data"/> / <see cref="Error"/> /
        /// <see cref="Variables"/>. In-flight mutations are not cancelled by Reset.
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
        public static UniTask<TData> MutateAsync<TData>(this MutationResult<Unit, TData> result) =>
            result.MutateAsync(Unit.Default);
    }
}
