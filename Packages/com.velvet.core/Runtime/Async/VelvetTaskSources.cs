using System;
using System.Threading;

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

    internal abstract class WhenAllVelvetTaskSourceBase
    {
        readonly Exception?[] _failures;
        int _remaining;

        // The count is whole before the derived constructor wires its first member, because a member that
        // is already complete settles during that loop: a count raised as the loop goes reaches zero on
        // such a member and publishes there.
        protected WhenAllVelvetTaskSourceBase(int memberCount)
        {
            _failures = new Exception?[memberCount];
            _remaining = memberCount;
        }

        protected void OnMemberSettled(int index, Exception? failure)
        {
            _failures[index] = failure;
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                Publish(FirstFault() ?? FirstCancellation());
            }
        }

        protected abstract void Publish(Exception? failure);

        Exception? FirstFault()
        {
            foreach (var failure in _failures)
            {
                if (failure is not null and not OperationCanceledException)
                {
                    return failure;
                }
            }

            return null;
        }

        Exception? FirstCancellation()
        {
            foreach (var failure in _failures)
            {
                if (failure is OperationCanceledException)
                {
                    return failure;
                }
            }

            return null;
        }
    }

    internal sealed class WhenAllVelvetTaskSource : WhenAllVelvetTaskSourceBase, IVelvetTaskSource
    {
        readonly VelvetTaskSource _source = new();

        internal WhenAllVelvetTaskSource(VelvetTask[] tasks)
            : base(tasks.Length)
        {
            for (var i = 0; i < tasks.Length; i++)
            {
                var index = i;
                var awaiter = tasks[i].GetAwaiter();
                awaiter.OnCompleted(() => Settle(index, awaiter));
            }
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        public void GetResult(short version) => _source.GetResult(version);

        protected override void Publish(Exception? failure)
        {
            if (failure == null)
            {
                _source.TrySetResult();
            }
            else if (failure is OperationCanceledException canceled)
            {
                _source.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                _source.TrySetException(failure);
            }
        }

        void Settle(int index, VelvetTask.Awaiter awaiter)
        {
            Exception? failure = null;
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            OnMemberSettled(index, failure);
        }
    }

    internal sealed class WhenAllVelvetTaskSource<T> : WhenAllVelvetTaskSourceBase, IVelvetTaskSource<T[]>
    {
        readonly VelvetTaskSource<T[]> _source = new();
        readonly T[] _results;

        internal WhenAllVelvetTaskSource(VelvetTask<T>[] tasks)
            : base(tasks.Length)
        {
            _results = new T[tasks.Length];
            for (var i = 0; i < tasks.Length; i++)
            {
                var index = i;
                var awaiter = tasks[i].GetAwaiter();
                awaiter.OnCompleted(() => Settle(index, awaiter));
            }
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version) =>
            _source.OnCompleted(continuation, state, version);

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T[] GetResult(short version) => _source.GetResult(version);

        protected override void Publish(Exception? failure)
        {
            if (failure == null)
            {
                _source.TrySetResult(_results);
            }
            else if (failure is OperationCanceledException canceled)
            {
                _source.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                _source.TrySetException(failure);
            }
        }

        void Settle(int index, VelvetTask<T>.Awaiter awaiter)
        {
            Exception? failure = null;
            try
            {
                _results[index] = awaiter.GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            OnMemberSettled(index, failure);
        }
    }

    internal readonly struct AsyncUnit
    {
        public static readonly AsyncUnit Default;
    }
}
