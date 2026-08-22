using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Velvet
{
    internal interface IVelvetTaskStateMachineRunner
    {
        Action MoveNext { get; }

        VelvetTask Task { get; }

        void SetStateMachine(IAsyncStateMachine stateMachine);

        void SetResult();

        void SetException(Exception exception);
    }

    internal interface IVelvetTaskStateMachineRunner<T>
    {
        Action MoveNext { get; }

        VelvetTask<T> Task { get; }

        void SetStateMachine(IAsyncStateMachine stateMachine);

        void SetResult(T result);

        void SetException(Exception exception);
    }

    internal sealed class AsyncVelvetTaskMethod<TStateMachine> :
        IVelvetTaskSource,
        IVelvetTaskStateMachineRunner
        where TStateMachine : IAsyncStateMachine
    {
        const int MaxPoolSize = 64;

        static readonly Stack<AsyncVelvetTaskMethod<TStateMachine>> Pool = new();

        TStateMachine _stateMachine = default!;
        IAsyncStateMachine? _boxedStateMachine;
        VelvetTaskCompletionSourceCore<AsyncUnit> _core = new();
        bool _returnedToPool;

        readonly Action _moveNext;

        AsyncVelvetTaskMethod() => _moveNext = Run;

        public Action MoveNext => _moveNext;

        public VelvetTask Task => new(this);

        public short Version => _core.Version;

        // The runner reaches the builder's field before the state machine is copied onto it: a struct
        // state machine copies by value, so a copy taken first carries a null runner, and neither
        // SetResult nor SetException on the resumed copy then reaches the task the caller holds.
        public static void Rent(ref TStateMachine stateMachine, [NotNull] ref IVelvetTaskStateMachineRunner? field)
        {
            AsyncVelvetTaskMethod<TStateMachine> runner;
            if (Pool.Count == 0)
            {
                runner = new AsyncVelvetTaskMethod<TStateMachine>();
            }
            else
            {
                runner = Pool.Pop();
                runner._core.Reset();
            }

            field = runner;
            runner._stateMachine = stateMachine;
            runner._boxedStateMachine = null;
            runner._returnedToPool = false;
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            _boxedStateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Run()
        {
            if (_boxedStateMachine != null)
            {
                _boxedStateMachine.MoveNext();
            }
            else
            {
                _stateMachine.MoveNext();
            }
        }

        public void SetResult() => _core.TrySetResult(AsyncUnit.Default);

        public void SetException(Exception exception) => _core.TrySetException(exception);

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
            _boxedStateMachine = null;
            if (Pool.Count < MaxPoolSize)
            {
                Pool.Push(this);
            }
        }
    }

    internal sealed class AsyncVelvetTaskMethod<TStateMachine, T> :
        IVelvetTaskSource<T>,
        IVelvetTaskStateMachineRunner<T>
        where TStateMachine : IAsyncStateMachine
    {
        const int MaxPoolSize = 64;

        static readonly Stack<AsyncVelvetTaskMethod<TStateMachine, T>> Pool = new();

        TStateMachine _stateMachine = default!;
        IAsyncStateMachine? _boxedStateMachine;
        VelvetTaskCompletionSourceCore<T> _core = new();
        bool _returnedToPool;

        readonly Action _moveNext;

        AsyncVelvetTaskMethod() => _moveNext = Run;

        public Action MoveNext => _moveNext;

        public VelvetTask<T> Task => new(this);

        public short Version => _core.Version;

        // Same publish-before-copy ordering as the non-generic AsyncVelvetTaskMethod.
        public static void Rent(ref TStateMachine stateMachine, [NotNull] ref IVelvetTaskStateMachineRunner<T>? field)
        {
            AsyncVelvetTaskMethod<TStateMachine, T> runner;
            if (Pool.Count == 0)
            {
                runner = new AsyncVelvetTaskMethod<TStateMachine, T>();
            }
            else
            {
                runner = Pool.Pop();
                runner._core.Reset();
            }

            field = runner;
            runner._stateMachine = stateMachine;
            runner._boxedStateMachine = null;
            runner._returnedToPool = false;
        }

        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            _boxedStateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Run()
        {
            if (_boxedStateMachine != null)
            {
                _boxedStateMachine.MoveNext();
            }
            else
            {
                _stateMachine.MoveNext();
            }
        }

        public void SetResult(T result) => _core.TrySetResult(result);

        public void SetException(Exception exception) => _core.TrySetException(exception);

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
            _boxedStateMachine = null;
            if (Pool.Count < MaxPoolSize)
            {
                Pool.Push(this);
            }
        }
    }
}
