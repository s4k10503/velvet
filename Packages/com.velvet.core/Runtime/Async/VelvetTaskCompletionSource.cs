using System;
using System.Threading;

namespace Velvet
{
    public sealed class VelvetTaskCompletionSource
    {
        readonly VelvetTaskSource _source = new();

        // Built once: a view built per read would carry the source's version after a consume, and so could
        // be consumed again.
        public VelvetTaskCompletionSource() => Task = new(_source);

        public VelvetTask Task { get; }

        public void SetResult()
        {
            if (!_source.TrySetResult())
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

            if (!_source.TrySetException(exception))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetCanceled(CancellationToken cancellationToken = default)
        {
            if (!_source.TrySetCanceled(cancellationToken))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public bool TrySetResult() => _source.TrySetResult();

        public bool TrySetException(Exception exception) => _source.TrySetException(exception);

        public bool TrySetCanceled(CancellationToken cancellationToken = default) => _source.TrySetCanceled(cancellationToken);
    }

    public sealed class VelvetTaskCompletionSource<T>
    {
        readonly VelvetTaskSource<T> _source = new();

        public VelvetTaskCompletionSource() => Task = new(_source);

        public VelvetTask<T> Task { get; }

        public void SetResult(T result)
        {
            if (!_source.TrySetResult(result))
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

            if (!_source.TrySetException(exception))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public void SetCanceled(CancellationToken cancellationToken = default)
        {
            if (!_source.TrySetCanceled(cancellationToken))
            {
                throw new InvalidOperationException("The VelvetTaskCompletionSource is already completed.");
            }
        }

        public bool TrySetResult(T result) => _source.TrySetResult(result);

        public bool TrySetException(Exception exception) => _source.TrySetException(exception);

        public bool TrySetCanceled(CancellationToken cancellationToken = default) => _source.TrySetCanceled(cancellationToken);
    }
}
