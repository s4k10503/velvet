// Hook slot storage keeps committed vs staged fields separate, with a null deps array meaning
// "always re-run" (UseEffect with no deps array).
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

    // Shared probe for hook-slot values that may be memoized VNode roots. The recycle sweep must
    // not return pooled objects a slot still holds: with stable inputs the SAME instance re-enters
    // a later committed tree (e.g. V.When toggling a memoized subtree), so its pooled parts stay
    // live across renders that omit it. Recognizes a node or any list of nodes (arrays and
    // List&lt;VNode&gt; both satisfy the covariant IReadOnlyList) — a node buried inside a user
    // composite (tuple / record) is NOT visible here; that boundary is part of the recycle
    // contract documented on FiberTreeReturn.
    internal static class HookSlotRecycleProbe
    {
        public static object? Probe(object? value)
            => value is VNode || value is IReadOnlyList<VNode?> ? value : null;
    }

    internal abstract class HookMemoValueSlot
    {
        public object?[]? LastDeps { get; set; }
        public object?[]? NextDeps { get; set; }
        public bool Committed { get; set; }
        public abstract void Commit();

        // The committed value when it is a memoized VNode root — see HookSlotRecycleProbe.
        public abstract object? RecycleMarkRoot { get; }
    }

    internal sealed class HookMemoValueSlot<T> : HookMemoValueSlot
    {
        public T Value = default!;
        public T NextValue = default!;

        // Evaluated once per instantiation so struct-valued slots skip the probe without the
        // box-then-isinst the raw type test would emit on backends that cannot fold it.
        private static readonly bool s_canHoldNodes = !typeof(T).IsValueType;

        public override void Commit()
        {
            Value = NextValue;
            LastDeps = NextDeps;
            Committed = true;
        }

        public override object? RecycleMarkRoot => s_canHoldNodes ? HookSlotRecycleProbe.Probe(Value) : null;
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

        // The held Ref&lt;T&gt;'s current value when it is a VNode root (element-in-ref caching) —
        // see HookSlotRecycleProbe. Ref is typed object here, so the probe goes through the
        // ref's own accessor.
        public object? RecycleMarkRoot => (Ref as IHookRefSetter)?.RecycleMarkRoot;
    }

    internal abstract class HookStateSlot
    {
        // The committed state value when it is a VNode root (element-in-state caching) —
        // see HookSlotRecycleProbe.
        public abstract object? RecycleMarkRoot { get; }
    }

    internal sealed class HookStateSlot<T> : HookStateSlot
    {
        public T Value = default!;
        public StateUpdater<T> Setter = default!;

        private static readonly bool s_canHoldNodes = !typeof(T).IsValueType;

        public override object? RecycleMarkRoot => s_canHoldNodes ? HookSlotRecycleProbe.Probe(Value) : null;
    }

    internal sealed class ReducerSlot<TState, TAction> : HookStateSlot
    {
        public TState Value = default!;
        public Func<TState, TAction, TState> Reducer = null!;
        public Action<TAction> Dispatch = null!;

        private static readonly bool s_canHoldNodes = !typeof(TState).IsValueType;

        public override object? RecycleMarkRoot => s_canHoldNodes ? HookSlotRecycleProbe.Probe(Value) : null;
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
