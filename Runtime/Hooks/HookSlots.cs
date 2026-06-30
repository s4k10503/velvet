// annotations only: incremental nullable hygiene. See the comment at the top of Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    // Internal state for a single blocker registered via UseBlocker.
    // Holds the registration handle into Router.RouteBlockerManager, the public state object, and the dependency array.
    internal sealed class HookBlockerSlot : IDisposable
    {
        public IDisposable Registration { get; set; }

        // Public state returned from UseBlocker. The same instance is kept across renders (fixed at slot creation).
        public RouteBlockerState State { get; init; }

        // Committed dependency array, fixed across render-phase re-run attempts.
        public object[] LastDeps { get; set; }

        // Deps / pending registration staged by the current Render. The blocker's (Dispose -> re-register) side
        // effect is deferred to the render-phase settle so a discarded attempt cannot register a stale predicate
        // closure against Router.RouteBlockerManager. NextRegister is the settled attempt's
        // registration delegate (null when no re-registration is needed or the router is absent).
        public object[] NextDeps { get; set; }

        public Func<IDisposable> NextRegister { get; set; }

        public bool NextNeedsReregister { get; set; }

        public void Dispose()
        {
            Registration?.Dispose();
            Registration = null;
        }
    }

    // Internal state for callbacks registered via UseCallback.
    // Holds the same delegate reference as long as the dependency array is unchanged.
    internal sealed class HookCallbackSlot
    {
        // Committed callback: the one that the last settled render returned.
        public Delegate Callback { get; set; }

        // Committed dependency array, fixed across render-phase re-run attempts.
        public object[] LastDeps { get; set; }

        // Callback / deps staged by the current Render, promoted to Callback / LastDeps
        // only once the render-phase loop settles (FiberRenderer commits them). Keeping the committed values fixed
        // during the loop lets each discarded attempt compare against the committed render, preserving referential
        // stability across a render-phase state-update re-run.
        public Delegate NextCallback { get; set; }

        public object[] NextDeps { get; set; }
    }

    // Non-generic base for HookMemoValueSlot<T>. ComponentFiber stores
    // List<HookMemoValueSlot> and casts to the concrete generic per call site, mirroring the
    // HookStateSlot hierarchy. The dependency arrays live on the base so the commit loop can
    // promote them without knowing the value type.
    internal abstract class HookMemoValueSlot
    {
        // Committed dependency array, fixed across render-phase re-run attempts. Null until the first render settles.
        public object[] LastDeps { get; set; }

        // Dependency array staged by the current Render, promoted to LastDeps only once the
        // render-phase loop settles. Keeping the committed deps fixed during the loop lets each discarded
        // attempt compare against the committed render, preserving value stability across a re-run.
        public object[] NextDeps { get; set; }

        // True once at least one render has settled, so a default(T) committed value is
        // distinguishable from "never committed" for value-type results.
        public bool Committed { get; set; }

        // Promotes the staged value / deps to the committed baseline. Overridden to copy the typed value.
        public abstract void Commit();
    }

    // Value memoization slot allocated by UseMemo<T>. Holds the same computed value as long as
    // the dependency array is unchanged. The staged (NextValue) / committed
    // (Value) split mirrors HookCallbackSlot so a discarded render-phase re-run
    // attempt cannot poison the committed baseline.
    // T: Type of the memoized value.
    internal sealed class HookMemoValueSlot<T> : HookMemoValueSlot
    {
        // Committed value: the one the last settled render returned.
        public T Value { get; set; }

        // Value staged by the current Render, promoted to Value on settle.
        public T NextValue { get; set; }

        public override void Commit()
        {
            Value = NextValue;
            LastDeps = NextDeps;
            Committed = true;
        }
    }

    // Persistent slot (base) for UseDeferredValue. Holds a type-erased HookDeferredValueSlot<T>.
    internal abstract class HookDeferredValueSlot { }

    // Persistent slot for UseDeferredValue<T>. Current holds the most recently committed value,
    // while Pending holds the value scheduled to apply on the next Transition flush.
    // Preserved across re-renders.
    internal sealed class HookDeferredValueSlot<T> : HookDeferredValueSlot
    {
        public T Current;
        public T Pending;
        public bool HasPending;
    }

    // Persistent slot (base) for UseOptimistic. Holds a type-erased HookOptimisticSlot<TState,TAction>.
    internal abstract class HookOptimisticSlot { }

    // Persistent slot for UseOptimistic<TState, TAction>. Base tracks the latest
    // pass-through state; while an optimistic action is outstanding, OptimisticState holds the
    // derived value shown to the component. The optimistic value is discarded once Base changes
    // (the real update landed). Preserved across re-renders.
    // TState: Optimistic state type.
    // TAction: Optimistic action / payload type passed to the apply function.
    internal sealed class HookOptimisticSlot<TState, TAction> : HookOptimisticSlot
    {
        public TState Base;
        public TState OptimisticState;
        public bool HasOptimistic;
        public Func<TState, TAction, TState> Apply;
        public Action<TAction> Add;
    }

    // Internal state for effects registered via UseEffect / UseLayoutEffect.
    // Held as a position-based slot; deps are compared during Render, while cleanup / factory run during RunEffects.
    internal sealed class HookEffectSlot
    {
        public Func<Action> EffectFactory { get; set; }

        // Committed dependency array: the deps of the render that last settled. null means "re-run on every
        // render". Elements may also be nullable. This stays fixed across render-phase re-run attempts so each
        // discarded attempt compares against the committed render, not the previous attempt.
        public object?[]? LastDeps { get; set; }

        // Dependency array staged by the current Render, promoted to LastDeps only once the
        // render-phase loop settles (see HookEffectExecutor.CommitEffectDeps).
        public object?[]? NextDeps { get; set; }

        public Action Cleanup { get; set; }
    }

    // Persistent slot for UseId. Holds the unique ID string bound to a single hook position
    // of one fiber.
    // Stored here so subsequent re-renders return the same ID generated on the first render.
    internal sealed class HookIdSlot
    {
        public string Id;
    }

    // Persistent slot for one UseTransition call position. Each call gets an independent
    // IsPending flag and a reference-stable Starter; every
    // UseTransition() has its own isPending (two transitions in one component do not share state).
    internal sealed class HookTransitionSlot
    {
        public bool IsPending;
        public TransitionStarter Starter;
    }

    // Handle slot allocated by UseImperativeHandle.
    // Holds the handle value to be stored into the Ref<THandle> the parent passed via
    // componentRef:, the previous deps used for comparison, and the handle ref itself.
    // Stored in ComponentFiber's list as a position-based slot.
    internal sealed class HookImperativeHandleSlot
    {
        public IHookRefSetter HandleRef;
        public object Handle;
        public object[] LastDeps;

        // Staged by the current Render. The handle factory invocation and the parent ref write are deferred to
        // the render-phase settle so a discarded attempt cannot expose a handle built from a throwaway render.
        public object[] NextDeps;
        public Func<object> NextFactory;
        public IHookRefSetter NextHandleRef;
        public bool NextNeedsRecompute;
    }

    // Component-level memoization slot for components annotated with [Component(Memoize = true)].
    // Distinct from the FiberMemoCache path used by V.Memoized(); this slot lives in the Fiber's hook slot list.
    internal sealed class HookMemoSlot
    {
        // Committed dependency array, fixed across render-phase re-run attempts.
        public object[] LastDeps { get; set; }

        // Committed VNode output. Reused while LastDeps remains unchanged.
        public VNode CachedResult { get; set; }

        // Deps / cached VNode staged by the current Render, promoted to LastDeps /
        // CachedResult only once the render-phase loop settles. A discarded render-phase attempt
        // therefore cannot poison the committed memo baseline (which would force a spurious subtree rebuild).
        public object[] NextDeps { get; set; }

        public VNode NextCachedResult { get; set; }
    }

    // Non-generic base for HookMutationSlot<TVariables, TData>. ComponentFiber
    // stores List<HookMutationSlot> and casts to the concrete generic per call site, mirroring
    // the HookStateSlot hierarchy.
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

        // CTS for the latest in-flight MutationResult<TVariables, TData>.MutateAsync.
        // Replaced on each call so superseded runs are cancelled.
        public CancellationTokenSource? Cts { get; set; }

        public override void Dispose()
        {
            Cts?.Cancel();
            Cts?.Dispose();
            Cts = null;
        }
    }

    // Persistent slot for UseRef. Holds the Ref<T> bound to a single hook position
    // of one fiber.
    // Stored here so the same reference is returned across re-renders.
    internal sealed class HookRefSlot
    {
        public object Ref { get; set; } = null!;
    }

    // Common base for UseState / UseReducer slots.
    // Stored in a List<HookStateSlot> as a position-based slot.
    // On each Render, cast to HookStateSlot<T> / ReducerSlot<S,A> for type-safe access.
    // Type mismatches indicate a hook-order violation and fail-fast.
    internal abstract class HookStateSlot
    {
    }

    // State slot allocated by UseState.
    // T: Type of the state being held.
    internal sealed class HookStateSlot<T> : HookStateSlot
    {
        public T Value;

        // Unified setter exposed to the component. Carries both the direct value-setter and the
        // functional-updater closures, so a single returned object handles setValue(next) and
        // setValue(prev => next). Built once at slot creation and
        // cached thereafter for reference stability across renders.
        public StateUpdater<T> Setter;
    }

    // State slot allocated by UseReducer.
    // The reducer may be updated on every render (since it can capture surrounding scope via closures).
    internal sealed class ReducerSlot<TState, TAction> : HookStateSlot
    {
        public TState Value;
        public Func<TState, TAction, TState> Reducer;

        // Dispatch delegate. Built once at slot creation and cached thereafter.
        public Action<TAction> Dispatch;
    }

    // Common base for UseStore slots.
    // Store subscriptions are released per Component on Unmount by disposing each slot's subscription handle.
    // selector and comparer may be updated on every render (because they capture surrounding scope via closures).
    internal abstract class HookStoreSlot : IDisposable
    {
        public abstract void Dispose();
    }

    // Selector subscription slot for a Store<TStore>.
    // TStore: State type of the Store.
    // TSel: Output type of the selector.
    internal sealed class HookStoreSlot<TStore, TSel> : HookStoreSlot
    {
        public Store<TStore> Store;
        public Func<TStore, TSel> Selector;
        public IEqualityComparer<TSel> Comparer;
        public TSel LastValue;
        public IDisposable Subscription;

        public override void Dispose()
        {
            Subscription?.Dispose();
            Subscription = null;
        }
    }
}
