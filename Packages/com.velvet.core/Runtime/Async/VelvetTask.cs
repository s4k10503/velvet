using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Velvet
{
    internal static class VelvetTaskAwaiterActions
    {
        internal static readonly Action<object?> InvokeContinuation = static state => ((Action)state!).Invoke();
    }

    internal static class VelvetTaskScheduler
    {
        internal static void PublishUnobservedException(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Debug.LogException(exception);
        }
    }

    [AsyncMethodBuilder(typeof(VelvetTaskMethodBuilder))]
    public readonly struct VelvetTask
    {
        readonly IVelvetTaskSource? _source;
        readonly short _version;

        internal VelvetTask(IVelvetTaskSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _version = source.Version;
        }

        internal VelvetTask(IVelvetTaskSource? source, short version)
        {
            _source = source;
            _version = version;
        }

        public VelvetTaskStatus Status =>
            _source == null ? VelvetTaskStatus.Succeeded : _source.GetStatus(_version);

        internal IReadOnlyList<ExceptionDispatchInfo>? Faults => (_source as IVelvetTaskFaults)?.GetFaults(_version);

        public Awaiter GetAwaiter() => new(this);

        public VelvetTask Preserve()
        {
            if (_source == null)
            {
                return this;
            }

            var preserved = new PreservedVelvetTaskSource<AsyncUnit>();
            VelvetTaskOutcome.OnSettled(this, preserved.Settle);
            return new VelvetTask(preserved);
        }

        public Task AsTask() => VelvetTaskOutcome.AsTask(this);

        public SuppressThrowingAwaitable SuppressThrowing() => new(this);

        public static VelvetTask FromResult() => CompletedTask;

        public static VelvetTask<T> FromResult<T>(T result) => VelvetTask<T>.FromResult(result);

        public static VelvetTask FromException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var source = VelvetTaskSourcePool.Rent();
            source.MarkReturnToPoolOnConsume();
            if (exception is OperationCanceledException canceled)
            {
                source.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                source.TrySetException(exception);
            }

            return new VelvetTask(source);
        }

        public static VelvetTask<T> FromException<T>(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var source = VelvetTaskSourcePool<T>.Rent();
            source.MarkReturnToPoolOnConsume();
            if (exception is OperationCanceledException canceled)
            {
                source.TrySetCanceled(canceled.CancellationToken);
            }
            else
            {
                source.TrySetException(exception);
            }

            return new VelvetTask<T>(source);
        }

        public static VelvetTask Yield() =>
            VelvetMainThread.IsCurrent
                ? new(YieldVelvetTaskSourcePool.Rent())
                : new(new OffMainThreadYieldVelvetTaskSource());

        public static SwitchToMainThreadAwaitable SwitchToMainThread() => default;

        public static VelvetTask Never(CancellationToken cancellationToken = default)
        {
            var source = new VelvetTaskCompletionSource();
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = default;
                registration = cancellationToken.Register(() =>
                {
                    if (source.TrySetCanceled(cancellationToken))
                    {
                        registration.Dispose();
                    }
                });
            }

            return source.Task;
        }

        public static VelvetTask<T> Never<T>(CancellationToken cancellationToken = default)
        {
            var source = new VelvetTaskCompletionSource<T>();
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = default;
                registration = cancellationToken.Register(() =>
                {
                    if (source.TrySetCanceled(cancellationToken))
                    {
                        registration.Dispose();
                    }
                });
            }

            return source.Task;
        }

        public static VelvetTask CompletedTask { get; } = default;

        public static VelvetTask WhenAll(params VelvetTask[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return tasks.Length == 0 ? CompletedTask : new VelvetTask(new WhenAllVelvetTaskSource(tasks));
        }

        public static VelvetTask<T[]> WhenAll<T>(params VelvetTask<T>[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return tasks.Length == 0
                ? VelvetTask<T[]>.FromResult(Array.Empty<T>())
                : new VelvetTask<T[]>(new WhenAllVelvetTaskSource<T>(tasks));
        }

        public static IEnumerator ToCoroutine(Func<VelvetTask> taskFactory) => taskFactory().ToCoroutine();

        public readonly struct SwitchToMainThreadAwaitable
        {
            public Awaiter GetAwaiter() => default;

            public readonly struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => VelvetMainThread.IsCurrent;

                public void GetResult()
                {
                }

                public void OnCompleted(Action continuation) =>
                    VelvetMainThread.Post(VelvetTaskAwaiterActions.InvokeContinuation, continuation);
            }
        }

        public readonly struct SuppressThrowingAwaitable
        {
            readonly VelvetTask _task;

            internal SuppressThrowingAwaitable(VelvetTask task) => _task = task;

            public Awaiter GetAwaiter() => new(_task);

            public readonly struct Awaiter : INotifyCompletion
            {
                readonly VelvetTask _task;

                internal Awaiter(VelvetTask task) => _task = task;

                public bool IsCompleted => _task.GetAwaiter().IsCompleted;

                // Only the task's own fault or cancellation is swallowed. A task consumed elsewhere throws from
                // the status read, which precedes the consume, and a read before completion from GetResult,
                // which the filter lets through.
                public VelvetTaskStatus GetResult()
                {
                    var status = _task.Status;
                    try
                    {
                        _task.GetAwaiter().GetResult();
                    }
                    catch (Exception) when (status is VelvetTaskStatus.Faulted or VelvetTaskStatus.Canceled)
                    {
                    }

                    return status;
                }

                public void OnCompleted(Action continuation) => _task.GetAwaiter().OnCompleted(continuation);
            }
        }

        public readonly struct Awaiter : INotifyCompletion
        {
            readonly VelvetTask _task;

            internal Awaiter(VelvetTask task) => _task = task;

            public bool IsCompleted =>
                _task._source == null || _task._source.GetStatus(_task._version).IsCompleted();

            public void GetResult()
            {
                if (_task._source != null)
                {
                    _task._source.GetResult(_task._version);
                }
            }

            public void OnCompleted(Action continuation) => OnCompleted(continuation, VelvetMainThread.IsCurrent);

            internal void OnCompleted(Action continuation, bool resumeOnMainThread)
            {
                if (_task._source == null)
                {
                    continuation();
                }
                else
                {
                    _task._source.OnCompleted(
                        VelvetTaskAwaiterActions.InvokeContinuation,
                        continuation,
                        _task._version,
                        resumeOnMainThread);
                }
            }
        }
    }

    [AsyncMethodBuilder(typeof(VelvetTaskMethodBuilder<>))]
    public readonly struct VelvetTask<T>
    {
        readonly IVelvetTaskSource<T>? _source;
        readonly T _result;
        readonly short _version;

        internal VelvetTask(IVelvetTaskSource<T> source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _version = source.Version;
            _result = default!;
        }

        internal VelvetTask(T result)
        {
            _source = null;
            _version = 0;
            _result = result;
        }

        public VelvetTaskStatus Status =>
            _source == null ? VelvetTaskStatus.Succeeded : _source.GetStatus(_version);

        internal IReadOnlyList<ExceptionDispatchInfo>? Faults => (_source as IVelvetTaskFaults)?.GetFaults(_version);

        public Awaiter GetAwaiter() => new(this);

        public VelvetTask<T> Preserve()
        {
            if (_source == null)
            {
                return this;
            }

            var preserved = new PreservedVelvetTaskSource<T>();
            VelvetTaskOutcome.OnSettled(this, preserved.Settle);
            return new VelvetTask<T>(preserved);
        }

        public Task<T> AsTask() => VelvetTaskOutcome.AsTask(this);

        public VelvetTask.SuppressThrowingAwaitable SuppressThrowing() => ((VelvetTask)this).SuppressThrowing();

        // The view carries this task's version rather than the source's current one, so a task already
        // consumed converts to a view that is consumed too.
        public static implicit operator VelvetTask(VelvetTask<T> task) => new(task._source, task._version);

        public static VelvetTask<T> FromResult(T result) => new(result);

        public readonly struct Awaiter : INotifyCompletion
        {
            readonly VelvetTask<T> _task;

            internal Awaiter(VelvetTask<T> task) => _task = task;

            public bool IsCompleted =>
                _task._source == null || _task._source.GetStatus(_task._version).IsCompleted();

            public T GetResult() =>
                _task._source == null ? _task._result : _task._source.GetResult(_task._version);

            public void OnCompleted(Action continuation) => OnCompleted(continuation, VelvetMainThread.IsCurrent);

            internal void OnCompleted(Action continuation, bool resumeOnMainThread)
            {
                if (_task._source == null)
                {
                    continuation();
                }
                else
                {
                    _task._source.OnCompleted(
                        VelvetTaskAwaiterActions.InvokeContinuation,
                        continuation,
                        _task._version,
                        resumeOnMainThread);
                }
            }
        }
    }

    public struct VelvetTaskMethodBuilder
    {
        IVelvetTaskStateMachineRunner? _runner;
        Exception? _exception;
        VelvetTask? _faultedTask;

        public static VelvetTaskMethodBuilder Create() => default;

        public VelvetTask Task
        {
            get
            {
                if (_runner != null)
                {
                    return _runner.Task;
                }

                if (_exception != null)
                {
                    return _faultedTask ??= VelvetTask.FromException(_exception);
                }

                return VelvetTask.CompletedTask;
            }
        }

        public void SetResult()
        {
            if (_runner != null)
            {
                _runner.SetResult();
            }
        }

        public void SetException(Exception exception)
        {
            if (_runner != null)
            {
                _runner.SetException(exception);
            }
            else
            {
                _exception = exception;
            }
        }

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine =>
            stateMachine.MoveNext();

        // Required by the compiler -- deleting it is CS0656 -- and reached by nothing: Roslyn emits no
        // call to it from a MoveNext, and this builder's own AwaitOnCompleted does not call the state
        // machine's. A runner resumes the copy it was handed, and there is one resume route.
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (_runner == null)
            {
                AsyncVelvetTaskMethod<TStateMachine>.Rent(ref stateMachine, ref _runner);
            }

            awaiter.OnCompleted(_runner.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (_runner == null)
            {
                AsyncVelvetTaskMethod<TStateMachine>.Rent(ref stateMachine, ref _runner);
            }

            awaiter.UnsafeOnCompleted(_runner.MoveNext);
        }
    }

    public struct VelvetTaskMethodBuilder<T>
    {
        IVelvetTaskStateMachineRunner<T>? _runner;
        Exception? _exception;
        T _result;
        VelvetTask<T>? _faultedTask;

        public static VelvetTaskMethodBuilder<T> Create() => default;

        public VelvetTask<T> Task
        {
            get
            {
                if (_runner != null)
                {
                    return _runner.Task;
                }

                if (_exception != null)
                {
                    return _faultedTask ??= VelvetTask.FromException<T>(_exception);
                }

                return new VelvetTask<T>(_result);
            }
        }

        public void SetResult(T result)
        {
            if (_runner != null)
            {
                _runner.SetResult(result);
            }
            else
            {
                _result = result;
            }
        }

        public void SetException(Exception exception)
        {
            if (_runner != null)
            {
                _runner.SetException(exception);
            }
            else
            {
                _exception = exception;
            }
        }

        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine =>
            stateMachine.MoveNext();

        // Required by the compiler -- deleting it is CS0656 -- and reached by nothing: Roslyn emits no
        // call to it from a MoveNext, and this builder's own AwaitOnCompleted does not call the state
        // machine's. A runner resumes the copy it was handed, and there is one resume route.
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (_runner == null)
            {
                AsyncVelvetTaskMethod<TStateMachine, T>.Rent(ref stateMachine, ref _runner);
            }

            awaiter.OnCompleted(_runner.MoveNext);
        }

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            if (_runner == null)
            {
                AsyncVelvetTaskMethod<TStateMachine, T>.Rent(ref stateMachine, ref _runner);
            }

            awaiter.UnsafeOnCompleted(_runner.MoveNext);
        }
    }

}
