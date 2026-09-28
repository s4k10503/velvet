using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

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

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _core.OnCompleted(continuation, state, version, resumeOnMainThread);

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

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _core.OnCompleted(continuation, state, version, resumeOnMainThread);

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

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);

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

    // Off the main thread a yield resumes where Task.Yield() does, in its order: through the awaiting
    // thread's synchronization context, then through a task scheduler other than the default, then on
    // the thread pool. Nothing is queued before the continuation is registered, so the await never finds
    // it complete.
    internal sealed class OffMainThreadYieldVelvetTaskSource : IVelvetTaskSource
    {
        static readonly SendOrPostCallback PostCompletion = Complete;
        static readonly Action<object?> ScheduleCompletion = Complete;
        static readonly WaitCallback QueueCompletion = Complete;

        readonly VelvetTaskSource _source = new();

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread)
        {
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);
            var context = SynchronizationContext.Current;
            if (context != null && context.GetType() != typeof(SynchronizationContext))
            {
                context.Post(PostCompletion, this);
            }
            else if (TaskScheduler.Current != TaskScheduler.Default)
            {
                Task.Factory.StartNew(
                    ScheduleCompletion,
                    this,
                    CancellationToken.None,
                    TaskCreationOptions.PreferFairness,
                    TaskScheduler.Current);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(QueueCompletion, this);
            }
        }

        public void GetResult(short version) => _source.GetResult(version);

        static void Complete(object? state) => ((OffMainThreadYieldVelvetTaskSource)state!)._source.TrySetResult();
    }

    // A cancellation of the awaited task keeps that task's own token rather than the attached one, as
    // WaitAsync does.
    internal sealed class AttachExternalCancellationVelvetTaskSource : IVelvetTaskSource, IVelvetTaskFaults
    {
        static readonly Action<object?> CancellationCallback = static state =>
        {
            var self = (AttachExternalCancellationVelvetTaskSource)state!;
            self._source.TrySetCanceled(self._cancellationToken);
        };

        readonly VelvetTaskSource _source = new();
        readonly CancellationToken _cancellationToken;
        CancellationTokenRegistration _registration;
        IReadOnlyList<ExceptionDispatchInfo>? _faults;

        public AttachExternalCancellationVelvetTaskSource(VelvetTask task, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(CancellationCallback, this);
            task.GetAwaiter().OnCompleted(() => Complete(task));
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);

        public void GetResult(short version) => _source.GetResult(version);

        public IReadOnlyList<ExceptionDispatchInfo>? GetFaults(short version) =>
            GetStatus(version) == VelvetTaskStatus.Faulted ? _faults : null;

        void Complete(VelvetTask task)
        {
            var outcome = VelvetTaskOutcome.Consume(task);
            try
            {
                if (outcome.Faults != null)
                {
                    _faults = outcome.Faults;
                    _source.TrySetException(outcome.Faults[0].SourceException);
                }
                else if (outcome.Cancellation != null)
                {
                    _source.TrySetCanceled(outcome.Cancellation.CancellationToken);
                }
                else
                {
                    _source.TrySetResult();
                }
            }
            finally
            {
                _registration.Dispose();
            }
        }
    }

    internal sealed class AttachExternalCancellationVelvetTaskSource<T> : IVelvetTaskSource<T>, IVelvetTaskFaults
    {
        static readonly Action<object?> CancellationCallback = static state =>
        {
            var self = (AttachExternalCancellationVelvetTaskSource<T>)state!;
            self._source.TrySetCanceled(self._cancellationToken);
        };

        readonly VelvetTaskSource<T> _source = new();
        readonly CancellationToken _cancellationToken;
        CancellationTokenRegistration _registration;
        IReadOnlyList<ExceptionDispatchInfo>? _faults;

        public AttachExternalCancellationVelvetTaskSource(VelvetTask<T> task, CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.Register(CancellationCallback, this);
            task.GetAwaiter().OnCompleted(() => Complete(task));
        }

        public short Version => _source.Version;

        public VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T GetResult(short version) => _source.GetResult(version);

        public IReadOnlyList<ExceptionDispatchInfo>? GetFaults(short version) =>
            GetStatus(version) == VelvetTaskStatus.Faulted ? _faults : null;

        void Complete(VelvetTask<T> task)
        {
            var outcome = VelvetTaskOutcome.Consume(task);
            try
            {
                if (outcome.Faults != null)
                {
                    _faults = outcome.Faults;
                    _source.TrySetException(outcome.Faults[0].SourceException);
                }
                else if (outcome.Cancellation != null)
                {
                    _source.TrySetCanceled(outcome.Cancellation.CancellationToken);
                }
                else
                {
                    _source.TrySetResult(outcome.Result);
                }
            }
            finally
            {
                _registration.Dispose();
            }
        }
    }

    internal abstract class WhenAllVelvetTaskSourceBase : IVelvetTaskFaults
    {
        readonly IReadOnlyList<ExceptionDispatchInfo>?[] _faults;
        readonly OperationCanceledException?[] _cancellations;
        List<ExceptionDispatchInfo>? _allFaults;
        int _remaining;

        // The count is whole before the derived constructor wires its first member, because a member that
        // is already complete settles during that loop: a count raised as the loop goes reaches zero on
        // such a member and publishes there.
        protected WhenAllVelvetTaskSourceBase(int memberCount)
        {
            _faults = new IReadOnlyList<ExceptionDispatchInfo>?[memberCount];
            _cancellations = new OperationCanceledException?[memberCount];
            _remaining = memberCount;
        }

        public abstract VelvetTaskStatus GetStatus(short version);

        public IReadOnlyList<ExceptionDispatchInfo>? GetFaults(short version) =>
            GetStatus(version) == VelvetTaskStatus.Faulted ? _allFaults : null;

        protected void OnMemberSettled<T>(int index, VelvetTaskOutcome<T> outcome)
        {
            _faults[index] = outcome.Faults;
            _cancellations[index] = outcome.Cancellation;
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                PublishSettled();
            }
        }

        protected abstract void Publish(Exception? failure);

        // Task.WhenAll's order: every member's faults in argument order, and a cancellation only where no
        // member faulted -- the first cancelled member's in argument order.
        void PublishSettled()
        {
            OperationCanceledException? cancellation = null;
            for (var i = 0; i < _faults.Length; i++)
            {
                var memberFaults = _faults[i];
                if (memberFaults != null)
                {
                    (_allFaults ??= new List<ExceptionDispatchInfo>()).AddRange(memberFaults);
                }

                cancellation ??= _cancellations[i];
            }

            Publish(_allFaults?[0].SourceException ?? cancellation);
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
                VelvetTaskOutcome.OnSettled(tasks[i], outcome => OnMemberSettled(index, outcome));
            }
        }

        public short Version => _source.Version;

        public override VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);

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
                VelvetTaskOutcome.OnSettled(tasks[i], outcome =>
                {
                    _results[index] = outcome.Result;
                    OnMemberSettled(index, outcome);
                });
            }
        }

        public short Version => _source.Version;

        public override VelvetTaskStatus GetStatus(short version) => _source.GetStatus(version);

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread) =>
            _source.OnCompleted(continuation, state, version, resumeOnMainThread);

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
    }

    // Settles once, from the task it preserves, and then answers any number of reads and awaits: its
    // version never moves, which is what lets the same task be consumed again.
    internal sealed class PreservedVelvetTaskSource<T> : IVelvetTaskSource<T>, IVelvetTaskFaults
    {
        readonly object _gate = new();
        List<(Action<object?> Continuation, object? State, bool ResumeOnMainThread)>? _waiting = new();
        VelvetTaskOutcome<T> _outcome;

        public short Version => 0;

        internal void Settle(VelvetTaskOutcome<T> outcome)
        {
            List<(Action<object?> Continuation, object? State, bool ResumeOnMainThread)> waiting;
            lock (_gate)
            {
                _outcome = outcome;
                waiting = _waiting!;
                _waiting = null;
            }

            foreach (var (continuation, state, resumeOnMainThread) in waiting)
            {
                // Taken per continuation rather than once, since each may have been registered on a
                // different thread; the rule itself is VelvetTaskCompletionSourceCore's.
                if (resumeOnMainThread && !VelvetMainThread.IsCurrent)
                {
                    VelvetMainThread.Post(continuation, state);
                }
                else
                {
                    continuation(state);
                }
            }
        }

        public VelvetTaskStatus GetStatus(short version)
        {
            lock (_gate)
            {
                return _waiting == null ? _outcome.Status : VelvetTaskStatus.Pending;
            }
        }

        public IReadOnlyList<ExceptionDispatchInfo>? GetFaults(short version)
        {
            lock (_gate)
            {
                return _waiting == null ? _outcome.Faults : null;
            }
        }

        public void OnCompleted(Action<object?> continuation, object? state, short version, bool resumeOnMainThread)
        {
            lock (_gate)
            {
                if (_waiting != null)
                {
                    _waiting.Add((continuation, state, resumeOnMainThread));
                    return;
                }
            }

            continuation(state);
        }

        void IVelvetTaskSource.GetResult(short version) => GetResult(version);

        public T GetResult(short version)
        {
            VelvetTaskOutcome<T> outcome;
            lock (_gate)
            {
                if (_waiting != null)
                {
                    throw new InvalidOperationException("The VelvetTask is not completed.");
                }

                outcome = _outcome;
            }

            if (outcome.Faults != null)
            {
                outcome.Faults[0].Throw();
            }

            if (outcome.Cancellation != null)
            {
                throw outcome.Cancellation;
            }

            return outcome.Result;
        }
    }

    internal readonly struct AsyncUnit
    {
        public static readonly AsyncUnit Default;
    }
}
