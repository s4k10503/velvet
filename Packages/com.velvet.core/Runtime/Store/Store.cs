using System;
using System.Collections.Generic;
using System.Threading;

namespace Velvet
{
    /// <summary>
    /// State container: immutable state plus collocated actions in a single class, with
    /// synchronous subscribers notified on every change.
    /// </summary>
    /// <remarks>
    /// <para>Equivalent to a Zustand store for users migrating from Zustand.</para>
    /// Threading: a store is single-threaded. All reads, mutations (<see cref="SetState"/> /
    /// <see cref="Mutate"/>), and subscription changes must occur on the Unity main thread
    /// (PlayerLoop). The store performs no internal locking; it assumes a single-threaded model.
    /// <para>
    /// Re-entrancy: listeners passed to <see cref="Subscribe(Action{TState}, bool)"/> /
    /// <see cref="Select{T}(Func{TState, T}, Action{T, T}, IEqualityComparer{T}, bool)"/> must not
    /// synchronously mutate the store. Notification is synchronous and not serialized,
    /// so a re-entrant mutation interleaves delivery and can reorder the (current, previous) pairs seen
    /// by other subscribers. Defer such updates (e.g. via a scheduler / next frame) instead of mutating
    /// during notification.
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">Immutable state record type.</typeparam>
    public abstract class Store<TState> : IStoreWriter<TState>, IDisposable
    {
        #region State

        private readonly StoreStateNotifier<TState> _state;

        /// <summary>
        /// Snapshot of the current state.
        /// </summary>
        public TState Current
            => _state.Value;

        /// <summary>
        /// The state value the store was constructed with.
        /// Useful for distinguishing "untouched" from
        /// "user-modified" without separately tracking a dirty flag, and for diffing against
        /// the persisted/seed value.
        /// </summary>
        public TState InitialState { get; }

        #endregion

        #region Infrastructure

        /// <summary>
        /// Logger. null suppresses logs.
        /// </summary>
        protected StoreLogger Logger { get; }

        /// <summary>
        /// CancellationToken for async operations.
        /// </summary>
        protected CancellationToken CancellationToken
            => _cancellationTokenSource.Token;

        private readonly CancellationTokenSource _cancellationTokenSource;

        #endregion

        #region Lifecycle

        private bool _disposed;

        #endregion

        #region Constructor

        /// <summary>
        /// Retains <paramref name="initial"/> as <see cref="InitialState"/> and seeds the live state with it.
        /// </summary>
        /// <param name="initial">Initial state.</param>
        protected Store(TState initial)
        {
            Logger = StoreLogger.Default;
            InitialState = initial;
            _state = new StoreStateNotifier<TState>(initial);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region State Methods

        /// <summary>
        /// Updates state and returns whether the value actually changed (equality-checked).
        /// Skips updates that would not change the state.
        /// </summary>
        bool IStoreWriter<TState>.SetState(Func<TState, TState> updater)
            => SetState(updater);

        /// <inheritdoc cref="IStoreWriter{TState}.SetState"/>
        protected bool SetState(Func<TState, TState> updater)
            => TryApply(updater, force: false);

        // The single write path through the state cell: guards disposal, applies the updater, and notifies.
        // When force is false an Object.is-unchanged result is suppressed; when true it always notifies.
        // Returns whether a notification was raised.
        //
        // Disposal-order race guard: VContainer / V.Mount cleanup paths can call setState from a UseEffect
        // cleanup that runs after the store has been disposed (e.g. app shutdown where the singleton store is
        // disposed before AppRouterInitializer tears down the mounted tree). A state update on an unmounted
        // component is treated as a no-op, so a setState after disposal is silently ignored rather than throwing.
        //
        // Reference (Object.is) equality is used deliberately: a value-equal but distinct record instance must
        // still notify subscribers. EqualityComparer<T>.Default would invoke record value-equality and suppress
        // the notification, which is not the intended behavior.
        private bool TryApply(Func<TState, TState> updater, bool force)
        {
            if (_disposed) return false;

            var current = _state.Value;
            var next = updater(current);
            if (!force && ObjectIs.AreEqual(next, current))
            {
                return false;
            }

            _state.Notify(next);
            return true;
        }

        /// <summary>
        /// Updates state unconditionally.
        /// Unlike <see cref="SetState"/>, this always raises a notification even when the state is unchanged.
        /// Use this for cases like Reset where subscribers must always be notified.
        /// </summary>
        void IStoreWriter<TState>.Mutate(Func<TState, TState> reducer)
            => Mutate(reducer);

        /// <inheritdoc cref="IStoreWriter{TState}.Mutate"/>
        protected void Mutate(Func<TState, TState> reducer)
            => TryApply(reducer, force: true);

        /// <summary>
        /// Subscribes to state changes.
        /// </summary>
        /// <param name="listener">Invoked on every state mutation. Must not be null.</param>
        /// <param name="fireImmediately">
        /// When <c>true</c>, the listener is invoked synchronously with <see cref="Current"/> before
        /// returning. Default is
        /// <c>false</c> — the listener fires only on subsequent state changes.
        /// </param>
        public IDisposable Subscribe(Action<TState> listener, bool fireImmediately = false)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            // Wrap in a fresh delegate so the same callback subscribed twice yields two distinct,
            // independently-disposable listeners (notifier removal is by reference identity). Subscribe BEFORE the
            // immediate fire so a listener that mutates the store during that fire observes its own mutation;
            // a fire-before-subscribe order would silently drop it.
            var subscription = _state.Subscribe(state => listener(state));
            if (fireImmediately) listener(Current);
            return subscription;
        }

