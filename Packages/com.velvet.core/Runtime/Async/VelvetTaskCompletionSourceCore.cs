using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Velvet
{
    internal static class VelvetTaskCompletionSourceCoreShared
    {
        internal static readonly Action<object?> Sentinel = _ => throw new InvalidOperationException("The sentinel delegate should never be invoked.");
    }

    internal struct VelvetTaskCompletionSourceCore<TResult>
    {
        TResult _result;
        object? _error;
        short _version;
        int _completedCount;
        Action<object?>? _continuation;
        object? _continuationState;

        public short Version => _version;

        public void Reset()
        {
            unchecked
            {
                _version++;
            }

            _completedCount = 0;
            _result = default!;
            _error = null;
            _continuation = null;
            _continuationState = null;
        }

        public bool TrySetResult(TResult result)
        {
            if (Interlocked.Increment(ref _completedCount) != 1)
            {
                return false;
            }

            _result = result;
            InvokeContinuation();
            return true;
        }

        public bool TrySetException(Exception exception)
        {
            if (Interlocked.Increment(ref _completedCount) != 1)
            {
                return false;
            }

            _error = exception is OperationCanceledException
                ? exception
                : ExceptionDispatchInfo.Capture(exception);
            InvokeContinuation();
            return true;
        }

        public bool TrySetCanceled(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _completedCount) != 1)
            {
                return false;
            }

            _error = new OperationCanceledException(cancellationToken);
            InvokeContinuation();
            return true;
        }

        public VelvetTaskStatus GetStatus(short token)
        {
            ValidateToken(token);
            if (_completedCount == 0)
            {
                return VelvetTaskStatus.Pending;
            }

            if (_error == null)
            {
                return VelvetTaskStatus.Succeeded;
            }

            return _error is OperationCanceledException
                ? VelvetTaskStatus.Canceled
                : VelvetTaskStatus.Faulted;
        }

        public TResult GetResult(short token)
        {
            ValidateToken(token);
            if (_completedCount == 0)
            {
                throw new InvalidOperationException("The VelvetTask is not completed.");
            }

            unchecked
            {
                _version++;
            }

            if (_error != null)
            {
                if (_error is OperationCanceledException canceled)
                {
                    throw canceled;
                }

                if (_error is ExceptionDispatchInfo dispatchInfo)
                {
                    dispatchInfo.Throw();
                }

                throw new InvalidOperationException("The VelvetTask faulted with an invalid exception type.");
            }

            return _result;
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            ValidateToken(token);

            var previous = _continuation;
            if (previous == null)
            {
                _continuationState = state;
                previous = Interlocked.CompareExchange(ref _continuation, continuation, null);
            }

            if (previous != null)
            {
                if (!ReferenceEquals(previous, VelvetTaskCompletionSourceCoreShared.Sentinel))
                {
                    throw new InvalidOperationException("The VelvetTask has already been awaited.");
                }

                continuation(state);
            }
        }

        void InvokeContinuation()
        {
            var continuation = _continuation;
            if (continuation != null
                || Interlocked.CompareExchange(ref _continuation, VelvetTaskCompletionSourceCoreShared.Sentinel, null) != null)
            {
                continuation?.Invoke(_continuationState);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ValidateToken(short token)
        {
            if (token != _version)
            {
                throw new InvalidOperationException("The VelvetTask has already been consumed.");
            }
        }
    }
}
