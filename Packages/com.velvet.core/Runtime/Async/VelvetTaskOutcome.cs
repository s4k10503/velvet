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
        internal static void OnSettled(VelvetTask task, Action<VelvetTaskOutcome<AsyncUnit>> settle) =>
            task.GetAwaiter().OnCompleted(() => settle(Consume(task)));

        internal static void OnSettled<T>(VelvetTask<T> task, Action<VelvetTaskOutcome<T>> settle) =>
            task.GetAwaiter().OnCompleted(() => settle(Consume(task)));

        internal static Task AsTask(VelvetTask task)
        {
            var completion = new TaskCompletionSource<AsyncUnit>();
            OnSettled(task, outcome => Complete(completion, outcome));
            return completion.Task;
        }

        internal static Task<T> AsTask<T>(VelvetTask<T> task)
        {
            var completion = new TaskCompletionSource<T>();
            OnSettled(task, outcome => Complete(completion, outcome));
            return completion.Task;
        }

        // The faults are read ahead of GetResult, which retires the version they are read under.
        static VelvetTaskOutcome<AsyncUnit> Consume(VelvetTask task)
        {
            IReadOnlyList<ExceptionDispatchInfo>? faults = null;
            try
            {
                faults = task.Faults;
                task.GetAwaiter().GetResult();
                return default;
            }
            catch (OperationCanceledException canceled)
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

        static VelvetTaskOutcome<T> Consume<T>(VelvetTask<T> task)
        {
            IReadOnlyList<ExceptionDispatchInfo>? faults = null;
            try
            {
                faults = task.Faults;
                return new VelvetTaskOutcome<T>(task.GetAwaiter().GetResult(), null, null);
            }
            catch (OperationCanceledException canceled)
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

        static void Complete<T>(TaskCompletionSource<T> completion, VelvetTaskOutcome<T> outcome)
        {
            if (outcome.Faults != null)
            {
                var exceptions = new Exception[outcome.Faults.Count];
                for (var i = 0; i < exceptions.Length; i++)
                {
                    exceptions[i] = outcome.Faults[i].SourceException;
                }

                completion.TrySetException(exceptions);
            }
            else if (outcome.Cancellation != null)
            {
                completion.TrySetCanceled(outcome.Cancellation.CancellationToken);
            }
            else
            {
                completion.TrySetResult(outcome.Result);
            }
        }
    }
}
