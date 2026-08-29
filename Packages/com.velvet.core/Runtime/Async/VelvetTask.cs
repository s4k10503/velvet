using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;
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

        public VelvetTaskStatus Status =>
            _source == null ? VelvetTaskStatus.Succeeded : _source.GetStatus(_version);

        public Awaiter GetAwaiter() => new(this);

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

        public static VelvetTask Yield() => new(YieldVelvetTaskSourcePool.Rent());

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

        public static IEnumerator ToCoroutine(Func<VelvetTask> taskFactory) => taskFactory().ToCoroutine();

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

            public void OnCompleted(Action continuation)
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
                        _task._version);
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

        public Awaiter GetAwaiter() => new(this);

        public static VelvetTask<T> FromResult(T result) => new(result);

        public readonly struct Awaiter : INotifyCompletion
        {
            readonly VelvetTask<T> _task;

            internal Awaiter(VelvetTask<T> task) => _task = task;

            public bool IsCompleted =>
                _task._source == null || _task._source.GetStatus(_task._version).IsCompleted();

            public T GetResult() =>
                _task._source == null ? _task._result : _task._source.GetResult(_task._version);

            public void OnCompleted(Action continuation)
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
                        _task._version);
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