        /// <summary>
        /// Subscribes to state changes with a (current, previous) signature.
        /// </summary>
        /// <param name="listener">Invoked with <c>(currentState, previousState)</c> on every state mutation. Must not be null.</param>
        /// <param name="fireImmediately">
        /// When <c>true</c>, fires the listener synchronously with <c>(Current, Current)</c> before returning
        /// (both arguments equal — there is no previous state at subscribe time). Default is <c>false</c>.
        /// </param>
        public IDisposable Subscribe(Action<TState, TState> listener, bool fireImmediately = false)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var prev = Current;
            // Subscribe before the immediate fire (see the single-arg overload) so a mutation made during the
            // fire is observed; the closure's `prev` tracking stays correct because a reentrant notify updates it.
            var subscription = _state.Subscribe(current =>
            {
                var oldPrev = prev;
                prev = current;
                listener(current, oldPrev);
            });
            if (fireImmediately) listener(Current, Current);
            return subscription;
        }

        /// <summary>
        /// Subscribes to a selected slice with a (currentSlice, previousSlice) signature. The listener
        /// fires only when the selected slice changes under the given comparer.
        /// </summary>
        /// <typeparam name="T">Selected projection type.</typeparam>
        /// <param name="selector">Pure projection from the store snapshot. Must not be null.</param>
        /// <param name="observer">Invoked with <c>(currentSlice, previousSlice)</c> on each change. Must not be null.</param>
        /// <param name="comparer">
        /// Equality comparer that decides whether two consecutive slices are equal. Defaults to
        /// reference (Object.is) equality, the same default used by <see cref="Hooks.UseStore{TStore,TSel}"/>.
        /// Pass <see cref="StoreShallowEqualityComparer.Sequence{TItem}"/> to compare sequence slices
        /// element-by-element instead.
        /// </param>
        /// <param name="fireImmediately">
        /// When <c>true</c>, fires the observer synchronously with <c>(selector(Current), selector(Current))</c>
        /// before returning. Default is <c>false</c>.
        /// </param>
        public IDisposable Select<T>(
            Func<TState, T> selector,
            Action<T, T> observer,
            IEqualityComparer<T>? comparer = null,
            bool fireImmediately = false)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            var cmp = comparer ?? ObjectIsEqualityComparer<T>.Instance;
            var prev = selector(Current);
            // Subscribe before the immediate fire (see the single-arg overload) so a mutation made during the fire
            // is observed through the now-active subscription.
            var subscription = _state.Subscribe(snapshot =>
            {
                var current = selector(snapshot);
                if (cmp.Equals(prev, current)) return;
                var oldPrev = prev;
                prev = current;
                observer(current, oldPrev);
            });
            if (fireImmediately) observer(selector(Current), selector(Current));
            return subscription;
        }

        /// <summary>
        /// Resets state to the initial value. Non-virtual template method that short-circuits
        /// when the store has been disposed; concrete stores implement <see cref="ResetCore"/>.
        /// </summary>
        /// <remarks>
        /// During app shutdown / scene unload, VContainer disposes singletons LIFO, so a child
        /// store can be disposed before a parent store's Reset chain reaches it. The same disposal
        /// race also fires when V.Mount's UseEffect cleanup runs after the store has been disposed.
        /// Centralizing the guard here ensures every <see cref="Store{TState}"/> subclass — including
        /// those whose <see cref="ResetCore"/> touches disposable internal resources — is automatically
        /// protected.
        /// </remarks>
        public void Reset()
        {
            if (_disposed) return;
            ResetCore();
        }

        /// <summary>
        /// Concrete reset logic. Called by <see cref="Reset"/> only when the store is not disposed.
        /// </summary>
        protected abstract void ResetCore();

        #endregion

        #region Dispose

        /// <summary>
        /// Releases resources in the order: CTS cancel → state notifier.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try { _cancellationTokenSource.Cancel(); _cancellationTokenSource.Dispose(); }
            catch (ObjectDisposedException) { }

            try { _state.Dispose(); }
            catch (Exception ex) { Logger.LogError($"[{GetType().Name}] error while disposing State: {ex.Message}"); }

            OnDispose();
        }

        /// <summary>
        /// Subclass-specific dispose logic (overridable). The state notifier is already disposed at
        /// this point and must not be accessed. Subscriptions a subclass opened via
        /// <see cref="Subscribe(Action{TState}, bool)"/> / <see cref="Select{T}(Func{TState, T}, Action{T, T}, IEqualityComparer{T}, bool)"/>
        /// are owned by the subclass and should be disposed here.
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        #endregion
    }
}
