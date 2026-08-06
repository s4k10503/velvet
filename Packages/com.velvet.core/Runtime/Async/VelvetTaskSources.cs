using System;
using System.Threading;
using UnityEngine;

namespace Velvet
{
    internal sealed class VelvetTaskSource : IVelvetTaskSource, IPoolableVelvetTaskSource
    {
        VelvetTaskCompletionSourceCore<AsyncUnit> _core = new();
        bool _returnToPoolOnConsume;
        bool _isPooled;

        public short Version => _core.Version;

        public bool IsPooled => _isPooled;

        public void MarkPooled() => _isPooled = true;

        public void ClearPooled() => _isPooled = false;

        internal void MarkReturnToPoolOnConsume() => _returnToPoolOnConsume = true;

        internal void ResetForPool()
        {
            _returnToPoolOnConsume = false;
            _core.Reset();
        }

        public VelvetTaskStatus GetStatus(short version) => _core.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _core.OnCompleted(continuation, state, version);

        public void GetResult(short version)
        {
            var versionBefore = _core.Version;
            try
            {
                _core.GetResult(version);
            }
            finally
            {
                if (_returnToPoolOnConsume && _core.Version != versionBefore)
                {
                    _returnToPoolOnConsume = false;
                    VelvetTaskSourcePool.Return(this);
                }
            }
        }

        public bool TrySetResult() => _core.TrySetResult(AsyncUnit.Default);

        public bool TrySetException(Exception exception) => _core.TrySetException(exception);

        public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
            _core.TrySetCanceled(cancellationToken);
    }

    internal sealed class VelvetTaskSource<T> : IVelvetTaskSource<T>, IPoolableVelvetTaskSource
    {
        VelvetTaskCompletionSourceCore<T> _core = new();
        bool _returnToPoolOnConsume;
        bool _isPooled;

        public short Version => _core.Version;

        public bool IsPooled => _isPooled;

        public void MarkPooled() => _isPooled = true;

        public void ClearPooled() => _isPooled = false;

        internal void MarkReturnToPoolOnConsume() => _returnToPoolOnConsume = true;

        internal void ResetForPool()
        {
            _returnToPoolOnConsume = false;
            _core.Reset();
        }

        public VelvetTaskStatus GetStatus(short version) => _core.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _core.OnCompleted(continuation, state, version);

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T GetResult(short version)
        {
            var versionBefore = _core.Version;
            try
            {
                return _core.GetResult(version);
            }
            finally
            {
                if (_returnToPoolOnConsume && _core.Version != versionBefore)
                {
                    _returnToPoolOnConsume = false;
                    VelvetTaskSourcePool<T>.Return(this);
                }
            }
        }

        public bool TrySetResult(T result) => _core.TrySetResult(result);

        public bool TrySetException(Exception exception) => _core.TrySetException(exception);

        public bool TrySetCanceled(CancellationToken cancellationToken = default) =>
            _core.TrySetCanceled(cancellationToken);
    }

    internal sealed class YieldVelvetTaskSource : IVelvetTaskSource, IPoolableVelvetTaskSource
    {
        readonly VelvetTaskSource _source = new();
        bool _scheduled;
        bool _isPooled;

        public short Version => _source.Version;

        public bool IsPooled => _isPooled;

        public void MarkPooled() => _isPooled = true;

        public void ClearPooled() => _isPooled = false;

        internal void ResetForPool()
        {
            if (_scheduled)
            {
                VelvetTaskFrameDriver.Unschedule(this);
            }

            _scheduled = false;
            _source.ResetForPool();
        }

        internal void Activate()
        {
            _scheduled = true;
            VelvetTaskFrameDriver.Schedule(this);
        }

        internal void CompleteScheduledFrame()
        {
            _scheduled = false;
            _source.TrySetResult();
        }

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        public void GetResult(short version)
        {
            var versionBefore = _source.Version;
            try
            {
                _source.GetResult(version);
            }
            finally
            {
                if (_source.Version != versionBefore)
                {
                    YieldVelvetTaskSourcePool.Return(this);
                }
            }
        }
    }

    internal sealed class AwaitableVelvetTaskSource : IVelvetTaskSource, IPoolableVelvetTaskSource
    {
        readonly VelvetTaskSource _source = new();
        bool _isPooled;

        public short Version => _source.Version;

        public bool IsPooled => _isPooled;

        public void MarkPooled() => _isPooled = true;

        public void ClearPooled() => _isPooled = false;

        internal void ResetForPool() => _source.ResetForPool();

