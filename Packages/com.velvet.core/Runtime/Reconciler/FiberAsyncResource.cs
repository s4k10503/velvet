using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    internal enum FiberAsyncResourceStatus
    {
        Pending,
        Success,
        Error,
    }

    // Non-generic base for the async resource that the Use<T> hook binds to an AsyncSlot.
    // Used to store different T types together in ComponentFiber's AsyncSlots.
    internal interface IFiberAsyncResource : IDisposable
    {
        FiberAsyncResourceStatus Status { get; }

        // Identity of the resource this slot represents; the slot is keyed by this resource
        // instance. When the next render presents a key that is not reference-equal, the slot is recreated.
        object ResourceKey { get; }
    }

    // State machine representing a single async fetch.
    // Stored in a Use<T> slot and reused across re-renders when deps are equal.
    // State transitions: Pending → Success / Error. Once a terminal state is entered, it does not restart
    // (the entire slot is discarded and recreated).
    // Cancel / Dispose cancels the token, but if the loader does not honor ct the task may keep running internally.
    // Even in that case, the OnCompleted callback satisfies the contract of "reflecting the Status at completion".
    // Only this resource's own cancellation leaves the machine non-terminal. A cancellation raised by a token
    // the consumer owns becomes Error instead: a resource left Pending holds its Suspense boundary in its
    // fallback, and Hooks.UseCore starts a resource only on the render that allocates its slot, so a slot whose
    // key is unchanged never gets another attempt.
    internal sealed class FiberAsyncResource<T> : IFiberAsyncResource
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public object ResourceKey { get; }
        public FiberAsyncResourceStatus Status { get; private set; } = FiberAsyncResourceStatus.Pending;
        public T Result { get; private set; } = default!;
        public Exception? Error { get; private set; }

        public Action? OnCompleted { get; set; }

        public FiberAsyncResource(object resourceKey)
        {
            ResourceKey = resourceKey;
        }

        public void Start(Func<CancellationToken, UniTask<T>> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (Status != FiberAsyncResourceStatus.Pending)
            {
                return;
            }

            UniTask<T> task;
            try
            {
                task = factory(_cts.Token);
            }
            catch (Exception ex)
            {
                Error = ex;
                Status = FiberAsyncResourceStatus.Error;
                return;
            }

            if (task.Status.IsCompletedSuccessfully())
            {
                Result = task.GetAwaiter().GetResult();
                Status = FiberAsyncResourceStatus.Success;
                return;
            }

            if (task.Status.IsFaulted())
            {
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Error = ex;
                }
                Status = FiberAsyncResourceStatus.Error;
                return;
            }

            AwaitAsync(task).Forget();
        }

        private async UniTask AwaitAsync(UniTask<T> task)
        {
            try
            {
                var result = await task.AttachExternalCancellation(_cts.Token);
                if (_disposed || _cts.IsCancellationRequested) return;
                Result = result;
                Status = FiberAsyncResourceStatus.Success;
            }
            // The gate asks who requested the cancellation, not which token carried it out to the awaiting frame,
            // so a loader that awaits on some token of its own is still judged by whether this resource asked.
            // Everything else falls through to the generic catch and lands in Error (see the class note above).
            catch (OperationCanceledException) when (_disposed || _cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (_disposed) return;
                Error = ex;
                Status = FiberAsyncResourceStatus.Error;
            }
            OnCompleted?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
            _cts.Dispose();
            OnCompleted = null;
        }
    }
}
