using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Velvet
{
    internal readonly struct VelvetTaskOutcome<T>
    {
        internal readonly T Result;
        internal readonly IReadOnlyList<ExceptionDispatchInfo>? Faults;
        internal readonly OperationCanceledException? Cancellation;

        internal VelvetTaskOutcome(
            T result,
            IReadOnlyList<ExceptionDispatchInfo>? faults,
            OperationCanceledException? cancellation)
        {
            Result = result;
            Faults = faults;
            Cancellation = cancellation;
        }

        internal VelvetTaskStatus Status =>
            Faults != null ? VelvetTaskStatus.Faulted
            : Cancellation != null ? VelvetTaskStatus.Canceled
            : VelvetTaskStatus.Succeeded;
    }

    internal static class VelvetTaskOutcome
    {
        internal static VelvetTaskOutcome<T> FromException<T>(Exception exception) =>
            new(default!, new[] { ExceptionDispatchInfo.Capture(exception) }, null);

        // A settle runs on the thread that completes the task, as ValueTask.AsTask() and Preserve() do:
        // handed to the main thread instead, a caller blocking there on what it settles would never see it.
        internal static void OnSettled(VelvetTask task, Action<VelvetTaskOutcome<AsyncUnit>> settle) =>
            task.GetAwaiter().OnCompleted(() => settle(Consume(task)), resumeOnMainThread: false);

        internal static void OnSettled<T>(VelvetTask<T> task, Action<VelvetTaskOutcome<T>> settle) =>
            task.GetAwaiter().OnCompleted(() => settle(Consume(task)), resumeOnMainThread: false);

        internal static Task AsTask(VelvetTask task)
        {
            var completion = new TaskCompletionSource<Task<AsyncUnit>>();
            OnSettled(task, outcome => completion.TrySetResult(Settled(outcome)));
            return completion.Task.Unwrap();
        }

        internal static Task<T> AsTask<T>(VelvetTask<T> task)
        {
            var completion = new TaskCompletionSource<Task<T>>();
            OnSettled(task, outcome => completion.TrySetResult(Settled(outcome)));
            return completion.Task.Unwrap();
        }

        // The status and the faults are read ahead of GetResult, which retires the version they are read under.
        // The status, not the exception's type, tells a cancellation from a fault that is an OperationCanceledException.
        internal static VelvetTaskOutcome<AsyncUnit> Consume(VelvetTask task)
        {
            var status = VelvetTaskStatus.Pending;
            IReadOnlyList<ExceptionDispatchInfo>? faults = null;
            try
            {
                status = task.Status;
                faults = task.Faults;
                task.GetAwaiter().GetResult();
                return default;
            }
            catch (OperationCanceledException canceled) when (status == VelvetTaskStatus.Canceled)
            {
                return new VelvetTaskOutcome<AsyncUnit>(default, null, canceled);
            }
            catch (Exception fault)
            {
                return new VelvetTaskOutcome<AsyncUnit>(
                    default,
                    faults ?? new[] { ExceptionDispatchInfo.Capture(fault) },
                    null);
            }
        }

        internal static VelvetTaskOutcome<T> Consume<T>(VelvetTask<T> task)
        {
            var status = VelvetTaskStatus.Pending;
            IReadOnlyList<ExceptionDispatchInfo>? faults = null;
            try
            {
                status = task.Status;
                faults = task.Faults;
                return new VelvetTaskOutcome<T>(task.GetAwaiter().GetResult(), null, null);
            }
            catch (OperationCanceledException canceled) when (status == VelvetTaskStatus.Canceled)
            {
                return new VelvetTaskOutcome<T>(default!, null, canceled);
            }
            catch (Exception fault)
            {
                return new VelvetTaskOutcome<T>(
                    default!,
                    faults ?? new[] { ExceptionDispatchInfo.Capture(fault) },
                    null);
            }
        }

        static Task<T> Settled<T>(VelvetTaskOutcome<T> outcome)
        {
            if (outcome.Faults != null)
            {
                var exceptions = new Exception[outcome.Faults.Count];
                for (var i = 0; i < exceptions.Length; i++)
                {
                    exceptions[i] = outcome.Faults[i].SourceException;
                }

                var faulted = new TaskCompletionSource<T>();
                faulted.TrySetException(exceptions);
                return faulted.Task;
            }

            return outcome.Cancellation != null
                ? CanceledWith<T>(outcome.Cancellation)
                : Task.FromResult(outcome.Result);
        }

        static async Task<T> CanceledWith<T>(OperationCanceledException cancellation)
        {
            await Task.CompletedTask;
            throw cancellation;
        }
    }
}
