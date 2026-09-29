using System;
using System.Threading;

namespace Velvet
{
    public sealed class VelvetTaskCompletionSource
    {
        readonly MultiAwaitVelvetTaskSource<AsyncUnit> _source = new();

        public VelvetTask Task => new(_source);

        public void SetResult()
        {
            if (!TrySetResult())
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (!TrySetException(exception))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetCanceled(CancellationToken cancellationToken = default)
        {
            if (!TrySetCanceled(cancellationToken))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public bool TrySetResult() => _source.TrySettle(default);

        public bool TrySetException(Exception exception) =>
            _source.TrySettle(VelvetTaskOutcome.FromException<AsyncUnit>(exception));

        public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
            _source.TrySettle(new VelvetTaskOutcome<AsyncUnit>(default, null, new OperationCanceledException(cancellationToken)));
    }

    public sealed class VelvetTaskCompletionSource<T>
    {
        readonly MultiAwaitVelvetTaskSource<T> _source = new();

        public VelvetTask<T> Task => new(_source);

        public void SetResult(T result)
        {
            if (!TrySetResult(result))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (!TrySetException(exception))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetCanceled(CancellationToken cancellationToken = default)
        {
            if (!TrySetCanceled(cancellationToken))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public bool TrySetResult(T result) => _source.TrySettle(new VelvetTaskOutcome<T>(result, null, null));

        public bool TrySetException(Exception exception) =>
            _source.TrySettle(VelvetTaskOutcome.FromException<T>(exception));

        public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
            _source.TrySettle(new VelvetTaskOutcome<T>(default!, null, new OperationCanceledException(cancellationToken)));
    }
}
