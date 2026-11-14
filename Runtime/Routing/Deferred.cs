#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// A value a loader hands back before it has it: React Router's unawaited promise in loader data. A loader
    /// returns it inside its data so the navigation commits without waiting for it, and the route reads it
    /// through <c>V.Await</c> beneath a <c>V.Suspense</c>, which shows its fallback until the value arrives.
    /// Any number of <c>V.Await</c> may read one.
    /// </summary>
    /// <typeparam name="T">The value's type.</typeparam>
    public sealed class Deferred<T> : IDeferred
    {
        private DeferredOutcome? _outcome;
        private List<VelvetTaskCompletionSource<DeferredOutcome>>? _waiting;

        /// <summary>Starts observing <paramref name="task"/>, which it consumes.</summary>
        public Deferred(VelvetTask<T> task) => Observe(task).Forget();

        private async VelvetTask Observe(VelvetTask<T> task)
        {
            DeferredOutcome outcome;
            try
            {
                outcome = new DeferredOutcome(await task, null);
            }
            catch (Exception error)
            {
                outcome = new DeferredOutcome(null, error);
            }

            _outcome = outcome;
            var waiting = _waiting;
            _waiting = null;
            if (waiting == null)
            {
                return;
            }

            foreach (var source in waiting)
            {
                source.TrySetResult(outcome);
            }
        }

        // One task per reader, since a task is consumed by the one await it hands its result to.
        VelvetTask<DeferredOutcome> IDeferred.OutcomeTask()
        {
            if (_outcome != null)
            {
                return VelvetTask.FromResult(_outcome);
            }

            var source = new VelvetTaskCompletionSource<DeferredOutcome>();
            (_waiting ??= new List<VelvetTaskCompletionSource<DeferredOutcome>>()).Add(source);
            return source.Task;
        }
    }

    internal interface IDeferred
    {
        // Completes, never faults, once the value arrives or its task fails.
        VelvetTask<DeferredOutcome> OutcomeTask();
    }

    internal sealed class DeferredOutcome
    {
        internal DeferredOutcome(object? value, Exception? error)
        {
            Value = value;
            Error = error;
        }

        internal object? Value { get; }
        internal Exception? Error { get; }
    }
}
