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
    internal sealed class FiberAsyncResource<T> : IFiberAsyncResource
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public object ResourceKey { get; }
        public FiberAsyncResourceStatus Status { get; private set; } = FiberAsyncResourceStatus.Pending;
        public T Result { get; private set; }
        public Exception Error { get; private set; }

        public Action OnCompleted { get; set; }

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
            catch (OperationCanceledException)
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

        // Test-only API that cancels the CancellationToken. Production code should call only
        // Dispose (Dispose performs cancel + token source release + OnCompleted clear).
        // Intentionally excluded from the IFiberAsyncResource interface to prevent production flows
        // (e.g. UseAsync) from accidentally calling Cancel alone and leaking the token source.
        internal void Cancel()
        {
            if (_disposed) return;
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
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
