// Hook slot storage mirrors React fiber hook state: committed vs staged fields, and nullable deps
// arrays where null means "always re-run" (UseEffect with no deps array).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    internal sealed class HookBlockerSlot : IDisposable
    {
        public IDisposable? Registration { get; set; }
        public RouteBlockerState State { get; init; } = null!;
        public object?[]? LastDeps { get; set; }
        public object?[]? NextDeps { get; set; }
        public Func<IDisposable>? NextRegister { get; set; }
        public bool NextNeedsReregister { get; set; }

        public void Dispose()
        {
            Registration?.Dispose();
            Registration = null;
        }
    }

    internal sealed class HookCallbackSlot
    {
        public Delegate? Callback { get; set; }
        public object?[]? LastDeps { get; set; }
        public Delegate? NextCallback { get; set; }
        public object?[]? NextDeps { get; set; }
    }

    internal abstract class HookMemoValueSlot
    {
        public object?[]? LastDeps { get; set; }
        public object?[]? NextDeps { get; set; }
        public bool Committed { get; set; }
        public abstract void Commit();
    }

    internal sealed class HookMemoValueSlot<T> : HookMemoValueSlot
    {
        public T Value = default!;
        public T NextValue = default!;

        public override void Commit()
        {
            Value = NextValue;
            LastDeps = NextDeps;
            Committed = true;
        }
    }

    internal abstract class HookDeferredValueSlot { }

    internal sealed class HookDeferredValueSlot<T> : HookDeferredValueSlot
    {
        public T Current = default!;
        public T? Pending;
        public bool HasPending;
    }

    internal abstract class HookOptimisticSlot { }

    internal sealed class HookOptimisticSlot<TState, TAction> : HookOptimisticSlot
    {
        public TState Base = default!;
        public TState OptimisticState = default!;
        public bool HasOptimistic;
        public Func<TState, TAction, TState> Apply = null!;
        public Action<TAction> Add = null!;
    }

    internal sealed class HookEffectSlot
    {
        public Func<Action?>? EffectFactory { get; set; }

        // null LastDeps => re-run every render (no dependency array was supplied).
        public object?[]? LastDeps { get; set; }
        public object?[]? NextDeps { get; set; }
        public Action? Cleanup { get; set; }
    }

    internal sealed class HookIdSlot
    {
        public string Id = null!;
    }

    internal sealed class HookTransitionSlot
    {
        public bool IsPending;
        public TransitionStarter Starter = default!;
    }

    internal sealed class HookImperativeHandleSlot
    {
        public IHookRefSetter? HandleRef;
        public object? Handle;
        public object?[]? LastDeps;
        public object?[]? NextDeps;
        public Func<object>? NextFactory;
        public IHookRefSetter? NextHandleRef;
        public bool NextNeedsRecompute;
    }

    internal sealed class HookMemoSlot
    {
        public object?[]? LastDeps { get; set; }
        public VNode? CachedResult { get; set; }
        public object?[]? NextDeps { get; set; }
        public VNode? NextCachedResult { get; set; }
    }

    internal abstract class HookMutationSlot : IDisposable
    {
        public abstract void Dispose();
    }

    internal sealed class HookMutationSlot<TVariables, TData> : HookMutationSlot
    {
        public MutationResult<TVariables, TData> Result { get; init; } = null!;
        public Func<TVariables, CancellationToken, UniTask<TData>> MutationFn { get; set; } = null!;
        public Action<TData, TVariables>? OnSuccess { get; set; }
        public Action<Exception, TVariables>? OnError { get; set; }
        public CancellationTokenSource? Cts { get; set; }

        public override void Dispose()
        {
            Cts?.Cancel();
            Cts?.Dispose();
            Cts = null;
        }
    }

    internal sealed class HookRefSlot
    {
        public object Ref { get; set; } = null!;
    }

    internal abstract class HookStateSlot { }

    internal sealed class HookStateSlot<T> : HookStateSlot
    {
        public T Value = default!;
        public StateUpdater<T> Setter = default!;
    }

    internal sealed class ReducerSlot<TState, TAction> : HookStateSlot
    {
        public TState Value = default!;
        public Func<TState, TAction, TState> Reducer = null!;
        public Action<TAction> Dispatch = null!;
    }

    internal abstract class HookStoreSlot : IDisposable
    {
        public abstract void Dispose();
    }

    internal sealed class HookStoreSlot<TStore, TSel> : HookStoreSlot
    {
        public Store<TStore> Store = null!;
        public Func<TStore, TSel> Selector = null!;
        public IEqualityComparer<TSel> Comparer = null!;
        public TSel LastValue = default!;
        public IDisposable? Subscription;

        public override void Dispose()
        {
            Subscription?.Dispose();
            Subscription = null;
        }
    }
}
