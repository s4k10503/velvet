#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// A mutation's state, TanStack Query's <c>MutationState</c>: a snapshot replaced whole by each change, so two
    /// readings are the same instance until the mutation moves.
    /// </summary>
    public sealed record MutationState
    {
        internal MutationState()
        {
        }

        internal static MutationState Idle { get; } = new();

        /// <summary>What the options' <c>OnMutate</c> returned, boxed; null without one.</summary>
        public object? Context { get; internal init; }

        /// <summary>The result, under <see cref="MutationStatus.Success"/>; null otherwise.</summary>
        public object? Data { get; internal init; }

        /// <summary>The failure, under <see cref="MutationStatus.Error"/>; null otherwise.</summary>
        public Exception? Error { get; internal init; }

        /// <summary>How many attempts have failed, as <see cref="MutationResult{TVariables, TData}.FailureCount"/>.</summary>
        public int FailureCount { get; internal init; }

        /// <summary>The latest failed attempt's exception, as <see cref="MutationResult{TVariables, TData}.FailureReason"/>.</summary>
        public Exception? FailureReason { get; internal init; }

        /// <summary>Whether the mutation waits, for the connection or for an earlier mutation of its scope.</summary>
        public bool IsPaused { get; internal init; }

        /// <summary>The mutation's lifecycle status.</summary>
        public MutationStatus Status { get; internal init; }

        /// <summary>The variables the mutation was started with.</summary>
        public object? Variables { get; internal init; }

        /// <summary>When the mutation was started, on the client's clock; zero before it starts.</summary>
        public TimeSpan SubmittedAt { get; internal init; }
    }

    /// <summary>What changed a mutation's <see cref="MutationState"/>: TanStack Query's mutation action types.</summary>
    public enum MutationActionType
    {
        /// <summary>An attempt failed and another follows.</summary>
        Failed,
        /// <summary>The mutation started, or its <c>OnMutate</c> returned a context.</summary>
        Pending,
        /// <summary>The mutation succeeded.</summary>
        Success,
        /// <summary>The mutation failed for good.</summary>
        Error,
        /// <summary>The mutation started waiting.</summary>
        Pause,
        /// <summary>The mutation stopped waiting.</summary>
        Continue,
    }

    /// <summary>The kind of a <see cref="MutationCacheEvent"/>, TanStack Query's mutation cache event types.</summary>
    public enum MutationCacheEventType
    {
        /// <summary>A mutation was added to the cache.</summary>
        Added,
        /// <summary>A mutation was removed from the cache.</summary>
        Removed,
        /// <summary>A mutation's state changed; <see cref="MutationCacheEvent.Action"/> says how.</summary>
        Updated,
        /// <summary>A <see cref="Hooks.UseMutation{TVariables, TData}"/> handle started showing the mutation.</summary>
        ObserverAdded,
        /// <summary>A handle stopped showing the mutation.</summary>
        ObserverRemoved,
        /// <summary>
        /// A handle committed options unequal to those it committed before; <see cref="MutationCacheEvent.Mutation"/>
        /// is the mutation it shows, null when it shows none.
        /// </summary>
        ObserverOptionsUpdated,
    }

    /// <summary>What a <see cref="MutationCache.Subscribe"/> listener is handed.</summary>
    public sealed class MutationCacheEvent
    {
        internal MutationCacheEvent(
            MutationCacheEventType type,
            Mutation? mutation,
            MutationActionType? action = null,
            MutationObserver? observer = null)
        {
            Type = type;
            Mutation = mutation;
            Action = action;
            Observer = observer;
        }

        /// <summary>What happened.</summary>
        public MutationCacheEventType Type { get; }

        /// <summary>The mutation it happened to; null only for an <see cref="MutationCacheEventType.ObserverOptionsUpdated"/>
        /// whose handle shows no mutation.</summary>
        public Mutation? Mutation { get; }

        /// <summary>How the state changed, for <see cref="MutationCacheEventType.Updated"/>; null otherwise.</summary>
        public MutationActionType? Action { get; }

        /// <summary>The observer that was added, removed or handed options, for the three observer events; null
        /// otherwise.</summary>
        public MutationObserver? Observer { get; }
    }

    /// <summary>
    /// Selects mutations in a <see cref="MutationCache"/>, TanStack Query's mutation filters. Every property that is
    /// set must match.
    /// </summary>
    public sealed record MutationFilters
    {
        /// <summary>
        /// Selects the mutations whose <see cref="MutationOptionsBase.MutationKey"/> this key leads, or equals under
        /// <see cref="Exact"/>, as a <see cref="QueryFilters.QueryKey"/> matches. A mutation without a key is not
        /// selected. Null selects every mutation.
        /// </summary>
        public QueryKey? MutationKey { get; init; }

        /// <summary>
        /// True selects only the mutations whose key equals <see cref="MutationKey"/>. Null is false, except for
        /// <see cref="MutationCache.Find"/>, where it is true.
        /// </summary>
        public bool? Exact { get; init; }

        /// <summary>Selects by <see cref="MutationState.Status"/>; null selects every status.</summary>
        public MutationStatus? Status { get; init; }

        /// <summary>Accepts or declines each mutation the other properties select.</summary>
        public Func<Mutation, bool>? Predicate { get; init; }

        // TanStack's matchMutation.
        internal static bool Match(MutationFilters? filters, Mutation mutation)
        {
            if (filters == null) return true;
            if (filters.MutationKey != null)
            {
                var key = mutation.Options.MutationKey;
                if (key == null) return false;
                if (filters.Exact == true ? !key.Equals(filters.MutationKey) : !key.Matches(filters.MutationKey)) return false;
            }
            if (filters.Status is { } status && mutation.State.Status != status) return false;
            return filters.Predicate == null || filters.Predicate(mutation);
        }
    }

    /// <summary>
    /// One call of a mutation, TanStack Query's <c>Mutation</c>: each <c>Mutate</c> / <c>MutateAsync</c> of a
    /// <see cref="Hooks.UseMutation{TVariables, TData}"/> handle builds one in its client's
    /// <see cref="MutationCache"/>, which removes it as <see cref="MutationOptionsBase.GcTime"/> says.
    /// </summary>
    public sealed class Mutation
    {
        private static readonly TimeSpan s_defaultGcTime = TimeSpan.FromMinutes(5);

        private readonly MutationCache _cache;
        private readonly Func<TimeSpan> _clock;
        private readonly List<MutationObserver> _observers = new();
        private VelvetTaskCompletionSource? _continue;

        internal Mutation(MutationCache cache, int mutationId, MutationOptionsBase options, Func<TimeSpan> clock)
        {
            _cache = cache;
            _clock = clock;
            MutationId = mutationId;
            SetOptions(options);
        }

        /// <summary>The mutation's number in its cache, counting from one.</summary>
        public int MutationId { get; }

        /// <summary>The options record the hook was given; a later commit of a pending mutation's handle replaces
        /// it.</summary>
        public MutationOptionsBase Options { get; private set; } = null!;

        /// <summary>The options' <see cref="MutationOptionsBase.Meta"/>.</summary>
        public IReadOnlyDictionary<string, object?>? Meta => Options.Meta;

        /// <summary>The mutation's state as it stands.</summary>
        public MutationState State { get; private set; } = MutationState.Idle;

        // The longest gcTime any of its options asked for, as TanStack's updateGcTime keeps it.
        internal TimeSpan GcTime { get; private set; }

        internal TimeSpan? GcDeadline { get; set; }

        internal TimeSpan Now => _clock();

        internal void SetOptions(MutationOptionsBase options)
        {
            Options = options;
            var requested = options.GcTime ?? s_defaultGcTime;
            if (requested > GcTime) GcTime = requested;
        }

        // The slot-typed options the call's function and callbacks are read from: the record the observer had
        // committed when the call was built, replaced only while the call is pending and the handle still shows it,
        // as v5's mutation keeps the options object it was built with.
        internal object Invocation { get; set; } = null!;

        // Completes when the call's function has settled, however it settled, as v5's retryer promise does, for
        // ResumePausedMutations to wait on. A call that failed before its function was reached never completes it.
        internal VelvetTaskCompletionSource Settled { get; } = new();

        // TanStack's addObserver: an observer already watching is not added again.
        internal void AddObserver(MutationObserver observer)
        {
            if (_observers.Contains(observer)) return;
            _observers.Add(observer);
            _cache.ClearGc(this);
            _cache.Notify(new MutationCacheEvent(MutationCacheEventType.ObserverAdded, this, observer: observer));
        }

        // TanStack's removeObserver, which reschedules the removal and announces it whether or not the observer was
        // watching.
        internal void RemoveObserver(MutationObserver observer)
        {
            _observers.Remove(observer);
            _cache.ScheduleGc(this);
            _cache.Notify(new MutationCacheEvent(MutationCacheEventType.ObserverRemoved, this, observer: observer));
        }

        // TanStack's optionalRemove, which its gc timeout calls.
        internal void OptionalRemove()
        {
            if (_observers.Count > 0) return;
            if (State.Status == MutationStatus.Pending) _cache.ScheduleGc(this);
            else _cache.Remove(this);
        }

        // A paused mutation waits on this: it completes on the next frame, which is when the conditions are read
        // again, or as soon as the cache continues the mutation, whichever comes first.
        internal VelvetTask WaitToContinue()
        {
            var source = new VelvetTaskCompletionSource();
            _continue = source;
            CompleteOnNextFrame(source).Forget();
            return source.Task;
        }

        // TanStack's continue: wakes a paused run and hands back its settling.
        internal VelvetTask Continue()
        {
            _continue?.TrySetResult();
            return Settled.Task;
        }

        private static async VelvetTask CompleteOnNextFrame(VelvetTaskCompletionSource source)
        {
            await VelvetTask.Yield();
            source.TrySetResult();
        }

        internal void DispatchPending(object? variables, object? context, bool isPaused) => Dispatch(new MutationState
        {
            Context = context,
            IsPaused = isPaused,
            Status = MutationStatus.Pending,
            Variables = variables,
            SubmittedAt = Now,
        }, MutationActionType.Pending);

        internal void DispatchFailed(Exception error)
            => Dispatch(State with { FailureCount = State.FailureCount + 1, FailureReason = error }, MutationActionType.Failed);

        internal void DispatchPause() => Dispatch(State with { IsPaused = true }, MutationActionType.Pause);

        internal void DispatchContinue() => Dispatch(State with { IsPaused = false }, MutationActionType.Continue);

        // Settling is split from its announcement: the handle and the call's own callbacks see the outcome before
        // the cache's listeners hear of it, as TanStack's dispatch tells the observers first.
        internal void SettleSuccess(object? data) => State = State with
        {
            Data = data,
            Error = null,
            FailureCount = 0,
            FailureReason = null,
            IsPaused = false,
            Status = MutationStatus.Success,
        };

        internal void SettleError(Exception error) => State = State with
        {
            Data = null,
            Error = error,
            FailureCount = State.FailureCount + 1,
            FailureReason = error,
            IsPaused = false,
            Status = MutationStatus.Error,
        };

        internal void Announce(MutationActionType action)
            => _cache.Notify(new MutationCacheEvent(MutationCacheEventType.Updated, this, action));

        private void Dispatch(MutationState next, MutationActionType action)
        {
            State = next;
            Announce(action);
        }
    }

    /// <summary>
    /// TanStack Query's <c>MutationCache</c>: the mutations a <see cref="QueryClient"/> has run, the callbacks that
    /// run for every one of them, and the events a listener can follow. Hand one to
    /// <see cref="QueryClientOptions.MutationCache"/>; a client given none makes its own.
    /// </summary>
    /// <remarks>
    /// Each callback runs before the mutation options' callback of the same name and is waited for, as the options'
    /// are. A throw from <see cref="OnMutate"/> or <see cref="OnSuccess"/>, or from <see cref="OnSettled"/> after a
    /// success, fails the mutation as the options' would; one from <see cref="OnError"/>, or from
    /// <see cref="OnSettled"/> after a failure, is reported and costs none of the later callbacks. Main thread only,
    /// like the rest of Velvet.
    /// </remarks>
    public sealed class MutationCache
    {
        // Insertion-ordered, as TanStack's Set iterates.
        private readonly List<Mutation> _mutations = new();
        private readonly Dictionary<string, List<Mutation>> _scopes = new(StringComparer.Ordinal);
        private readonly CacheListeners<MutationCacheEvent> _listeners = new();
        private readonly List<Mutation> _collectable = new();
        private int _mutationId;
        private bool _collecting;

        /// <summary>Creates an empty cache.</summary>
        public MutationCache()
        {
            StoreSubscribe = onStoreChange => Subscribe(_ => onStoreChange());
        }

        /// <summary>Runs as each mutation starts, with its variables, the mutation and the call's
        /// <see cref="MutationFunctionContext"/>, before the options' <c>OnMutate</c>. What it returns is waited for
        /// and is not a context.</summary>
        public Func<object?, Mutation, MutationFunctionContext, VelvetTask>? OnMutate { get; init; }

        /// <summary>Runs when a mutation succeeds, with its data, variables, the options' <c>OnMutate</c> result, the
        /// mutation and the call's <see cref="MutationFunctionContext"/>.</summary>
        public Func<object?, object?, object?, Mutation, MutationFunctionContext, VelvetTask>? OnSuccess { get; init; }

        /// <summary>Runs when a mutation fails, with the exception, its variables, the <c>OnMutate</c> result, the
        /// mutation and the call's <see cref="MutationFunctionContext"/>.</summary>
        public Func<Exception, object?, object?, Mutation, MutationFunctionContext, VelvetTask>? OnError { get; init; }

        /// <summary>Runs after <see cref="OnSuccess"/> or <see cref="OnError"/> and the options' callback of that
        /// outcome, with the data or null, the exception or null, the variables, the <c>OnMutate</c> result, the
        /// mutation and the call's <see cref="MutationFunctionContext"/>.</summary>
        public Func<object?, Exception?, object?, object?, Mutation, MutationFunctionContext, VelvetTask>? OnSettled { get; init; }

        // One delegate per cache, so a UseSyncExternalStore over it subscribes again only when the cache changes.
        internal Func<Action, Action> StoreSubscribe { get; }

        /// <summary>
        /// Calls <paramref name="listener"/> with every event from now on, until the returned action is called. A
        /// listener already subscribed is not added twice.
        /// </summary>
        /// <returns>The action that unsubscribes the listener.</returns>
        public Action Subscribe(Action<MutationCacheEvent> listener) => _listeners.Subscribe(listener);

        /// <summary>Every mutation in the cache, in the order they were added.</summary>
        public IReadOnlyList<Mutation> GetAll() => _mutations.ToArray();

        /// <summary>The first mutation <paramref name="filters"/> selects, with <see cref="MutationFilters.Exact"/>
        /// true unless it is set; null when none is.</summary>
        public Mutation? Find(MutationFilters filters)
        {
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            var exact = filters.Exact.HasValue ? filters : filters with { Exact = true };
            foreach (var mutation in GetAll())
            {
                if (MutationFilters.Match(exact, mutation)) return mutation;
            }
            return null;
        }

        /// <summary>The mutations <paramref name="filters"/> selects, in the order they were added; null selects
        /// every one.</summary>
        public IReadOnlyList<Mutation> FindAll(MutationFilters? filters = null)
        {
            var found = new List<Mutation>();
            foreach (var mutation in GetAll())
            {
                if (MutationFilters.Match(filters, mutation)) found.Add(mutation);
            }
            return found;
        }

        /// <summary>
        /// Removes <paramref name="mutation"/> and announces it, whether or not it was still in the cache, as
        /// TanStack Query's <c>remove</c> does. A removed mutation that is still running runs on.
        /// </summary>
        public void Remove(Mutation mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            if (_mutations.Remove(mutation))
            {
                ClearGc(mutation);
                if (mutation.Options.Scope?.Id is { } scope && _scopes.TryGetValue(scope, out var scoped))
                {
                    scoped.Remove(mutation);
                    if (scoped.Count == 0) _scopes.Remove(scope);
                }
            }
            Notify(new MutationCacheEvent(MutationCacheEventType.Removed, mutation));
        }

        /// <summary>
        /// Wakes every paused mutation, TanStack Query's <c>resumePausedMutations</c>. One that its network mode or
        /// its scope still holds, or one waiting to retry while the application is not visible, stays paused.
        /// </summary>
        /// <returns>A task that completes once the function of every mutation it woke has settled, however it
        /// settled.</returns>
        public VelvetTask ResumePausedMutations()
        {
            var runs = new List<VelvetTask>();
            foreach (var mutation in GetAll())
            {
                if (mutation.State.IsPaused) runs.Add(mutation.Continue());
            }
            return SettleAll(runs);
        }

        private static async VelvetTask SettleAll(List<VelvetTask> runs)
        {
            foreach (var run in runs)
            {
                await run;
            }
        }

        /// <summary>Removes every mutation and announces each removal.</summary>
        public void Clear()
        {
            var mutations = GetAll();
            _mutations.Clear();
            _scopes.Clear();
            _collectable.Clear();
            foreach (var mutation in mutations)
            {
                mutation.GcDeadline = null;
                Notify(new MutationCacheEvent(MutationCacheEventType.Removed, mutation));
            }
        }

        // TanStack's build: the mutation is added, and its removal scheduled until an observer takes it.
        internal Mutation Build(MutationOptionsBase options, Func<TimeSpan> clock)
        {
            var mutation = new Mutation(this, ++_mutationId, options, clock);
            ScheduleGc(mutation);
            _mutations.Add(mutation);
            if (options.Scope?.Id is { } scope)
            {
                if (_scopes.TryGetValue(scope, out var scoped)) scoped.Add(mutation);
                else _scopes.Add(scope, new List<Mutation> { mutation });
            }
            Notify(new MutationCacheEvent(MutationCacheEventType.Added, mutation));
            return mutation;
        }

        // A scoped mutation runs while no other mutation of its scope is pending ahead of it.
        internal bool CanRun(Mutation mutation)
        {
            if (mutation.Options.Scope?.Id is not { } scope || !_scopes.TryGetValue(scope, out var scoped)) return true;
            foreach (var other in scoped)
            {
                if (other.State.Status == MutationStatus.Pending) return ReferenceEquals(other, mutation);
            }
            return true;
        }

        // Continues the first other paused mutation of the scope, which the settled one may have been holding.
        internal void RunNext(Mutation mutation)
        {
            if (mutation.Options.Scope?.Id is not { } scope || !_scopes.TryGetValue(scope, out var scoped)) return;
            foreach (var other in scoped)
            {
                if (!ReferenceEquals(other, mutation) && other.State.IsPaused)
                {
                    other.Continue();
                    return;
                }
            }
        }

        internal void Notify(MutationCacheEvent cacheEvent) => _listeners.Notify(cacheEvent);

        // TanStack's scheduleGc, read once a frame on the mutation's clock as QueryClient's entries are: the removal
        // lands on the first frame whose reading is at or past the deadline. An infinite gcTime sets none.
        internal void ScheduleGc(Mutation mutation)
        {
            ClearGc(mutation);
            if (mutation.GcTime == TimeSpan.MaxValue) return;
            var now = mutation.Now;
            mutation.GcDeadline = mutation.GcTime >= TimeSpan.MaxValue - now ? TimeSpan.MaxValue : now + mutation.GcTime;
            _collectable.Add(mutation);
            if (_collecting) return;
            _collecting = true;
            CollectOnFrames(new WeakReference<MutationCache>(this)).Forget();
        }

        internal void ClearGc(Mutation mutation)
        {
            mutation.GcDeadline = null;
            _collectable.Remove(mutation);
        }

        private bool Poll()
        {
            foreach (var mutation in _collectable.ToArray())
            {
                if (mutation.GcDeadline is not { } deadline || mutation.Now < deadline) continue;
                ClearGc(mutation);
                mutation.OptionalRemove();
            }
            if (_collectable.Count == 0) _collecting = false;
            return _collecting;
        }

        // The loop holds the cache weakly, so a cache the application dropped is not kept alive by a mutation
        // waiting to be collected.
        private static async VelvetTask CollectOnFrames(WeakReference<MutationCache> cache)
        {
            do
            {
                await VelvetTask.Yield();
            }
            while (PollOf(cache));
        }

        private static bool PollOf(WeakReference<MutationCache> cache)
            => cache.TryGetTarget(out var target) && target.Poll();
    }

    /// <summary>
    /// What <see cref="Hooks.UseMutationState{TResult}"/> reads, TanStack Query's <c>useMutationState</c> options.
    /// </summary>
    /// <typeparam name="TResult">What each mutation selects to.</typeparam>
    public sealed record MutationStateOptions<TResult>
    {
        /// <summary>The mutations to read; null selects every one.</summary>
        public MutationFilters? Filters { get; init; }

        /// <summary>What each mutation selects to; null selects its <see cref="Mutation.State"/>.</summary>
        public Func<Mutation, TResult>? Select { get; init; }
    }

    // TanStack's createSnapshotFactory: one per hook, holding the list it last returned. Each render makes a getter
    // for that render's options, and a getter selects again only when the selected mutations or their states are no
    // longer the instances it last read.
    internal sealed class MutationStateSnapshots<TResult>
    {
        private List<TResult>? _previous;

        internal Func<IReadOnlyList<TResult>> Getter(MutationCache cache, MutationStateOptions<TResult>? options)
        {
            var inputs = Array.Empty<object>();
            IReadOnlyList<TResult>? snapshot = null;
            return () =>
            {
                var mutations = cache.FindAll(options?.Filters);
                if (snapshot != null && SameInputs(inputs, mutations)) return snapshot;
                inputs = InputsOf(mutations);
                var next = new List<TResult>(mutations.Count);
                foreach (var mutation in mutations)
                {
                    next.Add(options?.Select is { } select ? select(mutation) : (TResult)(object)mutation.State);
                }
                _previous = QueryStructuralSharing.Replace(_previous, next);
                snapshot = _previous;
                return snapshot;
            };
        }

        private static object[] InputsOf(IReadOnlyList<Mutation> mutations)
        {
            var inputs = new object[mutations.Count * 2];
            for (var i = 0; i < mutations.Count; i++)
            {
                inputs[2 * i] = mutations[i];
                inputs[2 * i + 1] = mutations[i].State;
            }
            return inputs;
        }

        private static bool SameInputs(object[] inputs, IReadOnlyList<Mutation> mutations)
        {
            if (inputs.Length != mutations.Count * 2) return false;
            for (var i = 0; i < mutations.Count; i++)
            {
                if (!ReferenceEquals(inputs[2 * i], mutations[i]) || !ReferenceEquals(inputs[2 * i + 1], mutations[i].State)) return false;
            }
            return true;
        }
    }
}