        internal void Initialize(Awaitable awaitable)
        {
            if (awaitable == null)
            {
                throw new ArgumentNullException(nameof(awaitable));
            }

            var awaiter = awaitable.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
            }
            else
            {
                awaiter.OnCompleted(() => Complete(awaitable.GetAwaiter()));
            }
        }

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        public void GetResult(short version)
        {
            var versionBefore = _source.Version;
            try
            {
                _source.GetResult(version);
            }
            finally
            {
                if (_source.Version != versionBefore)
                {
                    AwaitableVelvetTaskSourcePool.Return(this);
                }
            }
        }

        void Complete(Awaitable.Awaiter awaiter)
        {
            try
            {
                awaiter.GetResult();
                _source.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                _source.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _source.TrySetException(ex);
            }
        }
    }

    internal sealed class AwaitableVelvetTaskSource<T> : IVelvetTaskSource<T>, IPoolableVelvetTaskSource
    {
        readonly VelvetTaskSource<T> _source = new();
        bool _isPooled;

        public short Version => _source.Version;

        public bool IsPooled => _isPooled;

        public void MarkPooled() => _isPooled = true;

        public void ClearPooled() => _isPooled = false;

        internal void ResetForPool() => _source.ResetForPool();

        internal void Initialize(Awaitable<T> awaitable)
        {
            if (awaitable == null)
            {
                throw new ArgumentNullException(nameof(awaitable));
            }

            var awaiter = awaitable.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
            }
            else
            {
                awaiter.OnCompleted(() => Complete(awaitable.GetAwaiter()));
            }
        }

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T GetResult(short version)
        {
            var versionBefore = _source.Version;
            try
            {
                return _source.GetResult(version);
            }
            finally
            {
                if (_source.Version != versionBefore)
                {
                    AwaitableVelvetTaskSourcePool<T>.Return(this);
                }
            }
        }

        void Complete(Awaitable<T>.Awaiter awaiter)
        {
            try
            {
                _source.TrySetResult(awaiter.GetResult());
            }
            catch (OperationCanceledException)
            {
                _source.TrySetCanceled();
            }
            catch (Exception ex)
            {
                _source.TrySetException(ex);
            }
        }
    }

    internal sealed class AttachExternalCancellationVelvetTaskSource : IVelvetTaskSource
    {
        static readonly Action<object?> CancellationCallback = static state =>
        {
            var self = (AttachExternalCancellationVelvetTaskSource)state!;
            self._source.TrySetCanceled(self._cancellationToken);
        };

        readonly VelvetTaskSource _source = new();
        readonly CancellationToken _cancellationToken;
        CancellationTokenRegistration _registration;

        public AttachExternalCancellationVelvetTaskSource(VelvetTask task, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(CancellationCallback, this);
            WireAwait(task);
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        public void GetResult(short version) => _source.GetResult(version);

        void WireAwait(VelvetTask task)
        {
            var awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
                return;
            }

            awaiter.OnCompleted(() => Complete(task.GetAwaiter()));
        }

        void Complete(VelvetTask.Awaiter awaiter)
        {
            try
            {
                awaiter.GetResult();
                _source.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                _source.TrySetCanceled(_cancellationToken);
            }
            catch (Exception ex)
            {
                _source.TrySetException(ex);
            }
            finally
            {
                _registration.Dispose();
            }
        }
    }

    internal sealed class AttachExternalCancellationVelvetTaskSource<T> : IVelvetTaskSource<T>
    {
        static readonly Action<object?> CancellationCallback = static state =>
        {
            var self = (AttachExternalCancellationVelvetTaskSource<T>)state!;
            self._source.TrySetCanceled(self._cancellationToken);
        };

        readonly VelvetTaskSource<T> _source = new();
        readonly CancellationToken _cancellationToken;
        CancellationTokenRegistration _registration;

        public AttachExternalCancellationVelvetTaskSource(VelvetTask<T> task, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(CancellationCallback, this);
            WireAwait(task);
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T GetResult(short version) => _source.GetResult(version);

        void WireAwait(VelvetTask<T> task)
        {
            var awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                Complete(awaiter);
                return;
            }

            awaiter.OnCompleted(() => Complete(task.GetAwaiter()));
        }

        void Complete(VelvetTask<T>.Awaiter awaiter)
        {
            try
            {
                _source.TrySetResult(awaiter.GetResult());
            }
            catch (OperationCanceledException)
            {
                _source.TrySetCanceled(_cancellationToken);
            }
            catch (Exception ex)
            {
                _source.TrySetException(ex);
            }
            finally
            {
                _registration.Dispose();
            }
        }
    }

    internal readonly struct AsyncUnit
    {
        public static readonly AsyncUnit Default;
    }
}
