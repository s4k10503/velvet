using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Velvet
{
    internal interface IVelvetTaskStateMachineRunner
    {
        Action MoveNext { get; }

        void SetResult();

        void SetException(Exception exception);
    }

    internal interface IVelvetTaskStateMachineRunner<T>
    {
        Action MoveNext { get; }

        void SetResult(T result);

        void SetException(Exception exception);
    }

    internal sealed class AsyncVelvetTaskMethod<TStateMachine> :
        IVelvetTaskSource,
        IVelvetTaskStateMachineRunner
        where TStateMachine : IAsyncStateMachine
    {
        static readonly MainThreadPool<AsyncVelvetTaskMethod<TStateMachine>> Pool = new();

        TStateMachine _stateMachine = default!;
        VelvetTaskCompletionSourceCore<AsyncUnit> _core = new();
        bool _returnedToPool;

        readonly Action _moveNext;

        AsyncVelvetTaskMethod() => _moveNext = Run;

        public Action MoveNext => _moveNext;

        public short Version => _core.Version;

        // The runner reaches the builder's field before the state machine is copied onto it: a struct
        // state machine copies by value, so a copy taken first carries a null runner, and neither
        // SetResult nor SetException on the resumed copy then reaches the task the caller holds.
        // The task is taken here, at the version this call owns, and not on each read of the builder's
        // Task: the consume resets the runner and returns it to the pool, so a later read would see the
        // next version, and once the pool hands the runner on, another call's task.
        public static void Rent(
            ref TStateMachine stateMachine,
            [NotNull] ref IVelvetTaskStateMachineRunner? field,
            ref VelvetTask? task)
        {
            var runner = Pool.Rent();
            if (runner == null)
            {
                runner = new AsyncVelvetTaskMethod<TStateMachine>();
            }
            else
            {
                runner._core.Reset();
            }

            field = runner;
            task = new VelvetTask(runner);
            runner._stateMachine = stateMachine;
            runner._returnedToPool = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Run() => _stateMachine.MoveNext();

        public void SetResult() => _core.TrySetResult(AsyncUnit.Default);

        public void SetException(Exception exception) => _core.TrySetException(exception);

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
                if (_core.Version != versionBefore)
                {
                    ReturnToPool();
                }
            }
        }

        void ReturnToPool()
        {
            if (_returnedToPool)
            {
                throw new InvalidOperationException("The async state machine runner has already been returned to the pool.");
            }

            _returnedToPool = true;
            _core.Reset();
            _stateMachine = default!;
            Pool.Return(this);
        }
    }

    internal sealed class AsyncVelvetTaskMethod<TStateMachine, T> :
        IVelvetTaskSource<T>,
        IVelvetTaskStateMachineRunner<T>
        where TStateMachine : IAsyncStateMachine
    {
        static readonly MainThreadPool<AsyncVelvetTaskMethod<TStateMachine, T>> Pool = new();

        TStateMachine _stateMachine = default!;
        VelvetTaskCompletionSourceCore<T> _core = new();
        bool _returnedToPool;

        readonly Action _moveNext;

        AsyncVelvetTaskMethod() => _moveNext = Run;

        public Action MoveNext => _moveNext;

        public short Version => _core.Version;

        // Same publish-before-copy ordering, and the same once-per-call task, as the non-generic
        // AsyncVelvetTaskMethod.
        public static void Rent(
            ref TStateMachine stateMachine,
            [NotNull] ref IVelvetTaskStateMachineRunner<T>? field,
            ref VelvetTask<T>? task)
        {
            var runner = Pool.Rent();
            if (runner == null)
            {
                runner = new AsyncVelvetTaskMethod<TStateMachine, T>();
            }
            else
            {
                runner._core.Reset();
            }

            field = runner;
            task = new VelvetTask<T>(runner);
            runner._stateMachine = stateMachine;
            runner._returnedToPool = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Run() => _stateMachine.MoveNext();

        public void SetResult(T result) => _core.TrySetResult(result);

        public void SetException(Exception exception) => _core.TrySetException(exception);

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
                if (_core.Version != versionBefore)
                {
                    ReturnToPool();
                }
            }
        }

        void ReturnToPool()
        {
            if (_returnedToPool)
            {
                throw new InvalidOperationException("The async state machine runner has already been returned to the pool.");
            }

            _returnedToPool = true;
            _core.Reset();
            _stateMachine = default!;
            Pool.Return(this);
        }
    }
}
