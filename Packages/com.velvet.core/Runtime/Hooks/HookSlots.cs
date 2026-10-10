// Hook slot storage keeps committed vs staged fields separate, with a null deps array meaning
// "always re-run" (UseEffect with no deps array).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    internal sealed class HookBlockerSlot : IDisposable
    {
        public IDisposable? Registration { get; set; }
        public RouteBlockerState State { get; init; } = null!;
        public object?[]? LastDeps { get; set; }
        public object?[]? NextDeps { get; set; }
        // The router the registration is held against: a render under a different router registers anew
        // whatever the deps say.
        public Router? LastRouter { get; set; }
        public Router? NextRouter { get; set; }
        // The predicate the settle hands the registration; null when the committed one stands.
        public Func<BlockerFunctionArgs, bool>? NextPredicate { get; set; }

        public void Dispose()
        {
            // Detached first: the disposal returns the state to Idle, and a fiber being torn down has no render
            // left to run for it.
            State.StateChanged = null;
            Registration?.Dispose();
            Registration = null;
        }
    }

    internal sealed class HookErrorBoundaryKeysSlot
    {
        private readonly ComponentFiber _boundary;
        private object?[]? _renderedKeys;
        private Action<ErrorBoundaryResetDetails>? _renderedOnReset;
        private bool _renderedAfterACatch;
        private int _renderedAt;
        private object?[]? _committedKeys;
        private Action<ErrorBoundaryResetDetails>? _committedOnReset;

        public HookErrorBoundaryKeysSlot(ComponentFiber boundary)
        {
            _boundary = boundary;
            Commit = CommitRender;
        }

        // Cached so that registering the layout effect builds no delegate per render.
        public Func<Action?> Commit { get; }

        // Each attempt overwrites the last. FiberRenderer.RenderAndReconcile counts a render once it has run to the
        // end, so the count this one would reach tells the commit whether what was recorded is that render's.
        public void Record(object?[]? keys, Action<ErrorBoundaryResetDetails>? onReset, bool afterACatch)
        {
            _renderedKeys = keys;
            _renderedOnReset = onReset;
            _renderedAfterACatch = afterACatch;
            _renderedAt = _boundary.RenderCount + 1;
        }

        // react-error-boundary's componentDidUpdate: the keys and onReset of the render this commit lands become
        // the committed ones, and prevProps' keys are the ones the commit before it landed. An attempt that threw
        // is not counted, so a replay of this effect after it — a Suspense reveal that does not render the
        // boundary — puts back the committed onReset and resets nothing.
        private Action? CommitRender()
        {
            var prev = _committedKeys;
            var landed = _renderedAt == _boundary.RenderCount;
            if (landed)
            {
                _committedKeys = _renderedKeys;
                _committedOnReset = _renderedOnReset;
            }
            _boundary.OnErrorBoundaryReset = _committedOnReset;
            if (!landed || !_renderedAfterACatch || _boundary.CaughtError == null) return null;
            var next = _committedKeys;
            if (ObjectIs.AreEqualDeps(prev ?? Array.Empty<object?>(), next ?? Array.Empty<object?>())) return null;
            _boundary.OnErrorBoundaryReset?.Invoke(new ErrorBoundaryResetDetails(
                ErrorBoundaryResetReason.Keys, Array.Empty<object?>(), prev, next));
            FiberErrorBoundary.QueueReset(_boundary);
            return null;
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

    internal abstract class HookOptimisticSlot
    {
        public ComponentFiber Fiber = null!;

        // A settle renders nobody on its own, so a retirement asks for the render that drops the entries.
        // Reached only from a transition this slot enrolled on, which leaves it holding an entry of that
        // transition's until this removes or disowns them all.
        internal void RetireEntriesOwnedBy(HookTransitionSlot owner)
        {
            Retire(owner);
            ComponentFiber.RequestRenderForSettledTransition(Fiber);
        }

        internal abstract void Retire(HookTransitionSlot owner);

        internal abstract void Reown(HookTransitionSlot from, HookTransitionSlot to);

        // For a fiber whose slot list is being dropped: a transition settling afterwards would otherwise ask
        // that fiber, reused or not, for a render on behalf of a slot it no longer holds.
        internal abstract void DetachFromOwners();
    }

    internal sealed class OptimisticEntry<TAction>
    {
        public TAction Action = default!;
        // Null for an entry no transition owns (FiberWorkLoop.CurrentOptimisticOwner read null), which the
        // component's next Transition-lane render drops.
        public HookTransitionSlot? Owner;
        // An entry its transition settles before any render folded it is disowned rather than dropped, so the
        // render addOptimistic requested still shows it once.
        public bool Rendered;
    }

    internal sealed class HookOptimisticSlot<TState, TAction> : HookOptimisticSlot
    {
        // The actions rather than the state they produced, in the order addOptimistic received them, so a
        // render folds them over the pass-through state it is handed and an entry still pending lands on
        // whatever the authoritative state has become.
        public readonly List<OptimisticEntry<TAction>> Entries = new();
        public Func<TState, TAction, TState> Apply = null!;
        public Action<TAction> Add = null!;

        // drainingFiber: FiberWorkLoop.TransitionDrainFiber, null outside a Transition-lane drain. An entry whose
        // owner settles with that fiber's commit is left out of this render, so the commit that lands the
        // transition's work shows it already gone. Nothing is removed here, since the render may never commit:
        // Retire does that at the settle.
        internal TState Fold(TState passthroughState, ComponentFiber? drainingFiber)
        {
            var state = passthroughState;
            foreach (var entry in Entries)
            {
                entry.Rendered = true;
                if (drainingFiber != null && entry.Owner?.SettlesWithCommitOf(drainingFiber) == true)
                {
                    continue;
                }
                state = Apply(state, entry.Action);
            }
            return state;
        }

        // Static lambdas, since both run during renders and a capturing one would allocate at each.
        internal bool HasUnownedEntry => Entries.Exists(static entry => entry.Owner == null);

        internal void DropUnownedEntries() => Entries.RemoveAll(static entry => entry.Owner == null);

        internal override void Retire(HookTransitionSlot owner)
        {
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (!ReferenceEquals(entry.Owner, owner))
                {
                    continue;
                }
                if (entry.Rendered)
                {
                    Entries.RemoveAt(i);
                }
                else
                {
                    entry.Owner = null;
                }
            }
        }

        internal override void Reown(HookTransitionSlot from, HookTransitionSlot to)
        {
            foreach (var entry in Entries)
            {
                if (ReferenceEquals(entry.Owner, from))
                {
                    entry.Owner = to;
                }
            }
        }

        internal override void DetachFromOwners()
        {
            foreach (var entry in Entries)
            {
                entry.Owner?.OptimisticDependents?.Remove(this);
            }
        }
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
        // Every clear retires the optimistic entries this transition owns, the release an unmount forces
        // included: the task that would have settled it no longer can, so nothing else would retire them.
        public bool IsPending
        {
            get => _isPending;
            set
            {
                var settled = _isPending && !value;
                _isPending = value;
                if (settled)
                {
                    RetireOptimisticEntries();
                }
            }
        }
        private bool _isPending;
        // The optimistic slots holding an entry made while FiberWorkLoop.CurrentOptimisticOwner named this slot.
        public List<HookOptimisticSlot>? OptimisticDependents;

        internal void EnrolOptimisticDependent(HookOptimisticSlot slot)
        {
            OptimisticDependents ??= new List<HookOptimisticSlot>();
            if (!OptimisticDependents.Contains(slot))
            {
                OptimisticDependents.Add(slot);
            }
        }

        // True where this transition's last outstanding work is the commit of drainingFiber's Transition-lane
        // drain, which is when the settle clears this slot. The conditions are the ones
        // ComponentFiber.SettleIfNothingOutstanding clears on, plus that no async action is in flight, since
        // RetireOptimisticEntries holds the entries behind one.
        internal bool SettlesWithCommitOf(ComponentFiber drainingFiber)
            => !HasActiveOwner
                && !IsAsyncInFlight
                && !FiberWorkLoop.AsyncActionsInFlight.IsPending
                && EnrolledFibers is { Count: 1 }
                && ReferenceEquals(EnrolledFibers[0], drainingFiber);

        private void RetireOptimisticEntries()
        {
            if (OptimisticDependents == null)
            {
                return;
            }
            // The in-flight slot's own clear reaches here with its flag already false, so this is never it.
            // A transition settling while any async action is in flight keeps its entries until none is left:
            // the settle is not the last thing the entries wait for, so they move to the slot that stands for
            // the actions in flight and no render is asked for here.
            var inFlight = FiberWorkLoop.AsyncActionsInFlight;
            if (inFlight.IsPending)
            {
                foreach (var slot in OptimisticDependents)
                {
                    slot.Reown(this, inFlight);
                    inFlight.EnrolOptimisticDependent(slot);
                }
                OptimisticDependents.Clear();
                return;
            }
            foreach (var slot in OptimisticDependents)
            {
                slot.RetireEntriesOwnedBy(this);
            }
            OptimisticDependents.Clear();
        }

        // An awaiting async StartTransition may hold IsPending=true on a fiber with NO pending lane (its
        // setState calls come after the await), and a drain callback armed earlier can legitimately fire
        // on that clean fiber — the settle-time sweep (SettleTransitionPending) must not read the absence of
        // enrolled work as "the transition settled" and wipe the flag mid-flight. Only the async completion
        // path clears IsPending while this is set.
        public int AsyncOwnerDepth;
        public bool IsAsyncInFlight => AsyncOwnerDepth > 0;
        // Every StartTransition call sharing THIS slot contributes one level until its callback completes.
        // A further call joins the pending lifecycle already open instead of clearing the flag when an outer
        // callback returns first. Scoped to the slot, not the fiber: a call on a different slot is a concurrent
        // transition, not a nested one, and owns its own pending flag.
        public int OwnerDepth;
        public bool HasActiveOwner => OwnerDepth > 0;
        // The fibers a write scheduled under THIS slot's open scope enrolled the Transition lane on, each
        // removed by the drain that commits it. Recorded per fiber rather than as a flag on this slot,
        // because the component owning the state a callback writes need not be the one the hook was
        // declared on, and the flag then has to answer "is the work my callback queued still queued", which
        // no single fiber's lane queue can be read for: a lane is shared, so another slot's transition or a
        // UseDeferredValue re-queue reports as this slot's work, and a write to another component leaves
        // this one's queue empty. ComponentFiber.EnrolledTransitionSlots is the reverse of this list.
        public List<ComponentFiber>? EnrolledFibers;
        public bool HasQueuedWork => EnrolledFibers is { Count: > 0 };
        // The component that declared this UseTransition and reads its isPending. Held because a settle
        // driven by another fiber's drain has to reach it — see ComponentFiber.DischargeTransitionEnrolments.
        public ComponentFiber DeclaringFiber = null!;
        // The isPending the declaring component's last render read, recorded where UseTransition hands it
        // back. An exit that clears this flag has no render behind it, so it owes that component a render
        // exactly when its last render read the flag true; asking on every clear instead would charge a
        // render to every startTransition whose flag no render had seen.
        public bool LastRenderedPending;
        // Bumped whenever ownership changes hands, including the release an unmount forces on a slot whose
        // async action is still awaiting. An owner compares its own value before touching the flags above,
        // so a task settling after that release cannot clear a pending state a later owner is managing.
        public int OwnerGeneration;
        // The error of the call whose callback returned last, thrown from the declaring component's next
        // Transition-lane render, and the count of those returns that orders them — see FiberWorkLoop.Dispatch.
        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? PendingError;
        public int OutcomeSequence;
        public TransitionStarter Starter = default!;
    }

    internal sealed class HookImperativeHandleSlot
    {
        public IHookRefSetter? HandleRef;
        public object? Handle;
        public object?[]? LastDeps;
        public object?[]? NextDeps;
        public Func<object>? NextFactory;
        // The factory the last commit consumed, which a reveal the fiber does not render creates the handle from
        // again (FiberEffects.ShowLayoutEffects).
        public Func<object>? Factory;
        public IHookRefSetter? NextHandleRef;
        public bool NextNeedsRecompute;
    }

    internal sealed class HookMemoSlot
    {
        public object?[]? LastDeps { get; set; }
        public VNode? CachedResult { get; set; }
        public object?[]? NextDeps { get; set; }
        public VNode? NextCachedResult { get; set; }
        public int HookCallCountAtGate { get; set; }
        // Set once and never cleared: no hit is served after a hook has run past the gate.
        public bool RunsHooksPastGate { get; set; }
    }

    internal abstract class HookMutationSlot : IDisposable
    {
        public abstract void Dispose();
    }

    // TContext is Unit for the options records that carry no context. The callbacks are read through the
    // members below rather than stored as delegates of one shape, so the context-free records reach the
    // slot as the caller built them: adapting them to the context shape would allocate on every render.
    internal abstract class HookMutationSlot<TVariables, TData, TContext> : HookMutationSlot
    {
        public MutationResult<TVariables, TData> Result { get; } = new();
        // Every call in flight, not just the newest: two Mutate calls run side by side, so unmounting has
        // more than one token to cancel. Who may write the observed Status / Data is Generation's to say
        // instead: a call writes only while it still holds the current value, and Reset advances that too,
        // so every call then in flight has lost it. The hook options' callbacks are every call's own either
        // way; a call's per-call callbacks are delivered only while it holds the current value.
        public List<CancellationTokenSource> Live { get; } = new();

        public long Generation { get; set; }

        public abstract VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token);
        public abstract TContext InvokeOnMutate(TVariables variables);
        // What a per-call callback receives as the context, which the context-free slot declines to box.
        public virtual object? BoxContext(TContext context) => context;
        public abstract void InvokeOnSuccess(TData data, TVariables variables, TContext context);
        public abstract void InvokeOnError(Exception error, TVariables variables, TContext context);
        public abstract void InvokeOnSettled(TData data, Exception? error, TVariables variables, TContext context);

        public override void Dispose()
        {
            // Snapshot and clear before cancelling: a registration-based cancellation runs its
            // continuations inside Cancel(), so a mutation's own finally reaches back into Live while
            // this walks it. Clearing first is also what makes that finally's Remove return false, which
            // is how it knows not to dispose a source this loop still has to cancel.
            var live = Live.ToArray();
            Live.Clear();
            foreach (var callSource in live)
            {
                // Contained on RouteLoaderRunner.Retire's terms: this source is the slot's own and
                // its token goes to the mutation function, so a callback firing here can be the
                // application's.
                try
                {
                    callSource.Cancel();
                }
                catch (Exception cancellationFailure)
                {
                    FiberLogger.LogException(nameof(HookMutationSlot<TVariables, TData, TContext>), cancellationFailure);
                }
                callSource.Dispose();
            }
        }
    }

    internal sealed class HookMutationSlot<TVariables, TData> : HookMutationSlot<TVariables, TData, Unit>
    {
        public MutationOptions<TVariables, TData> Options { get; set; } = null!;

        public override VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token) =>
            Options.MutationFn(variables, token);

        public override Unit InvokeOnMutate(TVariables variables) => Unit.Default;

        public override object? BoxContext(Unit context) => null;

        public override void InvokeOnSuccess(TData data, TVariables variables, Unit context) =>
            Options.OnSuccess?.Invoke(data, variables);

        public override void InvokeOnError(Exception error, TVariables variables, Unit context) =>
            Options.OnError?.Invoke(error, variables);

        public override void InvokeOnSettled(TData data, Exception? error, TVariables variables, Unit context) =>
            Options.OnSettled?.Invoke(data, error, variables);
    }

    internal sealed class HookContextMutationSlot<TVariables, TData, TContext>
        : HookMutationSlot<TVariables, TData, TContext>
    {
        public MutationOptions<TVariables, TData, TContext> Options { get; set; } = null!;

        public override VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token) =>
            Options.MutationFn(variables, token);

        public override TContext InvokeOnMutate(TVariables variables) =>
            Options.OnMutate is { } onMutate ? onMutate(variables) : default!;

        public override void InvokeOnSuccess(TData data, TVariables variables, TContext context) =>
            Options.OnSuccess?.Invoke(data, variables, context);

        public override void InvokeOnError(Exception error, TVariables variables, TContext context) =>
            Options.OnError?.Invoke(error, variables, context);

        public override void InvokeOnSettled(TData data, Exception? error, TVariables variables, TContext context) =>
            Options.OnSettled?.Invoke(data, error, variables, context);
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

    // Holds no snapshot of its own beyond Value, the one the last render returned; the store stays the
    // authority, and a notification compares against a fresh GetSnapshot read.
    internal sealed class HookExternalStoreSlot<T> : HookStoreSlot
    {
        public ComponentFiber Fiber = null!;
        public Func<T> GetSnapshot = null!;
        public T Value = default!;
        // Null until a subscribe call has returned, so one that threw is called again by the next render.
        public Func<Action, Action>? Subscribe;
#if UNITY_EDITOR
        public bool ReportedUncachedSnapshot;
#endif
        // Set by a render that subscribes or that returns a snapshot other than the previous render's, and
        // cleared by the commit check it queues. A render can return a wave pin the store has already moved
        // past, and a subscription made after that move is never notified of it, so every render until the
        // check commits queues it: a render-phase re-run discards the queue of the attempt that set it.
        public bool AwaitsCommitCheck;
        private HookEffectSlot? _commitCheck;
        private Action? _unsubscribe;
        private bool _subscribing;

        // Queued on the fiber's pending layout effects rather than run from the render: requested there, the
        // re-render is a render-phase update, which re-runs the body against the same wave pin.
        public HookEffectSlot CommitCheck => _commitCheck ??= new HookEffectSlot { EffectFactory = RunCommitCheck };

        // React re-checks the snapshot at commit after a render that changed it, so a getSnapshot building a
        // new value per read fails the check after every render. The update-depth test in
        // UseSyncExternalStoreTests pins where that ends.
        public void Record(Func<T> getSnapshot, T value)
        {
            if (!ObjectIs.AreEqual(Value, value)) AwaitsCommitCheck = true;
            GetSnapshot = getSnapshot;
            Value = value;
        }

        // A notification raised while subscribe runs is dropped here: the render that called this reads the
        // snapshot again once it returns.
        // The previous unsubscribe runs uncontained here: a throw from it is a throw from the render.
        public void Resubscribe(Func<Action, Action> subscribe)
        {
            Detach()?.Invoke();
            _subscribing = true;
            try
            {
                _unsubscribe = subscribe(OnStoreChange);
            }
            finally
            {
                _subscribing = false;
            }
            Subscribe = subscribe;
            AwaitsCommitCheck = true;
        }

        private Action? RunCommitCheck()
        {
            AwaitsCommitCheck = false;
            if (SnapshotChanged()) FiberWorkLoop.RequestExternalStoreRender(Fiber);
            return null;
        }

        private void OnStoreChange()
        {
            if (!VelvetMainThread.IsCurrent)
            {
                throw new InvalidOperationException(
                    "UseSyncExternalStore: the store-change callback passed to subscribe was invoked off the Unity" +
                    " main thread. Marshal the notification to the main thread before invoking it.");
            }
            if (_subscribing || !SnapshotChanged()) return;
            FiberWorkLoop.RequestExternalStoreRender(Fiber);
        }

        // A throwing GetSnapshot counts as a change, so the render repeats the call and its exception reaches
        // the error boundary, as UseStore does for a throwing selector.
        private bool SnapshotChanged()
        {
            try
            {
                return !ObjectIs.AreEqual(Value, GetSnapshot());
            }
            catch
            {
                return true;
            }
        }

        // Contained as HookEffectExecutor.RunCleanups contains a throwing effect cleanup, so an unmount still
        // reaches the fiber's other slots.
        public override void Dispose()
        {
            var unsubscribe = Detach();
            try
            {
                unsubscribe?.Invoke();
            }
            catch (Exception exception)
            {
                ComponentBoundarySearch.PropagateException(Fiber, exception);
            }
        }

        private Action? Detach()
        {
            var unsubscribe = _unsubscribe;
            _unsubscribe = null;
            Subscribe = null;
            return unsubscribe;
        }
    }
}
