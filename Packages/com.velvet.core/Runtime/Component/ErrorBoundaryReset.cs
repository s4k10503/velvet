#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// What reset an error boundary, as react-error-boundary's <c>onReset</c> details name it.
    /// </summary>
    public enum ErrorBoundaryResetReason
    {
        /// <summary>The reset action was invoked — react-error-boundary's <c>"imperative-api"</c>.</summary>
        ImperativeApi,

        /// <summary>The boundary's reset keys changed — react-error-boundary's <c>"keys"</c>.</summary>
        Keys,
    }

    /// <summary>
    /// Handed to the <c>onReset</c> of <see cref="Hooks.UseErrorBoundaryReset"/> before the boundary resets,
    /// as react-error-boundary hands its <c>onReset</c> the reset's details.
    /// </summary>
    public sealed class ErrorBoundaryResetDetails
    {
        internal ErrorBoundaryResetDetails(
            ErrorBoundaryResetReason reason,
            IReadOnlyList<object?> args,
            IReadOnlyList<object?>? prev,
            IReadOnlyList<object?>? next)
        {
            Reason = reason;
            Args = args;
            Prev = prev;
            Next = next;
        }

        /// <summary>What reset the boundary.</summary>
        public ErrorBoundaryResetReason Reason { get; }

        /// <summary>
        /// The arguments the reset action was invoked with, for <see cref="ErrorBoundaryResetReason.ImperativeApi"/>;
        /// empty for <see cref="ErrorBoundaryResetReason.Keys"/>.
        /// </summary>
        public IReadOnlyList<object?> Args { get; }

        /// <summary>
        /// The reset keys the boundary's previous render passed, for <see cref="ErrorBoundaryResetReason.Keys"/>;
        /// null for <see cref="ErrorBoundaryResetReason.ImperativeApi"/> or where that render passed none.
        /// </summary>
        public IReadOnlyList<object?>? Prev { get; }

        /// <summary>
        /// The reset keys the boundary's render passed, for <see cref="ErrorBoundaryResetReason.Keys"/>; null for
        /// <see cref="ErrorBoundaryResetReason.ImperativeApi"/> or where that render passed none.
        /// </summary>
        public IReadOnlyList<object?>? Next { get; }
    }

    /// <summary>
    /// An error boundary's reset — react-error-boundary's <c>resetErrorBoundary</c>. One instance per boundary,
    /// reference-stable for the boundary's lifetime; it converts to an <see cref="Action"/> that invokes it with
    /// no arguments, so it can be passed as an <c>onClick</c>.
    /// </summary>
    public sealed class ErrorBoundaryReset
    {
        private readonly ComponentFiber _boundary;
        private readonly Action _invokeWithoutArgs;

        internal ErrorBoundaryReset(ComponentFiber boundary)
        {
            _boundary = boundary;
            _invokeWithoutArgs = () => Invoke();
        }

        /// <summary>
        /// While the boundary shows its fallback, calls its <c>onReset</c> with
        /// <see cref="ErrorBoundaryResetReason.ImperativeApi"/> and <paramref name="args"/>, then queues the reset as a
        /// state update: the boundary's next render on the lane the call is made on renders its children again.
        /// While it shows its children, does nothing.
        /// </summary>
        /// <param name="args">Handed to <c>onReset</c> as <see cref="ErrorBoundaryResetDetails.Args"/>.</param>
        public void Invoke(params object?[] args)
        {
            if (_boundary.IsDisposed || _boundary.CaughtError == null) return;
            _boundary.OnErrorBoundaryReset?.Invoke(new ErrorBoundaryResetDetails(
                ErrorBoundaryResetReason.ImperativeApi, args ?? Array.Empty<object?>(), prev: null, next: null));
            FiberErrorBoundary.QueueReset(_boundary);
        }

        /// <summary>The reference-stable action that invokes <paramref name="reset"/> with no arguments.</summary>
        /// <param name="reset">The reset to convert.</param>
        public static implicit operator Action(ErrorBoundaryReset reset) => reset._invokeWithoutArgs;
    }

    /// <summary>
    /// What <see cref="Hooks.UseErrorBoundary"/> returns — react-error-boundary's <c>useErrorBoundary()</c> result.
    /// </summary>
    public readonly struct ErrorBoundaryApi
    {
        internal ErrorBoundaryApi(Action resetBoundary, Action<Exception> showBoundary)
        {
            ResetBoundary = resetBoundary;
            ShowBoundary = showBoundary;
        }

        /// <summary>
        /// Resets the boundary <see cref="Hooks.UseErrorBoundary"/> found above the calling component, as its own
        /// reset does with no arguments — react-error-boundary's <c>resetBoundary</c>.
        /// </summary>
        public Action ResetBoundary { get; }

        /// <summary>
        /// Makes the calling component throw the error on its next render, which reaches the boundaries above it as
        /// any render error does: the first that shows a fallback for it catches it, and one that declines passes it
        /// up — react-error-boundary's <c>showBoundary</c>. Callable from an event handler or an async continuation
        /// on the main thread.
        /// </summary>
        public Action<Exception> ShowBoundary { get; }
    }
}
