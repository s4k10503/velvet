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
            FiberWorkLoop.OweSettleRender(Fiber);
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

    internal abstract class HookMutationSlot
    {
        // Set when the mount that created this slot unmounts. A remount of the same fiber builds new
        // slots, so a call still in flight from the earlier mount matches none of them, which the fiber's
        // IsDisposed cannot say once the remount has cleared it.
        public bool Retired { get; set; }
    }

    // TContext is Unit for the options records that carry no context. The callbacks are read through the
    // members below rather than stored as delegates of one shape, so the context-free records reach the
    // slot as the caller built them: adapting them to the context shape would allocate on every render.
    internal abstract class HookMutationSlot<TVariables, TData, TContext> : HookMutationSlot
    {
        public MutationResult<TVariables, TData> Result { get; } = new();
        // Who may write the observed Status / Data is Generation's to say: a call writes only while it
        // still holds the current value, and Reset advances that too, so every call then in flight has lost
        // it. The hook options' callbacks are every call's own either way; a call's per-call callbacks are
        // delivered only while it holds the current value.
        public long Generation { get; set; }

        public abstract RetryPolicy? Retry { get; }
        public abstract VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token);
        public abstract VelvetTask<TContext> InvokeOnMutate(TVariables variables);
        // What a per-call callback receives as the context, which the context-free slot declines to box.
        public virtual object? BoxContext(TContext context) => context;
        public abstract VelvetTask InvokeOnSuccess(TData data, TVariables variables, TContext context);
        public abstract VelvetTask InvokeOnError(Exception error, TVariables variables, TContext context);
        public abstract VelvetTask InvokeOnSettled(TData data, Exception? error, TVariables variables, TContext context);
    }

    internal sealed class HookMutationSlot<TVariables, TData> : HookMutationSlot<TVariables, TData, Unit>
    {
        public MutationOptions<TVariables, TData> Options { get; set; } = null!;

        public override RetryPolicy? Retry => Options.Retry;

        public override VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token) =>
            Options.MutationFn(variables, token);

        public override VelvetTask<Unit> InvokeOnMutate(TVariables variables) => VelvetTask.FromResult(Unit.Default);

        public override object? BoxContext(Unit context) => null;

        public override VelvetTask InvokeOnSuccess(TData data, TVariables variables, Unit context) =>
            Options.OnSuccess is { } onSuccess ? onSuccess(data, variables) : VelvetTask.CompletedTask;

        public override VelvetTask InvokeOnError(Exception error, TVariables variables, Unit context) =>
            Options.OnError is { } onError ? onError(error, variables) : VelvetTask.CompletedTask;

        public override VelvetTask InvokeOnSettled(TData data, Exception? error, TVariables variables, Unit context) =>
            Options.OnSettled is { } onSettled ? onSettled(data, error, variables) : VelvetTask.CompletedTask;
    }

    internal sealed class HookContextMutationSlot<TVariables, TData, TContext>
        : HookMutationSlot<TVariables, TData, TContext>
    {
        public MutationOptions<TVariables, TData, TContext> Options { get; set; } = null!;

        public override RetryPolicy? Retry => Options.Retry;

        public override VelvetTask<TData> InvokeMutationFn(TVariables variables, CancellationToken token) =>
            Options.MutationFn(variables, token);

        public override VelvetTask<TContext> InvokeOnMutate(TVariables variables) =>
            Options.OnMutate is { } onMutate ? onMutate(variables) : VelvetTask.FromResult(default(TContext)!);

        public override VelvetTask InvokeOnSuccess(TData data, TVariables variables, TContext context) =>
            Options.OnSuccess is { } onSuccess ? onSuccess(data, variables, context) : VelvetTask.CompletedTask;

        public override VelvetTask InvokeOnError(Exception error, TVariables variables, TContext context) =>
            Options.OnError is { } onError ? onError(error, variables, context) : VelvetTask.CompletedTask;

        public override VelvetTask InvokeOnSettled(TData data, Exception? error, TVariables variables, TContext context) =>
            Options.OnSettled is { } onSettled ? onSettled(data, error, variables, context) : VelvetTask.CompletedTask;
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
        // The node roots the slot holds that a recycle sweep must not return to the pool — see
        // HookSlotRecycleProbe: the states it holds or staged, and the nodes its queued updates carry or computed,
        // since the render that reaches a skipped update emits its node.
        public abstract void VisitRecycleRoots(Action<object> visitRoot);

        public abstract void Commit();

        // Puts back what the committed state was when the render that last committed began, for a render whose
        // output nothing shows, so the updates it folded are folded again by the render that retries.
        public abstract void UndoCommit();

        // Whether a render at lane would skip an update still queued here that asked for a render.
        public abstract bool HoldsUpdateBeyond(FiberUpdatePriority lane);
    }

    // An update a setter queued. Lane is the one the setter ran on. EagerState is the result the setter computed
    // at the call, which a replay uses rather than running the reducer again. An update that bailed out eagerly is
    // queued unscheduled, so a rebase still sees it and no render is asked for.
    internal sealed class HookUpdate<TState, TAction>
    {
        public TAction Action = default!;
        public FiberUpdatePriority Lane;
        public bool HasEagerState;
        public TState EagerState = default!;
        public bool Scheduled;

        // The copy a rebase keeps of an update that was applied after one that was skipped. Urgent is never
        // skipped by a render, so the update is never taken back out of the state.
        internal HookUpdate<TState, TAction> Rebased() => new()
        {
            Action = Action,
            Lane = FiberUpdatePriority.Urgent,
            HasEagerState = HasEagerState,
            EagerState = EagerState,
            Scheduled = Scheduled,
        };
    }

    // The state a UseState or UseReducer hook holds, as React keeps it: Value is the committed state, and a
    // render folds the queued updates over BaseState, skipping those whose lane it does not render. The
    // BaseQueue keeps every update from the first skipped one on, in insertion order, so the render that
    // reaches the skipped lane replays them over the state they were skipped from.
    internal abstract class HookUpdateQueueSlot<TState, TAction> : HookStateSlot
    {
        public TState Value;
        public Func<TState, TAction, TState> Reducer;

        private TState _baseState;
        private List<HookUpdate<TState, TAction>>? _baseQueue;
        private List<HookUpdate<TState, TAction>>? _pending;

        // What the last Process computed, adopted by Commit. A render that never commits leaves the committed
        // fields as they were, so the next render folds the same updates again. Process only moves these away
        // from the committed fields while updates are queued, which only Commit empties, so a Commit with no
        // Process before it adopts what is already committed.
        private TState _nextValue;
        private TState _nextBaseState;
        private List<HookUpdate<TState, TAction>>? _nextBaseQueue;

        // The committed fields as the last Process found them, which UndoCommit puts back.
        private TState _beforeValue;
        private TState _beforeBaseState;
        private List<HookUpdate<TState, TAction>>? _beforeBaseQueue;

        // React's useState computes an update's result at the call while nothing is scheduled and bails out of
        // rendering on an equal one; its useReducer dispatch never does either.
        private readonly bool _computesAtTheCall;

        private static readonly bool s_canHoldNodes = !typeof(TState).IsValueType;

        protected HookUpdateQueueSlot(TState seed, Func<TState, TAction, TState> reducer, bool computesAtTheCall)
        {
            Value = _baseState = _nextValue = _nextBaseState = _beforeValue = _beforeBaseState = seed;
            Reducer = reducer;
            _computesAtTheCall = computesAtTheCall;
        }

        // Queues action at lane and returns whether a render has to run for it.
        internal bool Enqueue(TAction action, FiberUpdatePriority lane)
        {
            var update = new HookUpdate<TState, TAction> { Action = action, Lane = lane, Scheduled = true };
            if (_computesAtTheCall && !HoldsScheduledUpdate())
            {
                update.EagerState = Reducer(Value, action);
                update.HasEagerState = true;
                update.Scheduled = !ObjectIs.AreEqual(Value, update.EagerState);
            }
            // Everything queued is an update that bailed out and left the state as it was, so one more
            // replaces them.
            if (!update.Scheduled) _pending?.Clear();
            (_pending ??= new List<HookUpdate<TState, TAction>>()).Add(update);
            return update.Scheduled;
        }

        // The state a render at renderLane shows. A skipped update asks the fiber for a render at its own lane,
        // so the render that reaches it is owed whichever path ran this one.
        internal TState Process(ComponentFiber fiber, FiberUpdatePriority renderLane)
        {
            if (_pending != null)
            {
                (_baseQueue ??= new List<HookUpdate<TState, TAction>>()).AddRange(_pending);
                _pending.Clear();
            }
            _beforeValue = Value;
            _beforeBaseState = _baseState;
            _beforeBaseQueue = _baseQueue;
            if (_baseQueue == null || _baseQueue.Count == 0) return Value;

            var state = _baseState;
            var newBaseState = state;
            List<HookUpdate<TState, TAction>>? newBaseQueue = null;
            foreach (var update in _baseQueue)
            {
                if (!FiberLane.Renders(renderLane, update.Lane))
                {
                    if (newBaseQueue == null)
                    {
                        newBaseQueue = new List<HookUpdate<TState, TAction>>();
                        newBaseState = state;
                    }
                    newBaseQueue.Add(update);
                    FiberWorkLoop.ScheduleRerender(fiber, update.Lane);
                    continue;
                }
                newBaseQueue?.Add(update.Rebased());
                state = update.HasEagerState ? update.EagerState : Reducer(state, update.Action);
            }
            if (newBaseQueue == null) newBaseState = state;

            _nextValue = state;
            _nextBaseState = newBaseState;
            _nextBaseQueue = newBaseQueue;
            return state;
        }

        public override void Commit()
        {
            Value = _nextValue;
            _baseState = _nextBaseState;
            _baseQueue = _nextBaseQueue;
        }

        public override void UndoCommit()
        {
            Value = _beforeValue;
            _baseState = _beforeBaseState;
            _baseQueue = _beforeBaseQueue;
        }

        private bool HoldsScheduledUpdate() => AnyScheduled(_baseQueue) || AnyScheduled(_pending);

        private static bool AnyScheduled(List<HookUpdate<TState, TAction>>? updates)
        {
            if (updates == null) return false;
            foreach (var update in updates)
            {
                if (update.Scheduled) return true;
            }
            return false;
        }

        public override bool HoldsUpdateBeyond(FiberUpdatePriority lane)
            => AnyBeyond(_baseQueue, lane) || AnyBeyond(_pending, lane);

        private static bool AnyBeyond(List<HookUpdate<TState, TAction>>? updates, FiberUpdatePriority lane)
        {
            if (updates == null) return false;
            foreach (var update in updates)
            {
                if (update.Scheduled && !FiberLane.Renders(lane, update.Lane)) return true;
            }
            return false;
        }

        // The node an action carries, for the actions that can carry one.
        protected virtual object? Payload(TAction action) => action;

        public override void VisitRecycleRoots(Action<object> visitRoot)
        {
            // MUTANT_SURVIVES(equivalent): a value-type state holds no node, so what the probes below would read through it marks nothing a render emits; the guard only skips boxing it.
            if (!s_canHoldNodes) return;
            Visit(visitRoot, Value, _baseState);
            VisitQueued(visitRoot, _baseQueue);
            VisitQueued(visitRoot, _pending);
            // Between a Process and the Commit that adopts it, which a sweep reaches while a Suspense holds the
            // commit of a render. Otherwise the staged fields are the committed ones, visited above.
            // MUTANT_SURVIVES(equivalent): visiting the staged fields when they are the committed ones marks the same roots a second time.
            if (ReferenceEquals(_nextBaseQueue, _baseQueue)) return;
            // The staged queue holds only updates _baseQueue still holds, or copies of them, until the Commit
            // adopts it, so it needs no visit of its own; the staged states can be a result no queued update names.
            Visit(visitRoot, _nextValue, _nextBaseState);
        }

        private void VisitQueued(Action<object> visitRoot, List<HookUpdate<TState, TAction>>? updates)
        {
            if (updates == null) return;
            foreach (var update in updates)
            {
                Visit(visitRoot, Payload(update.Action), update.EagerState);
            }
        }

        private static void Visit(Action<object> visitRoot, object? first, object? second)
        {
            VisitOne(visitRoot, first);
            VisitOne(visitRoot, second);
        }

        private static void VisitOne(Action<object> visitRoot, object? candidate)
        {
            var root = HookSlotRecycleProbe.Probe(candidate);
            if (root != null) visitRoot(root);
        }
    }

    // What UseState queues: the replacement value, or the function that computes it from the state before.
    internal readonly struct StateAction<T>
    {
        public readonly T Value;
        public readonly Func<T, T>? Updater;

        public StateAction(T value, Func<T, T>? updater)
        {
            Value = value;
            Updater = updater;
        }
    }

    internal sealed class HookStateSlot<T> : HookUpdateQueueSlot<T, StateAction<T>>
    {
        public StateUpdater<T> Setter = default!;

        private static readonly Func<T, StateAction<T>, T> s_apply
            = static (state, action) => action.Updater != null ? action.Updater(state) : action.Value;

        public HookStateSlot(T seed) : base(seed, s_apply, computesAtTheCall: true)
        {
        }

        protected override object? Payload(StateAction<T> action) => action.Value;
    }

    internal sealed class ReducerSlot<TState, TAction> : HookUpdateQueueSlot<TState, TAction>
    {
        public Action<TAction> Dispatch = null!;

        public ReducerSlot(TState seed, Func<TState, TAction, TState> reducer)
            : base(seed, reducer, computesAtTheCall: false)
        {
        }
    }

    internal abstract class HookStoreSlot : IDisposable
    {
        public abstract void Dispose();

        // Adopts what the settled render staged, from FiberBeginWork.CommitSettledEffects. A render whose body throws
        // or suspends, an attempt a render-phase update re-runs, and a render a Suspense gives up never reach it, so
        // nothing they read reaches a notification or the passive effects an earlier commit left pending.
        public abstract void Commit();
    }

    internal sealed class HookStoreSlot<TStore, TSel> : HookStoreSlot
    {
        public Store<TStore> Store = null!;
        // What the last commit rendered, which a notification compares against.
        public Func<TStore, TSel> Selector = null!;
        public IEqualityComparer<TSel> Comparer = null!;
        public TSel LastValue = default!;
        public Func<TStore, TSel> RenderedSelector = null!;
        public IEqualityComparer<TSel> RenderedComparer = null!;
        public TSel RenderedValue = default!;
        public IDisposable? Subscription;
        public HookEffectSlot SubscribeEffect = null!;
        public HookEffectSlot? CatchUpEffect;

        public void Render(Func<TStore, TSel> selector, IEqualityComparer<TSel> comparer, TSel value)
        {
            RenderedSelector = selector;
            RenderedComparer = comparer;
            RenderedValue = value;
        }

        public override void Commit()
        {
            Selector = RenderedSelector;
            Comparer = RenderedComparer;
            LastValue = RenderedValue;
        }

        public override void Dispose()
        {
            Subscription?.Dispose();
            Subscription = null;
        }
    }

    // Value and GetSnapshot are what the passive effects of the last commit promoted, as the store instance of
    // React's useSyncExternalStore holds them: a notification compares them, never what a render that did not
    // commit read. The Rendered* fields are the latest render's, which Commit hands to the Committed* fields the
    // passive effects promote from.
    internal sealed class HookExternalStoreSlot<T> : HookStoreSlot
    {
        public ComponentFiber Fiber = null!;
        public Func<T> GetSnapshot = null!;
        public T Value = default!;
        // The subscribe the live subscription was made with. Null while unsubscribed, so a call that threw is
        // made again by the next render.
        public Func<Action, Action>? Subscribe;
        public Func<T> RenderedGetSnapshot = null!;
        public T RenderedValue = default!;
        public Func<Action, Action> RenderedSubscribe = null!;
        public Func<T> CommittedGetSnapshot = null!;
        public T CommittedValue = default!;
        public Func<Action, Action> CommittedSubscribe = null!;
#if UNITY_EDITOR
        public bool ReportedUncachedSnapshot;
#endif
        private HookEffectSlot? _subscribeEffect;
        private HookEffectSlot? _checkEffect;
        private Action? _unsubscribe;
        private Action? _release;
        private bool _promoted;

        // Queued on the fiber's pending passive effects, which a render that fails or is discarded truncates,
        // so such a render subscribes nothing. A commit that subscribes queues no check: the subscription
        // compares the snapshot itself once it is made, which a check an earlier commit queued can run ahead of.
        public void Render(
            List<HookEffectSlot> pendingEffects, Func<Action, Action> subscribe, Func<T> getSnapshot, T value)
        {
            RenderedSubscribe = subscribe;
            RenderedGetSnapshot = getSnapshot;
            RenderedValue = value;

            if (Subscribe == null || !Subscribe.Equals(subscribe))
            {
                _subscribeEffect ??= new HookEffectSlot { EffectFactory = RunSubscribe };
                if (!pendingEffects.Contains(_subscribeEffect)) pendingEffects.Add(_subscribeEffect);
            }
            else if (!_promoted || !ObjectIs.AreEqual(Value, value) || !GetSnapshot.Equals(getSnapshot))
            {
                _checkEffect ??= new HookEffectSlot { EffectFactory = RunCheck };
                if (!pendingEffects.Contains(_checkEffect)) pendingEffects.Add(_checkEffect);
            }
        }

        // An effect a commit left pending can run after a later render of the fiber that did not commit, so
        // the effects read only what a settle handed over.
        public override void Commit()
        {
            CommittedSubscribe = RenderedSubscribe;
            CommittedGetSnapshot = RenderedGetSnapshot;
            CommittedValue = RenderedValue;
        }

        private void Promote()
        {
            Value = CommittedValue;
            GetSnapshot = CommittedGetSnapshot;
            _promoted = true;
        }

        // The store notified no one between the render and this effect, so the comparison after subscribing is
        // what finds a change made then.
        private Action? RunSubscribe()
        {
            Promote();
            var subscribe = CommittedSubscribe;
            _unsubscribe = subscribe(OnStoreChange);
            Subscribe = subscribe;
            RunCheck();
            return _release ??= Release;
        }

        // Uncontained: a throw from the unsubscribe reaches the error boundary through the effect executor, as
        // a throwing cleanup of UseEffect does.
        private void Release() => Detach()?.Invoke();

        private Action? RunCheck()
        {
            Promote();
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
            if (SnapshotChanged()) FiberWorkLoop.RequestExternalStoreRender(Fiber);
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
