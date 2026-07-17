// Full nullable checking: hook public surface and dependency arrays are part of the shipped API contract.
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// Single entry point for all hooks invoked from a <c>[Component] static VNode</c> body. Hooks may only
    /// be called during Render(), unconditionally and in a stable order (the Rules of Hooks).
    /// For example, <see cref="UseState{T}"/> returns the 2-tuple <c>(value, setValue)</c> where
    /// <c>setValue</c> is a <see cref="StateUpdater{T}"/> accepting either a replacement value
    /// (<c>setValue.Invoke(next)</c>) or a functional updater (<c>setValue.Invoke(prev =&gt; next)</c>).
    /// </summary>
    public static class Hooks
    {
        /// <summary>
        /// Retrieves the currently rendering Fiber from <see cref="FiberAmbientStack"/>; throws when
        /// null or when called outside of Render().
        /// </summary>
        private static ComponentFiber Resolve(string hookName)
        {
            var fiber = FiberAmbientStack.Current;
            HookGuard.ThrowIfNotRendering(fiber, hookName);
            return fiber!;
        }

        /// <summary>
        /// True while the StrictMode throwaway diagnostic render runs. Hooks that mutate committed state or
        /// produce externally visible side effects suppress those during this pass. Always false in player
        /// builds (the diagnostic exists only in the Editor).
        /// </summary>
        private static bool IsStrictDiagnosticPass(ComponentFiber? fiber)
        {
#if UNITY_EDITOR
            return fiber!.IsStrictDiagnosticPass;
#else
            return false;
#endif
        }

        #region UseState

        /// <summary>
        /// Local state hook. Must be used inside Render() only.
        /// Returns the 2-tuple shape <c>(value, setValue)</c> where <c>setValue</c> is a single
        /// <see cref="StateUpdater{T}"/> that accepts either a replacement value or a functional updater.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="initial">Initial value used on the first render. Ignored on subsequent renders.</param>
        /// <returns>
        /// 2-tuple:
        /// - <c>value</c>: current value
        /// - <c>setValue</c>: <see cref="StateUpdater{T}"/> — call <c>setValue.Invoke(next)</c> to replace the
        ///   value, or <c>setValue.Invoke(prev =&gt; next)</c> for the functional updater (reads the latest
        ///   committed value, safe to invoke from a closure captured by an earlier render).
        /// </returns>
        public static (T value, StateUpdater<T> setValue) UseState<T>(T initial)
            => UseStateInternalCore(default, initial, "UseState");

        /// <summary>
        /// Lazy-initialized variant taking a factory invoked once for the initial value.
        /// <paramref name="initialFactory"/> is invoked exactly once on the first render to produce the
        /// initial state; subsequent renders skip the call. Use this when constructing the initial value
        /// is expensive (large Map/Set/List allocation, etc.) so the cost is not paid on every render.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="initialFactory">Factory invoked on first render only. Must not be null.</param>
        /// <returns>
        /// 2-tuple:
        /// - <c>value</c>: current value
        /// - <c>setValue</c>: <see cref="StateUpdater{T}"/> accepting a value or a functional updater.
        /// </returns>
        public static (T value, StateUpdater<T> setValue) UseState<T>(Func<T> initialFactory)
        {
            if (initialFactory == null) throw new ArgumentNullException(nameof(initialFactory));
            return UseStateFromFactory(initialFactory, "UseState");
        }

        private static (T value, StateUpdater<T> setValue) UseStateFromFactory<T>(Func<T> initialFactory, string hookName)
        {
            var fiber = Resolve(hookName);
            fiber.StateSlots ??= new List<HookStateSlot>();
            var index = fiber.Indices.StateHookIndex++;

            if (index >= fiber.StateSlots.Count)
            {
                var seed = initialFactory();
                var slot = new HookStateSlot<T> { Value = seed };
                var setValue = HookSetterFactory.CreateStateSetter(slot, () => fiber.IsDisposed, () => RequestRender(fiber));
                slot.Setter = new StateUpdater<T>(setValue, CreateUpdater(slot, fiber));
                fiber.StateSlots.Add(slot);
                return (slot.Value, slot.Setter);
            }

            if (fiber.StateSlots[index] is not HookStateSlot<T> typed)
            {
                throw HookSlotTypeMismatch(fiber, hookName, fiber.StateSlots[index].GetType(), typeof(T).Name, index);
            }

            return (typed.Value, typed.Setter);
        }

        // Slot lookup shared by the eager and lazy overloads. Pass a non-null <paramref name="initialFactory"/>
        // when the caller wants lazy initialization (the factory is invoked once on the first render).
        // Otherwise the eager <paramref name="initial"/> seeds the slot.
        private static (T value, StateUpdater<T> setValue) UseStateInternalCore<T>(
            Func<T>? initialFactory, T initial, string hookName)
        {
            var fiber = Resolve(hookName);
            fiber.StateSlots ??= new List<HookStateSlot>();
            var index = fiber.Indices.StateHookIndex++;

            if (index >= fiber.StateSlots.Count)
            {
                var seed = initialFactory != null ? initialFactory() : initial;
                var slot = new HookStateSlot<T> { Value = seed };
                var setValue = HookSetterFactory.CreateStateSetter(slot, () => fiber.IsDisposed, () => RequestRender(fiber));
                slot.Setter = new StateUpdater<T>(setValue, CreateUpdater(slot, fiber));
                fiber.StateSlots.Add(slot);
                return (slot.Value, slot.Setter);
            }

            if (fiber.StateSlots[index] is not HookStateSlot<T> typed)
            {
                throw HookSlotTypeMismatch(fiber, hookName, fiber.StateSlots[index].GetType(), typeof(T).Name, index);
            }

            return (typed.Value, typed.Setter);
        }

        private static Action<Func<T, T>> CreateUpdater<T>(HookStateSlot<T> slot, ComponentFiber fiber)
        {
            // The functional updater always reads the latest value via the updater function so it can be
            // safely invoked inside a closure.
            return updater =>
            {
                if (fiber.IsDisposed) return;
                if (updater == null) throw new ArgumentNullException(nameof(updater));
                var next = updater(slot.Value);
                if (ObjectIs.AreEqual(slot.Value, next)) return;
                slot.Value = next;
                RequestRender(fiber);
            };
        }

        #endregion

        #region UseStore

        /// <summary>
        /// Subscribes to an external Store via a selector. Must be used inside Render() only.
        /// </summary>
        /// <typeparam name="TStore">Store snapshot type.</typeparam>
        /// <typeparam name="TSel">Selected projection type returned to the component.</typeparam>
        /// <param name="store">Store instance to subscribe to. Must not be null.</param>
        /// <param name="selector">Pure projection from the store snapshot to the value the component cares about. Must not be null.</param>
        /// <param name="comparer">
        /// Equality comparer used to detect changes. Defaults to <see cref="ObjectIsEqualityComparer{TSel}"/>
        /// (reference equality for objects, NaN-aware for float/double).
        /// Pass <see cref="EqualityComparer{TSel}.Default"/> explicitly
        /// when value-equality skip is desired (e.g. record selectors with stable content).
        /// </param>
        /// <returns>The selected value at the current store snapshot.</returns>
        public static TSel UseStore<TStore, TSel>(
            Store<TStore> store,
            Func<TStore, TSel> selector,
            IEqualityComparer<TSel>? comparer = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            var fiber = Resolve("UseStore");
            fiber.StoreSlots ??= new List<HookStoreSlot>();
            var index = fiber.Indices.StoreHookIndex++;
            var cmp = comparer ?? ObjectIsEqualityComparer<TSel>.Instance;

            // Cross-tier tearing guard: read the snapshot pinned for this store within the current batch
            // drain wave instead of the live store.Current. An ancestor on the immediate tier and a
            // descendant on the delayed tier (separated by up to DeferredDelayMs) therefore observe the SAME
            // store value even if the store mutates between their tier drains; the mutation re-schedules every
            // reader, and that follow-up render lands on the next immediate drain, which re-pins to the now-
            // current snapshot so readers converge. Falls back to store.Current
            // outside a reconcile context (e.g. a fiber not yet attached to a Reconciler).
            var snapshot = PinStoreSnapshot(fiber, store);

            if (index >= fiber.StoreSlots.Count)
            {
                var slot = new HookStoreSlot<TStore, TSel>
                {
                    Store = store,
                    Selector = selector,
                    Comparer = cmp,
                    LastValue = selector(snapshot),
                };

                slot.Subscription = store.Subscribe(snapshot =>
                {
                    if (fiber.IsDisposed) return;
                    TSel next;
                    try
                    {
                        next = slot.Selector(snapshot);
                    }
                    catch
                    {
                        // A throwing selector is not swallowed in
                        // the subscription path. Re-render so the render-phase selector re-throws and the
                        // exception reaches the ErrorBoundary, symmetric with the unguarded render path.
                        RequestRender(fiber);
                        return;
                    }

                    if (slot.Comparer.Equals(slot.LastValue, next)) return;
                    slot.LastValue = next;
                    RequestRender(fiber);
                });

                fiber.StoreSlots.Add(slot);
                return slot.LastValue;
            }

            if (fiber.StoreSlots[index] is not HookStoreSlot<TStore, TSel> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseStore", fiber.StoreSlots[index].GetType(),
                    $"HookStoreSlot<{typeof(TStore).Name},{typeof(TSel).Name}>", index);
            }

            if (!ReferenceEquals(typed.Store, store))
            {
                throw new InvalidOperationException(
                    $"{ComponentName(fiber)}: the store reference passed to UseStore differs from the previous render (slot #{index})." +
                    " This violates the Rules of Hooks.");
            }

            typed.Selector = selector;
            typed.Comparer = cmp;
            typed.LastValue = selector(snapshot);
            return typed.LastValue;
        }

        // Returns the store snapshot pinned for the current batch drain wave (see
        // ReconcilerContext.PinStoreSnapshot), or the live store.Current when the fiber has no Reconciler
        // context yet. Pinning is reference-keyed on the store, so distinct stores never collide.
        private static TStore PinStoreSnapshot<TStore>(ComponentFiber fiber, Store<TStore> store)
        {
            var ctx = fiber.Reconciler?.Context;
            return ctx != null ? ctx.PinStoreSnapshot(store, store.Current) : store.Current;
        }

        #endregion

        #region UseContext

        /// <summary>
        /// Reads the value of the given context from the nearest Provider.
        /// Returns the context's default value when no Provider is present.
        /// </summary>
        /// <remarks>
        /// Always reflects the nearest enclosing Provider at the moment Render() runs, including after an
        /// isolated re-render (state update / context change), not a value pinned at first render.
        /// </remarks>
        /// <typeparam name="T">Context value type.</typeparam>
        /// <param name="context">Context object to read. Must not be null.</param>
        /// <returns>Provided value when an ancestor Provider is present, otherwise <c>context.DefaultValue</c>.</returns>
        public static T UseContext<T>(ComponentContext<T> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var fiber = Resolve("UseContext");
            fiber.RegisterContextDependency(context);
            var stack = fiber.Reconciler?.Context.ComponentContextStack;
            return stack != null ? stack.Get(context) : context.DefaultValue!;
        }

        #endregion

        #region UseService

        /// <summary>
        /// Resolves a service from the <see cref="IHookServiceResolver"/> provided through
        /// <see cref="HookServiceContext.Ref"/>. A service-locator hook that reads the host
        /// resolver from context, keeping the lookup DI-framework-neutral.
        /// </summary>
        /// <typeparam name="T">Service contract type to resolve.</typeparam>
        /// <returns>The instance returned by the host resolver.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no <see cref="HookServiceContext"/> Provider is mounted above the caller,
        /// or when the host <see cref="IHookServiceResolver"/> returns null in violation of its contract.
        /// </exception>
        public static T UseService<T>() where T : class
        {
            // Surface "UseService" in HookGuard's outside-of-render message instead of "UseContext".
            _ = Resolve("UseService");
            var resolver = UseContext(HookServiceContext.Ref)
                ?? throw new InvalidOperationException(
                    "HookServiceContext provider not found. " +
                    "Mount V.Provider(HookServiceContext.Ref, value: resolver, ...) at the root.");
            return resolver.Resolve<T>()
                ?? throw new InvalidOperationException(
                    $"IHookServiceResolver.Resolve<{typeof(T).Name}>() returned null. " +
                    "The host resolver must return a non-null instance or throw.");
        }

        #endregion

        #region UseCallback

        /// <summary>
        /// Returns the latest callback every render without memoization. With no deps argument the
        /// callback behaves as identity — a fresh closure each render.
        /// Use this overload only when you intentionally want the unmemoized form (e.g. handler that
        /// already captures no render-scoped state). For memoized stable references prefer the
        /// <see cref="UseCallback{T}(T, object?[])"/> overload with an explicit deps array.
        /// </summary>
        /// <remarks>
        /// The single-argument overload exists so that omitting deps is unambiguous: the
        /// <c>params object?[] deps</c> overload would otherwise observe an empty array (not
        /// <c>null</c>) and incorrectly freeze the callback to the first-render closure.
        /// </remarks>
        public static T UseCallback<T>(T callback) where T : Delegate
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var fiber = Resolve(nameof(UseCallback));
            fiber.CallbackSlots ??= new List<HookCallbackSlot>();
            var index = fiber.Indices.HookIndex++;

            // No-deps overload: returns this render's callback as-is (no memoization). Stage into Next* like the
            // deps overload so the render-phase settle commit treats every callback slot uniformly.
            if (index >= fiber.CallbackSlots.Count)
            {
                fiber.CallbackSlots.Add(new HookCallbackSlot
                {
                    NextCallback = callback,
                    NextDeps = null,
                });
            }
            else
            {
                var entry = fiber.CallbackSlots[index];
                entry.NextCallback = callback;
                entry.NextDeps = null;
            }
            return callback;
        }

        /// <summary>
        /// Returns the same delegate reference as long as the dependency array is unchanged.
        /// </summary>
        /// <typeparam name="T">Delegate type to memoize.</typeparam>
        /// <param name="callback">Callback to memoize. Captured on first render and on dependency changes.</param>
        /// <param name="deps">Dependency values. When deeply equal to the previous render, the cached callback is reused.</param>
        /// <returns>The cached callback reference (stable across renders while <paramref name="deps"/> are equal).</returns>
        public static T UseCallback<T>(T callback, params object?[] deps) where T : Delegate
        {
            var fiber = Resolve("UseCallback");
            fiber.CallbackSlots ??= new List<HookCallbackSlot>();
            var index = fiber.Indices.HookIndex++;

            if (index >= fiber.CallbackSlots.Count)
            {
                // New (mounting) slot: stage the callback / deps. The committed Callback / LastDeps are written
                // when the render settles, so a discarded render-phase attempt cannot freeze a stale callback.
                fiber.CallbackSlots.Add(new HookCallbackSlot
                {
                    NextCallback = callback,
                    NextDeps = deps,
                });
                return callback;
            }

            var entry = fiber.CallbackSlots[index];
            if (entry.Callback != null && ObjectIs.AreEqualDeps(entry.LastDeps, deps))
            {
                // Unchanged vs the committed render: return the committed callback so its reference stays stable
                // across a render-phase re-run. Comparing against the committed deps (never a discarded attempt)
                // is what preserves referential stability when render-phase state oscillates back to its committed
                // value.
                entry.NextCallback = entry.Callback;
                entry.NextDeps = entry.LastDeps;
                return (T)entry.Callback;
            }

            // Changed, or a not-yet-committed mount slot revisited during a render-phase re-run: stage this
            // render's callback; the committed value is promoted when the loop settles.
            entry.NextCallback = callback;
            entry.NextDeps = deps;
            return callback;
        }

        #endregion

        #region UseMemo

        /// <summary>
        /// Recomputes <paramref name="factory"/> on every render (no memoization). With no deps argument the
        /// value is never cached — prefer the <see cref="UseMemo{T}(Func{T}, object[])"/> overload with an
        /// explicit deps array to reuse the value while the dependencies are unchanged.
        /// </summary>
        /// <remarks>
        /// The single-argument overload exists so that omitting deps is unambiguous: the
        /// <c>params object?[]? deps</c> overload would otherwise observe an empty array (not <c>null</c>) and
        /// incorrectly freeze the value to the first-render computation.
        /// </remarks>
        /// <typeparam name="T">Type of the value to compute.</typeparam>
        /// <param name="factory">Factory invoked every render to produce the value.</param>
        /// <returns>The freshly computed value.</returns>
        public static T UseMemo<T>(Func<T> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve(nameof(UseMemo));
            fiber.MemoValueSlots ??= new List<HookMemoValueSlot>();
            var index = fiber.Indices.MemoValueHookIndex++;

            // No-deps overload: recompute every render (no memoization). Stage into Next* like the deps
            // overload so the render-phase settle commit treats every memo slot uniformly.
            var value = factory();
            if (index >= fiber.MemoValueSlots.Count)
            {
                fiber.MemoValueSlots.Add(new HookMemoValueSlot<T> { NextValue = value, NextDeps = null });
            }
            else
            {
                var entry = (HookMemoValueSlot<T>)fiber.MemoValueSlots[index];
                entry.NextValue = value;
                entry.NextDeps = null;
            }
            return value;
        }

        /// <summary>
        /// Returns the same computed value as long as the dependency array is unchanged; recomputes
        /// <paramref name="factory"/> only when a dependency changes.
        /// </summary>
        /// <typeparam name="T">Type of the value to memoize.</typeparam>
        /// <param name="factory">Factory invoked to produce the value on the first render and whenever the deps change.</param>
        /// <param name="deps">Dependency values. When deeply equal to the previous render, the cached value is reused.</param>
        /// <returns>The cached value (stable across renders while <paramref name="deps"/> are equal).</returns>
        public static T UseMemo<T>(Func<T> factory, params object?[]? deps)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve("UseMemo");
            fiber.MemoValueSlots ??= new List<HookMemoValueSlot>();
            var index = fiber.Indices.MemoValueHookIndex++;

            if (index >= fiber.MemoValueSlots.Count)
            {
                // New (mounting) slot: stage the value / deps. The committed Value / LastDeps are written when
                // the render settles, so a discarded render-phase attempt cannot freeze a stale value.
                var value = factory();
                fiber.MemoValueSlots.Add(new HookMemoValueSlot<T> { NextValue = value, NextDeps = deps });
                return value;
            }

            var entry = (HookMemoValueSlot<T>)fiber.MemoValueSlots[index];
            if (entry.Committed && ObjectIs.AreEqualDeps(entry.LastDeps, deps))
            {
                // Unchanged vs the committed render: return the committed value and re-stage the committed
                // values so a render-phase re-run keeps them. Comparing against the committed deps (never a
                // discarded attempt) is what preserves value stability when render-phase state oscillates.
                entry.NextValue = entry.Value;
                entry.NextDeps = entry.LastDeps;
                return entry.Value;
            }

            // Changed, or a not-yet-committed mount slot revisited during a render-phase re-run: recompute and
            // stage this render's value; the committed value is promoted when the loop settles.
            var recomputed = factory();
            entry.NextValue = recomputed;
            entry.NextDeps = deps;
            return recomputed;
        }

        #endregion

        #region UseBlocker

        /// <summary>
        /// Conditionally blocks navigation departures (synchronous variant).
        /// Must be used inside Render() only.
        /// </summary>
        /// <param name="shouldBlock">Predicate delegate; returning true blocks the departure.</param>
        /// <param name="deps">Dependency array. When null, re-registers on every render.</param>
        /// <returns>The shared <see cref="RouteBlockerState"/> handle for inspecting / resolving the pending departure.</returns>
        public static RouteBlockerState UseBlocker(Func<NavigationAttempt, bool> shouldBlock, params object?[] deps)
        {
            if (shouldBlock == null) throw new ArgumentNullException(nameof(shouldBlock));
            return UseBlockerCore(
                (router, state) => router.RouteBlockerManager.Register(shouldBlock, state),
                deps);
        }

        /// <summary>
        /// Conditionally blocks navigation departures (asynchronous variant). Use when integrating with
        /// asynchronous UI such as confirmation dialogs.
        /// </summary>
        /// <param name="shouldBlock">Async predicate; returning true blocks the departure. The CancellationToken is cancelled on unmount.</param>
        /// <param name="deps">Dependency array. When null, re-registers on every render.</param>
        /// <returns>The shared <see cref="RouteBlockerState"/> handle for inspecting / resolving the pending departure.</returns>
        public static RouteBlockerState UseBlocker(Func<NavigationAttempt, CancellationToken, UniTask<bool>> shouldBlock, params object?[] deps)
        {
            if (shouldBlock == null) throw new ArgumentNullException(nameof(shouldBlock));
            return UseBlockerCore(
                (router, state) => router.RouteBlockerManager.Register(shouldBlock, state),
                deps);
        }

        private static RouteBlockerState UseBlockerCore(Func<Router, RouteBlockerState, IDisposable> registerFn, object?[] deps)
        {
            var fiber = Resolve("UseBlocker");

            var router = Router.Current;
            if (router == null)
            {
                FiberLogger.LogWarning("Hooks",
                    $"{ComponentName(fiber)}: UseBlocker - Router.Current is null. Blocker is not registered.");
            }

            fiber.BlockerSlots ??= new List<HookBlockerSlot>();
            var index = fiber.Indices.BlockerHookIndex++;

            if (index < fiber.BlockerSlots.Count)
            {
                var existing = fiber.BlockerSlots[index];

                // Compare against the committed deps. The (Dispose -> re-register) side effect is staged and run
                // at the render-phase settle, so a discarded attempt cannot register a throwaway predicate closure
                // against Router.RouteBlockerManager (or leave the committed blocker pointing at it).
                var unchanged = deps != null && ObjectIs.AreEqualDeps(existing.LastDeps, deps);
                existing.NextDeps = deps;
                existing.NextNeedsReregister = !unchanged;
                existing.NextRegister = (!unchanged && router != null)
                    ? () => registerFn(router, existing.State)
                    : null;
                return existing.State;
            }

            // New (mounting) slot: the public State is created now (so it can be returned), but the registration
            // is staged and performed at settle with the settled attempt's predicate closure.
            var state = new RouteBlockerState();
            var slot = new HookBlockerSlot
            {
                State = state,
                NextDeps = deps,
                NextNeedsReregister = true,
                NextRegister = router != null ? () => registerFn(router, state) : null,
            };
            fiber.BlockerSlots.Add(slot);
            return state;
        }

        #endregion

        #region Routing (descendant router hooks)

        /// <summary>
        /// Returns the current router location.
        /// Reads <see cref="RouterContext.Location"/>; returns null when no router is mounted.
        /// </summary>
        public static RouterLocation UseLocation()
        {
            _ = Resolve("UseLocation");
            return UseContext(RouterContext.Location);
        }

        /// <summary>
        /// Returns the path parameters captured for the current location (cumulative across the matched
        /// route chain). Returns an empty dictionary when
        /// no router is mounted or no parameters were captured.
        /// </summary>
        public static IReadOnlyDictionary<string, string> UseParams()
        {
            _ = Resolve("UseParams");
            var location = UseContext(RouterContext.Location);
            return location?.Params ?? EmptyParams;
        }

        /// <summary>
        /// Returns a navigate function that pushes (or, when <paramref name="replace"/> is true, replaces)
        /// the given target path. The target may be
        /// absolute (<c>/foo</c>) or relative (<c>.</c>, <c>..</c>, <c>../sibling</c>) — relative targets
        /// resolve against <see cref="Router.CurrentLocation"/>. Navigation is driven by
        /// <see cref="Router.Current"/>.
        /// </summary>
        /// <returns>A stable delegate <c>navigate(to)</c>; the returned <see cref="UniTask{NavigationResult}"/> can be awaited or fire-and-forget.</returns>
        public static Func<string, UniTask<NavigationResult>> UseNavigate(bool replace = false)
        {
            _ = Resolve("UseNavigate");
            var mode = replace ? NavigationMode.Replace : NavigationMode.Push;
            // Capture the caller's Outlet depth so relative ("..") targets resolve against the route this
            // hook is called in, not the leaf route (route-relative resolution). A component
            // mounted by an Outlet at depth d owns Matches[d - 1], so baseRouteIndex = depth - 1. When
            // there is no enclosing route (depth 0), baseRouteIndex becomes -1, which the Router treats as
            // "leaf" and leaves absolute/no-context navigation unaffected.
            var depth = UseContext(RouterContext.Depth);
            var baseRouteIndex = depth - 1;
            return UseCallback<Func<string, UniTask<NavigationResult>>>(
                to =>
                {
                    var router = Router.Current;
                    if (router == null)
                    {
                        return UniTask.FromResult(NavigationResult.Cancelled);
                    }
                    return router.NavigateAsync(to, mode, baseRouteIndex);
                },
                mode, baseRouteIndex);
        }

        /// <summary>
        /// Returns the current navigation state. The state is <see cref="NavigationLifecycle.Loading"/> while
        /// the active <see cref="Router"/> is matching or loading the next location, and
        /// <see cref="NavigationLifecycle.Idle"/> otherwise. The component re-renders as the router's status
        /// transitions.
        /// </summary>
        /// <remarks>
        /// A <c>submitting</c> state is intentionally not modelled because Velvet has no route action /
        /// form-submission model.
        /// </remarks>
        public static NavigationState UseNavigation()
        {
            _ = Resolve("UseNavigation");
            var (state, setState) = UseState(ReadNavigationState(Router.Current));

            UseEffect(() =>
            {
                var router = Router.Current;
                if (router == null)
                {
                    return (Action)(() => { });
                }

                void Sync() => setState.Invoke(ReadNavigationState(router));
                void OnStatus(RouterStatus _) => Sync();
                void OnLocation(RouterLocation _) => Sync();

                router.OnStatusChanged += OnStatus;
                router.OnLocationChanged += OnLocation;
                // Reconcile any transition that happened between render and effect attach.
                Sync();
                return () =>
                {
                    router.OnStatusChanged -= OnStatus;
                    router.OnLocationChanged -= OnLocation;
                };
            }, Array.Empty<object>());

            return state;
        }

        private static NavigationState ReadNavigationState(Router? router)
        {
            if (router == null)
            {
                return new NavigationState { State = NavigationLifecycle.Idle, Location = null };
            }

            var lifecycle = router.Status is RouterStatus.Matching or RouterStatus.Loading
                ? NavigationLifecycle.Loading
                : NavigationLifecycle.Idle;

            return new NavigationState { State = lifecycle, Location = router.CurrentLocation };
        }

        /// <summary>
        /// Returns the parsed query string of the current location together with a setter that navigates
        /// to the same path with a new query string.
        /// </summary>
        /// <returns>
        /// A tuple of (the parsed search parameters, a setter that replaces the query string and navigates).
        /// </returns>
        public static (ISearchParams searchParams, SearchParamsSetter setSearchParams) UseSearchParams()
        {
            _ = Resolve("UseSearchParams");
            var location = UseContext(RouterContext.Location);
            var path = location?.Path ?? string.Empty;
            var parsed = RouteQuery.ParseQuery(path);

            // The setter is stateless (it reads Router.Current live), so a single shared, reference-stable
            // instance is returned — no per-render allocation. It supports a value or functional update, and
            // defaults to a PUSH navigation so Back returns to the previous query.
            return (parsed, SearchParamsSetter.Shared);
        }

        // Cache pattern -> single-route matcher so UseMatch does not allocate a RouteTree on every render.
        // Velvet renders on the main thread, so a plain dictionary needs no synchronization. Cleared on each
        // domain init so matchers are not retained across "Enter Play Mode (no Domain Reload)" sessions.
        private static readonly Dictionary<string, RouteTree> s_useMatchTrees = new();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetUseMatchTrees() => s_useMatchTrees.Clear();

        /// <summary>
        /// Returns the match for the given <paramref name="pattern"/> against the current location, or null
        /// when it does not match. The pattern uses the same
        /// segment syntax as routes (literal / <c>:param</c> / <c>*</c>); matching is case-insensitive.
        /// </summary>
        public static RouteMatch? UseMatch(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            _ = Resolve("UseMatch");
            var location = UseContext(RouterContext.Location);
            var path = RouteQuery.StripQuery(location?.Path);
            if (path == null)
            {
                return null;
            }

            if (!s_useMatchTrees.TryGetValue(pattern, out var tree))
            {
                tree = new RouteTree(new[]
                {
                    new RouteDefinition { Path = pattern, Element = null },
                });
                s_useMatchTrees[pattern] = tree;
            }
            var matches = tree.Match(path);
            return matches is { Count: > 0 } ? matches[matches.Count - 1] : null;
        }

        /// <summary>
        /// Returns the context value supplied by the enclosing <c>Outlet</c>.
        /// Returns <c>default</c> when no context was supplied.
        /// </summary>
        /// <typeparam name="T">Expected context value type.</typeparam>
        public static T? UseOutletContext<T>()
        {
            _ = Resolve("UseOutletContext");
            var value = UseContext(RouterContext.OutletContext);
            return value is T typed ? typed : default;
        }

        /// <summary>
        /// Returns the loader data for the route at the current Outlet depth, cast to <typeparamref name="T"/>.
        /// Returns <c>default</c> when there is no data.
        /// </summary>
        /// <typeparam name="T">Expected loader data type.</typeparam>
        public static T? UseLoaderData<T>()
        {
            _ = Resolve("UseLoaderData");
            var routeId = CurrentRouteId();
            if (routeId == null)
            {
                return default;
            }
            var data = UseContext(RouterContext.LoaderData);
            return data != null && data.TryGetValue(routeId, out var value) && value is T typed ? typed : default;
        }

        /// <summary>
        /// Returns the loader error for the route at the current Outlet depth, or null when the route did
        /// not error.
        /// </summary>
        public static Exception? UseRouteError()
        {
            _ = Resolve("UseRouteError");
            var location = UseContext(RouterContext.Location);
            var depth = UseContext(RouterContext.Depth);
            var errors = UseContext(RouterContext.Errors);
            if (location?.Matches == null || depth <= 0 || errors == null || errors.Count == 0)
            {
                return null;
            }
            // A loader error bubbles to the nearest ancestor route with an ErrorElement,
            // which renders here. The error is keyed by the descendant route that actually threw, so it is not
            // found under this boundary route's own RouteId. Resolve it by scanning from this boundary
            // (Matches[depth - 1]) toward the leaf and returning the error on the nearest matched route at or
            // below the boundary — the one this boundary caught.
            var start = System.Math.Min(depth - 1, location.Matches.Count - 1);
            for (var i = start; i < location.Matches.Count; i++)
            {
                var routeId = location.Matches[i].RouteId;
                if (routeId != null && errors.TryGetValue(routeId, out var ex))
                {
                    return ex;
                }
            }
            return null;
        }

        private static readonly IReadOnlyDictionary<string, string> EmptyParams =
            new Dictionary<string, string>();

        /// <summary>
        /// Resolves the RouteId of the route rendered at the caller's Outlet depth. A component mounted by
        /// an Outlet sees <see cref="RouterContext.Depth"/> incremented to depth+1, so its own match is
        /// <c>Matches[Depth - 1]</c>. Returns null when there is no enclosing matched route.
        /// </summary>
        private static string? CurrentRouteId()
        {
            var location = UseContext(RouterContext.Location);
            var depth = UseContext(RouterContext.Depth);
            if (location?.Matches == null || depth <= 0 || depth > location.Matches.Count)
            {
                return null;
            }
            return location.Matches[depth - 1].RouteId;
        }

        #endregion

        #region UseLayoutEffect

        /// <summary>
        /// Position-based layout effect. Runs synchronously immediately after Render completes,
        /// before the frame is painted.
        /// </summary>
        /// <param name="factory">Effect body. Returns a cleanup Action invoked on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseLayoutEffect(Func<Action?>? factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve("UseLayoutEffect");
            HookSlotRegistrar.RegisterLayoutEffect(
                ref fiber.LayoutEffects,
                ref fiber.PendingLayoutEffects,
                ref fiber.Indices.LayoutEffectHookIndex,
                factory,
                deps,
                IsStrictDiagnosticPass(fiber));
        }

        /// <summary>IDisposable variant. Dispose() is called when deps change.</summary>
        /// <param name="factory">Effect body returning an <see cref="IDisposable"/>. Disposed on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseLayoutEffect(Func<IDisposable> factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            UseLayoutEffect(() => WrapDisposable(factory), deps);
        }

        #endregion

        #region UseInsertionEffect

        /// <summary>
        /// Position-based insertion effect. Runs synchronously
        /// after Render completes and <b>before</b> any <see cref="UseLayoutEffect(Func{Action}, object[])"/> of
        /// the same commit. Intended for injecting styles: the DOM is not yet
        /// laid out, so the body must not read layout or refs — use <see cref="UseLayoutEffect(Func{Action}, object[])"/>
        /// for that.
        /// </summary>
        /// <param name="factory">Effect body. Returns a cleanup Action invoked on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseInsertionEffect(Func<Action?>? factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve("UseInsertionEffect");
            HookSlotRegistrar.RegisterLayoutEffect(
                ref fiber.InsertionEffects,
                ref fiber.PendingInsertionEffects,
                ref fiber.Indices.InsertionEffectHookIndex,
                factory,
                deps,
                IsStrictDiagnosticPass(fiber));
        }

        /// <summary>IDisposable variant. Dispose() is called when deps change.</summary>
        /// <param name="factory">Effect body returning an <see cref="IDisposable"/>. Disposed on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseInsertionEffect(Func<IDisposable> factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            UseInsertionEffect(() => WrapDisposable(factory), deps);
        }

        #endregion

        #region UseEffect (async)

        /// <summary>
        /// Position-based asynchronous effect. Runs at the next frame boundary, after the frame is painted.
        /// </summary>
        /// <param name="factory">Effect body. Returns a cleanup Action invoked on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseEffect(Func<Action?>? factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve("UseEffect");
            HookSlotRegistrar.RegisterEffect(
                ref fiber.Effects,
                ref fiber.PendingEffects,
                ref fiber.Indices.EffectHookIndex,
                factory,
                deps,
                deduplicatePending: true,
                diagnosticPass: IsStrictDiagnosticPass(fiber));
        }

        /// <summary>IDisposable variant.</summary>
        /// <param name="factory">Effect body returning an <see cref="IDisposable"/>. Disposed on unmount or when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array. When deeply equal to the previous render, the effect is skipped. When omitted or <c>null</c>, the effect runs on every render.</param>
        public static void UseEffect(Func<IDisposable> factory, object?[]? deps = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            UseEffect(() => WrapDisposable(factory), deps);
        }

        #endregion

        #region UseFrame

        /// <summary>
        /// Per-frame callback: <paramref name="onFrame"/> runs once per frame with the elapsed time in
        /// seconds while the component stays mounted, and stops on unmount. The latest render's closure
        /// is always the one invoked — a re-render swaps the callback without re-subscribing — so
        /// per-frame data flows without touching component state (the escape hatch for
        /// simulation-driven visuals; setting state per frame would re-render the world every tick).
        /// Frames tick while the component's host is attached to a panel and pause while it is not.
        /// </summary>
        /// <param name="onFrame">Invoked once per frame with the elapsed time in seconds (always positive).</param>
        public static void UseFrame(Action<float> onFrame)
        {
            if (onFrame == null) throw new ArgumentNullException(nameof(onFrame));
            var fiber = Resolve("UseFrame");
            // Every render overwrites the ref slot, and the tick below reads through it — that is what
            // swaps in the latest closure without re-subscribing (the effect's empty deps never re-run).
            // StrictMode's throwaway diagnostic pass must not swap the LIVE closure (its captures are
            // the discarded pass's state), matching UseImperativeHandle's gate.
            var latest = UseRef<Action<float>>();
            if (!IsStrictDiagnosticPass(fiber))
            {
                latest.Set(onFrame);
            }

            UseEffect(() =>
            {
                // Resolved inside the effect factory: MountPoint is unset while rendering and assigned
                // by commit time, and this passive effect runs after the commit has painted.
                var host = fiber.MountPoint;
                if (host == null)
                {
                    return null;
                }
                UnityEngine.UIElements.IVisualElementScheduledItem? tick = null;
                void StartTickIfNeeded()
                {
                    if (tick != null)
                    {
                        return;
                    }
                    tick = host.schedule.Execute((UnityEngine.UIElements.TimerState ts) =>
                    {
                        // TimerState.start is the previous callback's time for a repeating item (or the
                        // schedule time for the first firing), so deltaTime is already exactly the
                        // elapsed interval — no separate "last tick" bookkeeping. A zero delta
                        // (same-frame flush) is skipped so the callback only ever observes positive,
                        // frame-sized seconds; a hitch spike is clamped the way Time.deltaTime clamps
                        // its own, so a stall cannot teleport a user simulation by one giant step. The
                        // zero interval fires on every scheduler update — once per frame — rather than
                        // imposing a wall-clock floor that would skip frames on fast panels.
                        var dt = ts.deltaTime / 1000f;
                        if (dt <= 0f)
                        {
                            return;
                        }
                        dt = UnityEngine.Mathf.Min(dt, UnityEngine.Time.maximumDeltaTime);
                        try
                        {
                            latest.Current?.Invoke(dt);
                        }
                        catch (Exception ex)
                        {
                            // Contained like an effect exception: an escaped throw would abort the rest
                            // of this panel's scheduled updates for the frame and re-fire every frame
                            // thereafter (the scheduler never unschedules a throwing item), and the
                            // nearest error boundary must receive user-callback failures either way.
                            ComponentBoundarySearch.PropagateException(fiber, ex);
                        }
                    }).Every(0);
                }
                // A recurring item like this Every(0) tick survives a keyed reorder's detach/re-attach
                // on its own: UI Toolkit's own per-item attach/detach handling pauses it when the host
                // leaves the panel and reschedules the SAME item when the host returns (the detach and
                // re-attach even cancel out in the scheduler's own bookkeeping when, as here, both land
                // in a single pass), so nothing needs to re-arm it. A one-shot item has no such luck —
                // its delay restarts in full on every re-attach. The tick is therefore only ever
                // created once; an explicit Pause on every detach, as this hook used to do, would
                // defeat that built-in survival and force a fresh item to be armed on every attach for
                // no benefit.
                UnityEngine.UIElements.EventCallback<UnityEngine.UIElements.AttachToPanelEvent> onAttach = _ => StartTickIfNeeded();
                host.RegisterCallback(onAttach);
                // The passive effect runs post-commit, so the host is ordinarily already attached and
                // no AttachToPanelEvent is coming — arm the first tick directly.
                if (host.panel != null)
                {
                    StartTickIfNeeded();
                }
                return () =>
                {
                    host.UnregisterCallback(onAttach);
                    tick?.Pause();
                    tick = null;
                };
            }, Array.Empty<object>());
        }

        #endregion

        #region UseAnimationSequence

        /// <summary>
        /// Plays an ordered <see cref="AnimationSequenceStep"/> array over time, exposing the active step's
        /// label/transition to feed straight into a coordinator <c>V.Motion(animate:, transition:)</c>.
        /// Velvet's timeline primitive (Framer Motion's <c>useAnimate</c> parity target): the hook owns the
        /// clock (via <see cref="UseFrame"/>) and the step walk, so a caller never hand-rolls
        /// <see cref="UseEffect(Func{Action},object[])"/> plus a timer plus <see cref="UseState{T}(T)"/> to
        /// sequence a multi-stage animation. Descendant <c>V.Motion</c> nodes with no own <c>animate</c>
        /// inherit the coordinator's label exactly as they already do for any hand-toggled label change; "one
        /// at a time" fan-out across a list of such descendants is <c>StaggerChildrenSec</c> on a step's own
        /// <see cref="AnimationSequenceStep.Transition"/> — there is no separate multi-target API.
        /// </summary>
        /// <param name="steps">The ordered sequence. Must not be null.</param>
        /// <param name="autoplay">Starts advancing on mount when true (default). When false, call
        /// <c>controls.Play()</c> — e.g. from an <c>onClick</c> handler — to start it on demand. Only read on
        /// mount / a <paramref name="deps"/> change, not on every render, so a later <c>controls.Pause()</c>
        /// is not fought by a re-render that keeps passing <c>autoplay: true</c>.</param>
        /// <param name="loop">When true, the cursor wraps to step 0 after the last step's hold elapses and
        /// <see cref="AnimationSequenceState.IsComplete"/> never latches.</param>
        /// <param name="deps">
        /// Unlike <see cref="UseEffect(Func{Action},object[])"/>, omitting this (or passing null) resets the
        /// walker on MOUNT ONLY, not on every render — a freshly-built <paramref name="steps"/> array literal
        /// in the component body (the common case) must not restart an in-flight sequence every render. Pass
        /// an explicit array to restart the sequence when one of its entries changes, same convention as every
        /// other deps-taking hook.
        /// </param>
        public static (AnimationSequenceState state, AnimationSequenceControls controls) UseAnimationSequence(
            IReadOnlyList<AnimationSequenceStep> steps, bool autoplay = true, bool loop = false, object?[]? deps = null)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var fiber = Resolve("UseAnimationSequence");
            var walker = UseRef(() => new SequenceWalker());
            var (_, bumpRenderVersion) = UseState(0);

            // Tracks the LATEST render's steps for controls.Restart() to read (see below) — mirrors UseFrame's
            // own `latest.Set(onFrame)` pattern: a re-render must not leave an earlier render's Restart closing
            // over a stale steps array, and the StrictMode throwaway diagnostic pass's steps must never become
            // "latest" since that render is discarded.
            var latestSteps = UseRef<IReadOnlyList<AnimationSequenceStep>>();
            if (!IsStrictDiagnosticPass(fiber))
            {
                latestSteps.Set(steps);
            }

            UseEffect(() =>
            {
                walker.Current.Reset(steps);
                walker.Current.IsPaused = !autoplay;
                bumpRenderVersion.Invoke(v => v + 1);
                return (Action)null;
            }, deps ?? Array.Empty<object>());

            UseFrame(dt =>
            {
                if (walker.Current.IsPaused || walker.Current.IsComplete)
                {
                    return;
                }
                var beforeGeneration = walker.Current.Generation;
                walker.Current.Advance(dt, loop);
                if (walker.Current.Generation != beforeGeneration || walker.Current.IsComplete)
                {
                    bumpRenderVersion.Invoke(v => v + 1);
                }
            });

            var controls = new AnimationSequenceControls(
                play: () => walker.Current.IsPaused = false,
                pause: () => walker.Current.IsPaused = true,
                restart: () =>
                {
                    walker.Current.Reset(latestSteps.Current ?? steps);
                    bumpRenderVersion.Invoke(v => v + 1);
                });

            return (walker.Current.ToState(), controls);
        }

        #endregion

        #region UseFocusRing

        /// <summary>
        /// React Aria's <c>useFocusRing</c> parity: exposes an element's focus state — and specifically
        /// keyboard/gamepad-visible focus, as distinct from pointer focus — as re-rendering component
        /// state. Pass <see cref="FocusRing.Ref"/> as the target element's <c>refCallback:</c>. For pure
        /// styling, the <c>focus-visible:</c> class variant already covers the same distinction without a
        /// hook — reach for this when the component must RENDER differently (e.g. a "press A to select"
        /// hint), not just restyle.
        /// </summary>
        public static FocusRing UseFocusRing()
        {
            var (isFocused, setFocused) = UseState(false);
            var (isFocusVisible, setFocusVisible) = UseState(false);
            var refCallback = UseCallback<Func<UnityEngine.UIElements.VisualElement, Action>>(element =>
            {
                var signals = new ElementLocalVariantSignals((signal, on) =>
                {
                    switch (signal)
                    {
                        case VariantSignal.Focus: setFocused.Invoke(on); break;
                        case VariantSignal.FocusVisible: setFocusVisible.Invoke(on); break;
                    }
                });
                signals.Hook(element, seedChecked: false, registerChecked: false);
                // Seed: the signals are edge-driven, so hooking an element that ALREADY holds focus
                // raises no Focus edge. A ref composed inside a per-render lambda cycles on every
                // patch (fresh identity), and its cleanup below writes the flags false on the
                // still-focused element — without this seed the ring would go dark and stay dark
                // until a real blur+refocus. Focus-visible is deliberately NOT seeded: the input
                // modality that produced the pre-existing focus is unknown here, and understating
                // the ring beats inventing a keyboard modality a pointer created.
                if (element.panel?.focusController?.focusedElement is UnityEngine.UIElements.VisualElement alreadyHeld
                    && (alreadyHeld == element || element.Contains(alreadyHeld)))
                {
                    setFocused.Invoke(true);
                }
                return () =>
                {
                    signals.Unhook();
                    // An element torn down WHILE FOCUSED gets no Blur through these signals (the
                    // unhook above runs before the element leaves the panel), which would strand
                    // the flags at true on a still-mounted component. The ref cycles only on
                    // identity change or a host remount — never per patch — and a state write from
                    // the commit phase schedules an ordinary follow-up render, so the correction is
                    // a plain setter call; the setters are no-ops if the component itself unmounted.
                    var panel = element.panel;
                    if (panel?.focusController?.focusedElement is UnityEngine.UIElements.VisualElement held
                        && (held == element || element.Contains(held)))
                    {
                        setFocused.Invoke(false);
                        setFocusVisible.Invoke(false);
                    }
                };
                // Deps are the two setters — reference-stable across renders — so the ref identity
                // never changes and a patch leaves the installed ref untouched.
            }, setFocused, setFocusVisible);
            return new FocusRing(isFocused, isFocusVisible, refCallback);
        }

        #endregion

        #region Refs

        /// <summary>
        /// Hook that returns the same <see cref="Ref{T}"/> across re-renders.
        /// On the first render, creates a <c>new Ref&lt;T&gt;()</c>, stores it in the fiber slot, and returns
        /// the same reference at the same position thereafter. <see cref="Ref{T}.Current"/> stays null until
        /// it is assigned externally.
        /// </summary>
        /// <typeparam name="T">Reference target type.</typeparam>
        /// <returns>The fiber-scoped <see cref="Ref{T}"/> stored at this hook position.</returns>
        public static Ref<T> UseRef<T>() where T : class => UseRef<T>(initialFactory: null);

        /// <summary>
        /// Variant of <see cref="UseRef{T}()"/> that takes an initial factory.
        /// On the first render, invokes <paramref name="initialFactory"/> and stores the result into
        /// <see cref="Ref{T}.Current"/>, seeding the ref with a lazily-constructed value.
        /// </summary>
        /// <typeparam name="T">Reference target type.</typeparam>
        /// <param name="initialFactory">Factory invoked once on first render to seed <see cref="Ref{T}.Current"/>. Pass null to leave it unset.</param>
        /// <returns>The fiber-scoped <see cref="Ref{T}"/> stored at this hook position.</returns>
        public static Ref<T> UseRef<T>(Func<T>? initialFactory) where T : class
        {
            var fiber = Resolve("UseRef");
            fiber.RefSlots ??= new List<HookRefSlot>();
            var index = fiber.Indices.RefHookIndex++;

            if (index >= fiber.RefSlots.Count)
            {
                var newRef = new Ref<T>();
                if (initialFactory != null)
                {
                    ((IHookRefSetter)newRef).Set(initialFactory());
                }
                fiber.RefSlots.Add(new HookRefSlot { Ref = newRef });
                return newRef;
            }

            if (fiber.RefSlots[index].Ref is not Ref<T> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseRef", fiber.RefSlots[index].Ref.GetType(),
                    $"Ref<{typeof(T).Name}>", index);
            }
            return typed;
        }

        /// <summary>
        /// Hook that returns the same <see cref="MutableRef{T}"/> across re-renders, used as an
        /// instance-scoped mutable slot. Unlike <see cref="UseRef{T}()"/>, the type
        /// parameter is unconstrained so value types can be stored directly.
        /// On the first render, allocates <c>new MutableRef&lt;T&gt;(initial)</c> and stores it in the fiber
        /// slot; subsequent renders return the same instance.
        /// Writes to <see cref="MutableRef{T}.Current"/> do not schedule a re-render.
        /// </summary>
        /// <typeparam name="T">Stored value type. May be a value type or a reference type.</typeparam>
        /// <param name="initial">Initial value applied on the first render only.</param>
        /// <returns>The fiber-scoped <see cref="MutableRef{T}"/> stored at this hook position.</returns>
        public static MutableRef<T> UseMutableRef<T>(T initial) => UseMutableRefInternal<T>(initial, factory: null);

        /// <summary>
        /// Variant of <see cref="UseMutableRef{T}(T)"/> that takes a lazy factory. <paramref name="initialFactory"/>
        /// is invoked only on the first render to seed <see cref="MutableRef{T}.Current"/>; subsequent renders
        /// return the same instance without invoking the factory.
        /// </summary>
        /// <typeparam name="T">Stored value type. May be a value type or a reference type.</typeparam>
        /// <param name="initialFactory">Factory invoked once on first render to produce the initial value.</param>
        /// <returns>The fiber-scoped <see cref="MutableRef{T}"/> stored at this hook position.</returns>
        public static MutableRef<T> UseMutableRef<T>(Func<T> initialFactory)
        {
            if (initialFactory == null) throw new ArgumentNullException(nameof(initialFactory));
            return UseMutableRefInternal<T>(default!, initialFactory);
        }

        private static MutableRef<T> UseMutableRefInternal<T>(T initial, Func<T>? factory)
        {
            var fiber = Resolve("UseMutableRef");
            fiber.RefSlots ??= new List<HookRefSlot>();
            var index = fiber.Indices.RefHookIndex++;

            if (index >= fiber.RefSlots.Count)
            {
                var seed = factory != null ? factory() : initial;
                var newRef = new MutableRef<T>(seed);
                fiber.RefSlots.Add(new HookRefSlot { Ref = newRef });
                return newRef;
            }

            if (fiber.RefSlots[index].Ref is not MutableRef<T> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseMutableRef", fiber.RefSlots[index].Ref.GetType(),
                    $"MutableRef<{typeof(T).Name}>", index);
            }
            return typed;
        }

        /// <summary>
        /// Retrieves the handle reference passed by the parent via <c>V.Component&lt;TRef&gt;(componentRef:)</c>.
        /// </summary>
        /// <typeparam name="T">Handle type expected by this component.</typeparam>
        /// <returns>The forwarded <see cref="Ref{T}"/>, or null when the parent did not forward one or types do not match.</returns>
        public static Ref<T>? ForwardedRef<T>() where T : class
        {
            var fiber = Resolve("ForwardedRef");
            return fiber.ExternalRef as Ref<T>;
        }

        #endregion

        #region UseId

        /// <summary>
        /// Returns a stable unique ID string tied to the component instance
        /// and slot position. Re-renders of the same component return the same ID; different instances
        /// or different slots receive different IDs. Use as a unique key for label-to-field association
        /// or aria attributes.
        /// </summary>
        /// <param name="prefix">
        /// Optional prefix to prepend (omitted when null/empty).
        /// Only the first render reflects this in the generated ID; changes on subsequent renders are
        /// ignored (same convention as the initial value of UseState).
        /// </param>
        /// <returns>Format is <c>:r{hex}:</c> (no prefix) or <c>{prefix}:r{hex}:</c> (with prefix).</returns>
        public static string UseId(string? prefix = null)
        {
            var fiber = Resolve("UseId");
            fiber.IdSlots ??= new List<HookIdSlot>();
            var index = fiber.Indices.IdHookIndex++;

            if (index >= fiber.IdSlots.Count)
            {
                // Tree-position based (fiber instance + slot index). Decentralized — different
                // roots / different trees produce independent IDs without coordination, so there
                // is no global counter to share between subsystems. The emitted
                // <c>:r{...}:</c> format keeps DOM consumers (USS, aria) compatible.
                var fiberHash = (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(fiber);
                // Shift the small slot index into the high byte before XOR so a fiber's sibling slots spread
                // across the id space instead of differing only in the low bits of the same fiber hash.
                var slotKey = fiberHash ^ ((uint)index << 24);
                var id = string.IsNullOrEmpty(prefix) ? $":r{slotKey:x}:" : $"{prefix}:r{slotKey:x}:";
                fiber.IdSlots.Add(new HookIdSlot { Id = id });
                return id;
            }

            return fiber.IdSlots[index].Id;
        }

        #endregion

        #region UseImperativeHandle

        /// <summary>
        /// Builds a public handle (via <paramref name="factory"/>) and stores it into the
        /// <see cref="Ref{THandle}"/> the parent passed via <c>componentRef:</c>. This no-deps overload
        /// re-invokes the factory every render (equivalent to passing <c>null</c> as deps); it exists so omitting
        /// deps is unambiguous — the <c>params object[]</c> overload would otherwise observe an empty array (not
        /// <c>null</c>) and incorrectly freeze the handle to the first render.
        /// </summary>
        /// <typeparam name="THandle">Handle type exposed to the parent.</typeparam>
        /// <param name="handleRef">Parent-supplied <see cref="Ref{THandle}"/> that receives the handle. May be null.</param>
        /// <param name="factory">Factory invoked to build the handle on every render. Must not be null.</param>
        public static void UseImperativeHandle<THandle>(
            Ref<THandle> handleRef,
            Func<THandle> factory) where THandle : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve(nameof(UseImperativeHandle));
            fiber.ImperativeHandleSlots ??= new List<HookImperativeHandleSlot>();
            var index = fiber.Indices.ImperativeHandleHookIndex++;

            // The StrictMode diagnostic render must not write into the parent-supplied ref (an externally
            // visible side effect) nor re-run the handle factory; the cursor is advanced above for the purity
            // check and the handle is not part of the render output signature.
            if (IsStrictDiagnosticPass(fiber)) return;

            // No-deps: refresh every render. Stage the factory / ref; the handle is built and the parent ref is
            // written at the render-phase settle so a discarded attempt cannot expose a throwaway handle.
            if (index >= fiber.ImperativeHandleSlots.Count)
            {
                fiber.ImperativeHandleSlots.Add(new HookImperativeHandleSlot
                {
                    NextHandleRef = handleRef,
                    NextFactory = factory,
                    NextDeps = null,
                    NextNeedsRecompute = true,
                });
                return;
            }

            var entry = fiber.ImperativeHandleSlots[index];
            entry.NextHandleRef = handleRef;
            entry.NextFactory = factory;
            entry.NextDeps = null;
            entry.NextNeedsRecompute = true;
        }

        /// <summary>
        /// Builds a handle via <paramref name="factory"/> and stores it into the parent-supplied
        /// <see cref="Ref{THandle}"/>, rebuilding only when <paramref name="deps"/> change (compared with
        /// <c>Object.is</c> semantics).
        /// </summary>
        /// <typeparam name="THandle">The imperative handle type exposed to the parent.</typeparam>
        /// <param name="handleRef">The ref the parent passed via <c>componentRef:</c>.</param>
        /// <param name="factory">Produces the handle; re-invoked when <paramref name="deps"/> change.</param>
        /// <param name="deps">Dependency array gating recomputation.</param>
        public static void UseImperativeHandle<THandle>(
            Ref<THandle> handleRef,
            Func<THandle> factory,
            params object?[]? deps) where THandle : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve("UseImperativeHandle");
            fiber.ImperativeHandleSlots ??= new List<HookImperativeHandleSlot>();
            var index = fiber.Indices.ImperativeHandleHookIndex++;

            // See the no-deps overload: the diagnostic render skips the ref write and factory invocation.
            if (IsStrictDiagnosticPass(fiber)) return;

            if (index >= fiber.ImperativeHandleSlots.Count)
            {
                fiber.ImperativeHandleSlots.Add(new HookImperativeHandleSlot
                {
                    NextHandleRef = handleRef,
                    NextFactory = factory,
                    NextDeps = deps,
                    NextNeedsRecompute = true,
                });
                return;
            }

            var entry = fiber.ImperativeHandleSlots[index];
            // Compare against the committed deps. The handle recompute and the parent ref write (and any ref
            // swap) are deferred to the render-phase settle so a discarded attempt cannot build the handle from a
            // throwaway render or leave the parent ref pointing at it.
            entry.NextHandleRef = handleRef;
            entry.NextFactory = factory;
            entry.NextDeps = deps;
            entry.NextNeedsRecompute = deps == null || !ObjectIs.AreEqualDeps(entry.LastDeps, deps);
        }

        #endregion

        #region UseReducer

        /// <summary>
        /// Local state hook that derives the next state from the current state and a dispatched action.
        /// </summary>
        /// <typeparam name="TState">State type managed by the reducer.</typeparam>
        /// <typeparam name="TAction">Action type accepted by the reducer.</typeparam>
        /// <param name="reducer">Pure reducer (state, action) =&gt; nextState. Must not be null.</param>
        /// <param name="initial">Initial state used on the first render. Ignored on subsequent renders.</param>
        /// <returns>
        /// 2-tuple:
        /// - <c>state</c>: current state value.
        /// - <c>dispatch</c>: action dispatcher that schedules a re-render with the next state.
        /// </returns>
        public static (TState state, Action<TAction> dispatch) UseReducer<TState, TAction>(
            Func<TState, TAction, TState> reducer, TState initial)
        {
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));
            return UseReducerCore<TState, TAction>(reducer, initial);
        }

        /// <summary>
        /// Lazy-initialized variant taking an <paramref name="init"/> function to compute the initial state.
        /// <paramref name="init"/> is invoked exactly once with <paramref name="initialArg"/> on the first
        /// render to produce the initial state, then never called again. Use this to defer expensive
        /// initialization (large arrays, dictionaries, etc.) so that it does not run on every render.
        /// </summary>
        /// <typeparam name="TArg">Type of the argument forwarded to <paramref name="init"/>.</typeparam>
        /// <typeparam name="TState">State type managed by the reducer.</typeparam>
        /// <typeparam name="TAction">Action type accepted by the reducer.</typeparam>
        /// <param name="reducer">Pure reducer (state, action) =&gt; nextState. Must not be null.</param>
        /// <param name="initialArg">Argument passed to <paramref name="init"/> on the first render. Ignored afterwards.</param>
        /// <param name="init">Initializer that computes the initial state from <paramref name="initialArg"/>. Must not be null.</param>
        /// <returns>
        /// 2-tuple:
        /// - <c>state</c>: current state value.
        /// - <c>dispatch</c>: action dispatcher that schedules a re-render with the next state.
        /// </returns>
        public static (TState state, Action<TAction> dispatch) UseReducer<TArg, TState, TAction>(
            Func<TState, TAction, TState> reducer, TArg initialArg, Func<TArg, TState> init)
        {
            if (reducer == null) throw new ArgumentNullException(nameof(reducer));
            if (init == null) throw new ArgumentNullException(nameof(init));
            var fiber = Resolve("UseReducer");
            fiber.StateSlots ??= new List<HookStateSlot>();
            var index = fiber.Indices.StateHookIndex++;

            if (index >= fiber.StateSlots.Count)
            {
                return CreateReducerSlot<TState, TAction>(fiber, init(initialArg), reducer);
            }
            return UpdateReducerSlot<TState, TAction>(fiber, index, reducer);
        }

        private static (TState state, Action<TAction> dispatch) UseReducerCore<TState, TAction>(
            Func<TState, TAction, TState> reducer, TState initial)
        {
            var fiber = Resolve("UseReducer");
            fiber.StateSlots ??= new List<HookStateSlot>();
            var index = fiber.Indices.StateHookIndex++;

            if (index >= fiber.StateSlots.Count)
            {
                return CreateReducerSlot<TState, TAction>(fiber, initial, reducer);
            }
            return UpdateReducerSlot<TState, TAction>(fiber, index, reducer);
        }

        private static (TState state, Action<TAction> dispatch) CreateReducerSlot<TState, TAction>(
            ComponentFiber fiber, TState initial, Func<TState, TAction, TState> reducer)
        {
            var slot = new ReducerSlot<TState, TAction>
            {
                Value = initial,
                Reducer = reducer,
            };
            slot.Dispatch = HookSetterFactory.CreateReducerDispatch(slot, () => fiber.IsDisposed, () => RequestRender(fiber));
            fiber.StateSlots!.Add(slot);
            return (slot.Value, slot.Dispatch);
        }

        private static (TState state, Action<TAction> dispatch) UpdateReducerSlot<TState, TAction>(
            ComponentFiber fiber, int index, Func<TState, TAction, TState> reducer)
        {
            if (fiber.StateSlots![index] is not ReducerSlot<TState, TAction> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseReducer", fiber.StateSlots[index].GetType(),
                    $"ReducerSlot<{typeof(TState).Name},{typeof(TAction).Name}>", index);
            }

            typed.Reducer = reducer;
            return (typed.Value, typed.Dispatch);
        }

        #endregion

        #region Use (Suspense)

        /// <summary>
        /// Declaratively fetches asynchronous data; while pending, throws a <see cref="FiberSuspendSignal"/>
        /// to delegate the fallback to the nearest Suspense Boundary.
        /// </summary>
        /// <remarks>
        /// The hook is keyed by the resource identity, not by a dependency array:
        /// the same resource returns the same result; presenting a different resource starts a new fetch.
        /// The optional <paramref name="resourceKey"/> identifies the resource (compared by reference identity).
        /// When omitted, the factory delegate's identity is the key — a stable (cached) factory reuses the running
        /// resource, while a fresh closure each render is treated as a new resource. Pass a stable
        /// <paramref name="resourceKey"/> (e.g. the query id) when the factory is a fresh closure but the
        /// resource is logically the same.
        /// </remarks>
        /// <typeparam name="T">Resolved value type.</typeparam>
        /// <param name="factory">Factory that returns a UniTask producing the value. Must not be null.</param>
        /// <param name="resourceKey">Identity of the resource. When null, the factory delegate is the key.</param>
        /// <returns>The resolved value once the resource completes successfully.</returns>
        public static T Use<T>(Func<UniTask<T>> factory, object? resourceKey = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return UseCore<T>(_ => factory(), resourceKey ?? factory, resourceKeyExplicit: resourceKey != null, "Use");
        }

        /// <summary>
        /// CancellationToken-aware variant of <see cref="Use{T}(Func{UniTask{T}}, object)"/>.
        /// The token is cancelled when the resource is superseded (a new key) or the component unmounts;
        /// loader implementations are responsible for honoring it and aborting.
        /// </summary>
        /// <typeparam name="T">Resolved value type.</typeparam>
        /// <param name="factory">Factory that receives a CancellationToken and returns a UniTask producing the value. Must not be null.</param>
        /// <param name="resourceKey">Identity of the resource. When null, the factory delegate is the key.</param>
        /// <returns>The resolved value once the resource completes successfully.</returns>
        public static T Use<T>(Func<CancellationToken, UniTask<T>> factory, object? resourceKey = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return UseCore<T>(factory, resourceKey ?? factory, resourceKeyExplicit: resourceKey != null, "Use");
        }

        private static T UseCore<T>(Func<CancellationToken, UniTask<T>> factory, object resourceKey, bool resourceKeyExplicit, string hookName)
        {
            var fiber = Resolve(hookName);
            var slots = fiber.AsyncSlots;
            var index = fiber.NextAsyncSlotIndex();
            // Cache on the Fiber to avoid allocating a closure on every render.
            var onCompleted = fiber.AsyncResourceCompletedCallback ??= () =>
            {
                if (fiber.IsDisposed) return;
                FiberRenderer.NotifyAsyncResourceCompleted(fiber);
            };

            FiberAsyncResource<T> resource;
            if (index >= slots.Count)
            {
                resource = new FiberAsyncResource<T>(resourceKey) { OnCompleted = onCompleted };
                slots.Add(resource);
                resource.Start(factory);
            }
            else
            {
                var existing = slots[index];
                // Reuse while the resource identity is unchanged (by reference): the same
                // resource returns the same result without re-fetching.
                if (existing is FiberAsyncResource<T> typed && ObjectIs.AreEqual(typed.ResourceKey, resourceKey))
                {
                    resource = typed;
                }
                else
                {
#if UNITY_EDITOR
                    // Footgun: when the caller omits resourceKey, the factory delegate identity
                    // becomes the key — and a fresh inline lambda on every render restarts the
                    // resource permanently (never-resolving suspense). Only warn when the user did
                    // NOT pass an explicit resourceKey, so a deliberate method-group key (e.g.
                    // `Hooks.Use(store.LoadAsync)`) is not flagged.
                    if (!resourceKeyExplicit && existing is FiberAsyncResource<T>)
                    {
                        FiberLogger.LogWarning(hookName,
                            $"{hookName}<{typeof(T).Name}>: factory delegate identity changed across renders. " +
                            "Pass a stable `resourceKey` (e.g. the query id) so the resource is not restarted every render.");
                    }
#endif
                    existing?.Dispose();
                    resource = new FiberAsyncResource<T>(resourceKey) { OnCompleted = onCompleted };
                    slots[index] = resource;
                    resource.Start(factory);
                }
            }

            return resource.Status switch
            {
                FiberAsyncResourceStatus.Success => resource.Result,
                FiberAsyncResourceStatus.Error => throw resource.Error!,
                _ => throw FiberSuspendSignal.Instance,
            };
        }

        #endregion

        #region UseFallback (functional Error Boundary)

        /// <summary>
        /// Registers a fallback factory inside Render() of an `[Component(IsErrorBoundary = true)]`
        /// component. When this component catches a Render exception from a child, the registered factory
        /// is invoked to produce the fallback VNode.
        /// This API expresses, in functional Velvet, the Error Boundary pattern of returning a
        /// fallback when a descendant render throws.
        /// Must be called during Render since it is overwritten on every render.
        /// </summary>
        /// <remarks>
        /// When called from a non-EB component (`[Component]` with IsErrorBoundary=false), the
        /// factory is still stored on the fiber, but RenderFallback is only reached on the Error
        /// Boundary path, so it is effectively a no-op. Do not call this hook from non-EB components.
        /// <para/>
        /// <b>Bubble-up</b>: when the factory returns <c>null</c> or itself throws, the exception
        /// bubbles to the next enclosing Error Boundary, ultimately reaching the root as an
        /// unhandled exception when no boundary catches it.
        /// </remarks>
        /// <param name="factory">Factory that receives the caught exception and returns the fallback VNode. Must not be null.</param>
        public static void UseFallback(Func<Exception, VNode> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            UseFallback((ex, _) => factory(ex));
        }

        /// <summary>
        /// Two-argument fallback factory variant that receives the caught exception and an
        /// <see cref="ErrorInfo"/> with the throwing fiber's <c>ComponentStack</c>, describing where
        /// the error was caught.
        /// </summary>
        /// <remarks>
        /// Same bubble-up semantics as the single-arg overload: returning <c>null</c> or throwing
        /// re-propagates the original exception to the next enclosing Error Boundary.
        /// </remarks>
        /// <param name="factory">Factory that receives <c>(exception, info)</c> and returns the fallback VNode. Must not be null.</param>
        public static void UseFallback(Func<Exception, ErrorInfo, VNode> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var fiber = Resolve(nameof(UseFallback));
            fiber.FallbackFactory = factory;
        }

        #endregion

        #region UseTransition

        /// <summary>
        /// Returns the pending state and starter for low-priority updates.
        /// </summary>
        /// <returns>
        /// 2-tuple in the order (<c>isPending</c>, <c>startTransition</c>):
        /// - <c>isPending</c>: true while a Transition update is queued or being committed (and across an async
        ///   transition's awaits).
        /// - <c>startTransition</c>: a <see cref="TransitionStarter"/>. Call <c>startTransition.Invoke(() =&gt; ...)</c>
        ///   for synchronous updates or <c>startTransition.Invoke(async () =&gt; ...)</c> for async actions whose
        ///   <c>isPending</c> stays true across awaits. Nested calls join the outer transition.
        /// </returns>
        /// <remarks>Equivalent to React's <c>useTransition</c> (<c>[isPending, startTransition]</c>) for users migrating from React.</remarks>
        public static (bool isPending, TransitionStarter startTransition) UseTransition()
        {
            var fiber = Resolve("UseTransition");
            fiber.TransitionSlots ??= new List<HookTransitionSlot>();
            var index = fiber.Indices.TransitionHookIndex++;

            if (index >= fiber.TransitionSlots.Count)
            {
                var slot = new HookTransitionSlot();
                // The starter captures this slot so each UseTransition() drives only its own pending flag:
                // two transitions in one component are independent. Built once so the
                // returned starter is reference-stable across renders (safe to place in a dependency array).
                slot.Starter = new TransitionStarter(
                    updates => FiberWorkLoop.StartTransition(fiber, slot, updates),
                    asyncUpdates => FiberWorkLoop.StartTransition(fiber, slot, asyncUpdates).Forget());
                fiber.TransitionSlots.Add(slot);
                return (slot.IsPending, slot.Starter);
            }

            var existing = fiber.TransitionSlots[index];
            return (existing.IsPending, existing.Starter);
        }

        #endregion

        #region UseMutation

        /// <summary>
        /// Returns a handle that tracks the lifecycle (Idle / Pending / Success / Error) of an async mutation.
        /// The caller invokes <c>Mutate</c> / <c>MutateAsync</c> with
        /// <typeparamref name="TVariables"/>; the <see cref="MutationOptions{TVariables, TData}.MutationFn"/> runs
        /// and the handle's status / data / error fields are updated, triggering a re-render.
        /// </summary>
        /// <typeparam name="TVariables">Mutation input variables type. Use <see cref="Unit"/> for "no variables".</typeparam>
        /// <typeparam name="TData">Mutation result type. Use <see cref="Unit"/> for "no return value".</typeparam>
        /// <param name="options">The mutation function plus optional success / error callbacks.</param>
        /// <returns>A <see cref="MutationResult{TVariables, TData}"/> whose reference is stable across renders.</returns>
        public static MutationResult<TVariables, TData> UseMutation<TVariables, TData>(
            MutationOptions<TVariables, TData> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var fiber = Resolve("UseMutation");
            fiber.MutationSlots ??= new List<HookMutationSlot>();
            var index = fiber.Indices.MutationHookIndex++;

            if (index >= fiber.MutationSlots.Count)
            {
                var slot = new HookMutationSlot<TVariables, TData>
                {
                    Result = new MutationResult<TVariables, TData>(),
                    MutationFn = options.MutationFn,
                    OnSuccess = options.OnSuccess,
                    OnError = options.OnError,
                };
                slot.Result.MutateAction = variables => RunMutationAsync(fiber, slot, variables, rethrowOnFailure: false).Forget();
                slot.Result.MutateAsyncFunc = variables => RunMutationAsync(fiber, slot, variables, rethrowOnFailure: true);
                slot.Result.ResetAction = () => ResetMutation(fiber, slot);
                fiber.MutationSlots.Add(slot);
                return slot.Result;
            }

            if (fiber.MutationSlots[index] is not HookMutationSlot<TVariables, TData> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseMutation", fiber.MutationSlots[index].GetType(),
                    $"HookMutationSlot<{typeof(TVariables).Name}, {typeof(TData).Name}>", index);
            }

            // Refresh closure-captured options (latest render's MutationFn / callbacks).
            typed.MutationFn = options.MutationFn;
            typed.OnSuccess = options.OnSuccess;
            typed.OnError = options.OnError;
            return typed.Result;
        }

        /// <summary>
        /// Void-return overload of <see cref="UseMutation{TVariables, TData}"/>. Use when the mutation function
        /// returns no data (typical for Store-action mutations where state is updated internally).
        /// </summary>
        public static MutationResult<TVariables, Unit> UseMutation<TVariables>(
            MutationOptions<TVariables> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return UseMutation(new MutationOptions<TVariables, Unit>(
                MutationFn: async (v, ct) =>
                {
                    await options.MutationFn(v, ct);
                    return Unit.Default;
                },
                OnSuccess: options.OnSuccess is { } onSuccess ? (_, v) => onSuccess(v) : null,
                OnError: options.OnError));
        }

        /// <summary>
        /// No-input void overload of <see cref="UseMutation{TVariables, TData}"/>. Use when the mutation takes
        /// no input and returns no data. Pair with <see cref="MutationResultExtensions.Mutate{TData}"/> to call
        /// <c>mutation.Mutate()</c> without an explicit <c>Unit.Default</c> argument.
        /// </summary>
        public static MutationResult<Unit, Unit> UseMutation(MutationOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            return UseMutation(new MutationOptions<Unit, Unit>(
                MutationFn: async (_, ct) =>
                {
                    await options.MutationFn(ct);
                    return Unit.Default;
                },
                OnSuccess: options.OnSuccess is { } onSuccess ? (_, _) => onSuccess() : null,
                OnError: options.OnError is { } onError ? (ex, _) => onError(ex) : null));
        }

        // rethrowOnFailure distinguishes the two call shapes: mutateAsync (true) returns a task its caller
        // awaits, so a failure must reject; mutate (false) is fire-and-forget (dispatched via .Forget()) and
        // reports failures through onError / the Error status only — it must never rethrow, or the forgotten
        // task would surface an unobserved exception with no observer that can act on it.
        private static async UniTask<TData> RunMutationAsync<TVariables, TData>(
            ComponentFiber fiber,
            HookMutationSlot<TVariables, TData> slot,
            TVariables variables,
            bool rethrowOnFailure)
        {
            if (fiber.IsDisposed) return default!;

            // Cancel any superseded in-flight mutation. The latest call wins.
            slot.Cts?.Cancel();
            slot.Cts?.Dispose();
            var cts = new CancellationTokenSource();
            slot.Cts = cts;

            slot.Result.Status = MutationStatus.Pending;
            slot.Result.Variables = variables;
            slot.Result.Error = null;
            RequestRender(fiber);

            try
            {
                var data = await slot.MutationFn(variables, cts.Token);
                if (slot.Cts != cts || fiber.IsDisposed) return data;
                slot.Result.Data = data;
                slot.Result.Status = MutationStatus.Success;
                slot.OnSuccess?.Invoke(data, variables);
                RequestRender(fiber);
                return data;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return default!;
            }
            catch (Exception ex)
            {
                // Superseded (a newer call replaced slot.Cts) or the owner was disposed: the result is stale, so
                // neither deliver onError nor mutate the slot. mutateAsync still rejects so its awaiter observes
                // the failure; fire-and-forget mutate swallows (no awaiter — a rethrow would leak unobserved).
                if (slot.Cts != cts || fiber.IsDisposed)
                {
                    if (rethrowOnFailure) throw;
                    return default!;
                }
                slot.Result.Error = ex;
                slot.Result.Status = MutationStatus.Error;
                slot.OnError?.Invoke(ex, variables);
                RequestRender(fiber);
                // mutate() reports failures via onError / the Error status only and never raises an unhandled
                // rejection; mutateAsync() rejects so the awaiter can catch it.
                if (rethrowOnFailure) throw;
                return default!;
            }
        }

        private static void ResetMutation<TVariables, TData>(
            ComponentFiber fiber,
            HookMutationSlot<TVariables, TData> slot)
        {
            if (fiber.IsDisposed) return;
            slot.Result.Status = MutationStatus.Idle;
            slot.Result.Data = default;
            slot.Result.Error = null;
            slot.Result.Variables = default;
            RequestRender(fiber);
        }

        #endregion

        #region UseDeferredValue

        /// <summary>
        /// Defers commits of <paramref name="value"/> changes
        /// through the Transition lane and returns the previous value during urgent re-renders.
        /// Use this to deprioritize heavy re-render inputs such as search queries.
        /// </summary>
        /// <remarks>
        /// Change detection matches <see cref="UseStore{TStore,TSel}"/>:
        /// reference equality for objects, NaN-aware for float/double. There is no comparer argument.
        /// </remarks>
        /// <typeparam name="T">Type of the value being deferred.</typeparam>
        /// <param name="value">Latest value (provided by the caller).</param>
        /// <returns>
        /// First render: returns <paramref name="value"/> as-is.
        /// Subsequent renders: returns the previously committed value and queues the next value as pending
        /// on the Transition lane.
        /// Render after a Transition flush: commits the pending value and returns the new value.
        /// </returns>
        public static T UseDeferredValue<T>(T value)
            => UseDeferredValueCore(value, default!, hasInitialValue: false);

        /// <summary>
        /// Overload taking an <paramref name="initialValue"/>. On the initial render the
        /// hook returns <paramref name="initialValue"/> instead of <paramref name="value"/> and immediately
        /// schedules a Transition-lane re-render that defers toward <paramref name="value"/>. Subsequent
        /// renders behave exactly like <see cref="UseDeferredValue{T}(T)"/>.
        /// </summary>
        /// <typeparam name="T">Type of the value being deferred.</typeparam>
        /// <param name="value">Latest value (provided by the caller).</param>
        /// <param name="initialValue">Value returned on the initial render only.</param>
        /// <returns>The initial render returns <paramref name="initialValue"/>; thereafter same as the single-arg overload.</returns>
        public static T UseDeferredValue<T>(T value, T initialValue)
            => UseDeferredValueCore(value, initialValue, hasInitialValue: true);

        private static T UseDeferredValueCore<T>(T value, T initialValue, bool hasInitialValue)
        {
            var fiber = Resolve("UseDeferredValue");
            fiber.DeferredValueSlots ??= new List<HookDeferredValueSlot>();
            var index = fiber.Indices.DeferredValueHookIndex++;

            if (index >= fiber.DeferredValueSlots.Count)
            {
                if (hasInitialValue && !ObjectIs.AreEqual(initialValue, value))
                {
                    // The first render returns initialValue and schedules a Transition-lane render
                    // that defers toward the real value. The current commit holds initialValue; the pending
                    // value is the target.
                    var seeded = new HookDeferredValueSlot<T> { Current = initialValue, Pending = value, HasPending = true };
                    fiber.DeferredValueSlots.Add(seeded);
                    FiberWorkLoop.RequestTransitionRerender(fiber);
                    return initialValue;
                }

                // First render without an initialValue (or initialValue equals value): commit value as-is.
                var slot = new HookDeferredValueSlot<T> { Current = value };
                fiber.DeferredValueSlots.Add(slot);
                return value;
            }

            if (fiber.DeferredValueSlots[index] is not HookDeferredValueSlot<T> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseDeferredValue", fiber.DeferredValueSlots[index].GetType(),
                    $"HookDeferredValueSlot<{typeof(T).Name}>", index);
            }

            if (typed.HasPending && ObjectIs.AreEqual(typed.Pending, value))
            {
                // Commit phase after a Transition flush: promote pending to current and return the new value.
                typed.Current = value;
                typed.Pending = default;
                typed.HasPending = false;
                return typed.Current;
            }

            if (ObjectIs.AreEqual(typed.Current, value))
            {
                // No value change: return current as-is.
                // If the input has reverted to Current, clear any ghost Pending (a stale deferred value)
                // so that the next change can be deferred correctly. (Without this, an alpha->beta->alpha
                // sequence would leave Pending=beta and would commit beta immediately the next time it arrives.)
                if (typed.HasPending)
                {
                    typed.Pending = default;
                    typed.HasPending = false;
                }
                return typed.Current;
            }

            // Value change detected: store the new value as pending and queue a Transition lane render.
            // It will be committed on the next Transition flush.
            typed.Pending = value;
            typed.HasPending = true;
            FiberWorkLoop.RequestTransitionRerender(fiber);
            return typed.Current;
        }

        #endregion

        #region UseOptimistic

        /// <summary>
        /// Returns the optimistic state and an
        /// <c>addOptimistic</c> action. Normally the returned state equals <paramref name="passthroughState"/>.
        /// When <c>addOptimistic(action)</c> is invoked, <paramref name="applyOptimistic"/> derives an
        /// optimistic state that is shown immediately (a re-render is requested) while the real update is in
        /// flight; once <paramref name="passthroughState"/> changes (the real update lands), the optimistic
        /// override is discarded and the pass-through state is shown again.
        /// </summary>
        /// <typeparam name="TState">Optimistic state type.</typeparam>
        /// <typeparam name="TAction">Action / payload type passed to <paramref name="applyOptimistic"/>.</typeparam>
        /// <param name="passthroughState">The authoritative state. Shown when no optimistic update is outstanding.</param>
        /// <param name="applyOptimistic">Pure reducer <c>(currentState, action) =&gt; optimisticState</c>. Must not be null.</param>
        /// <returns>
        /// 2-tuple:
        /// - <c>optimisticState</c>: the optimistic state while an update is outstanding, otherwise the pass-through state.
        /// - <c>addOptimistic</c>: applies an optimistic action; the override is cleared when the pass-through state changes.
        /// </returns>
        public static (TState optimisticState, Action<TAction> addOptimistic) UseOptimistic<TState, TAction>(
            TState passthroughState, Func<TState, TAction, TState> applyOptimistic)
        {
            if (applyOptimistic == null) throw new ArgumentNullException(nameof(applyOptimistic));
            var fiber = Resolve("UseOptimistic");
            fiber.OptimisticSlots ??= new List<HookOptimisticSlot>();
            var index = fiber.Indices.OptimisticHookIndex++;

            if (index >= fiber.OptimisticSlots.Count)
            {
                var slot = new HookOptimisticSlot<TState, TAction>
                {
                    Base = passthroughState,
                    OptimisticState = passthroughState,
                    HasOptimistic = false,
                    Apply = applyOptimistic,
                };
                slot.Add = CreateOptimisticAdd(slot, fiber);
                fiber.OptimisticSlots.Add(slot);
                return (slot.OptimisticState, slot.Add);
            }

            if (fiber.OptimisticSlots[index] is not HookOptimisticSlot<TState, TAction> typed)
            {
                throw HookSlotTypeMismatch(fiber, "UseOptimistic", fiber.OptimisticSlots[index].GetType(),
                    $"HookOptimisticSlot<{typeof(TState).Name}, {typeof(TAction).Name}>", index);
            }

            // Refresh the apply function (it may capture the latest render's scope).
            typed.Apply = applyOptimistic;

            if (!ObjectIs.AreEqual(typed.Base, passthroughState))
            {
                // The authoritative state changed (the real update landed): adopt it and drop the optimistic
                // override, resetting the optimistic state once the update completes.
                typed.Base = passthroughState;
                typed.OptimisticState = passthroughState;
                typed.HasOptimistic = false;
            }

            return (typed.HasOptimistic ? typed.OptimisticState : typed.Base, typed.Add);
        }

        private static Action<TAction> CreateOptimisticAdd<TState, TAction>(
            HookOptimisticSlot<TState, TAction> slot, ComponentFiber fiber)
        {
            return action =>
            {
                if (fiber.IsDisposed) return;
                // Layer onto the already-optimistic value so multiple addOptimistic calls compose.
                var current = slot.HasOptimistic ? slot.OptimisticState : slot.Base;
                slot.OptimisticState = slot.Apply(current, action);
                slot.HasOptimistic = true;
                RequestRender(fiber);
            };
        }

        #endregion

        #region Memoization (component-level)

        /// <summary>
        /// Memoization slot accessor used exclusively by SG-generated code for
        /// <c>[Component(Memoize = true)]</c>.
        /// On each Render, advances <see cref="HookIndexTable.MemoHookIndex"/> to allocate a slot in
        /// Fiber.MemoSlots; returns the cached VNode when the previous deps are equal.
        /// Do not call by hand (the slot index convention is a position-based API fixed by SG).
        /// When the return value is <c>false</c>, <paramref name="cached"/> is undefined.
        /// </summary>
        /// <param name="deps">Dependency values for this slot. Must not be null.</param>
        /// <param name="slotIndex">Outputs the allocated slot index; must be passed back to <see cref="StoreMemoizedVNode"/>.</param>
        /// <param name="cached">Outputs the cached VNode on a hit; undefined on a miss.</param>
        /// <returns>True when the previous deps are equal and the cached VNode was returned.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool TryGetMemoizedVNode(object?[]? deps, out int slotIndex, out VNode? cached)
        {
            if (deps == null) throw new ArgumentNullException(nameof(deps));
            var fiber = Resolve("TryGetMemoizedVNode");
            // SG-emitted code is contracted to call this exactly once per render, so Count is at most
            // slotIndex or slotIndex+1.
            // Append a single entry to allocate the slot at the end of the list.
            slotIndex = fiber.Indices.MemoHookIndex++;
            fiber.MemoSlots ??= new List<HookMemoSlot>();
            if (fiber.MemoSlots.Count <= slotIndex)
            {
                fiber.MemoSlots.Add(new HookMemoSlot());
            }
            var slot = fiber.MemoSlots[slotIndex];
            // Compare by identity (reference identity for reference types, raw bits for float/double),
            // matching the strictness the reconciler and Provider use to decide a re-render. Structural value
            // equality would treat a fresh-but-equal record prop or context value as unchanged and return a
            // stale cached VNode, suppressing a re-render the framework actually committed. Identity comparison keeps the
            // memo sound for props- and context-driven inputs that the weaver now captures in the deps array.
            if (slot.LastDeps != null && ObjectIs.AreEqualDeps(slot.LastDeps, deps))
            {
                // Hit against the committed deps: reuse the committed VNode and stage the committed values so the
                // render-phase settle keeps them. (On a miss, StoreMemoizedVNode stages the rebuilt result; without
                // this hit-side staging a discarded attempt's miss would otherwise be the last thing staged.)
                slot.NextDeps = slot.LastDeps;
                slot.NextCachedResult = slot.CachedResult;
                cached = slot.CachedResult;
                return true;
            }
            cached = null!;
            return false;
        }

        /// <summary>
        /// Slot writer API called by SG-generated code on the miss path of <see cref="TryGetMemoizedVNode"/>.
        /// The <paramref name="slotIndex"/> argument must be the value returned by the immediately
        /// preceding <see cref="TryGetMemoizedVNode"/> call.
        /// </summary>
        /// <param name="slotIndex">Slot index obtained from the immediately preceding <see cref="TryGetMemoizedVNode"/> call.</param>
        /// <param name="deps">Dependency values to record for this slot. Must not be null.</param>
        /// <param name="result">VNode to cache for subsequent hits.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void StoreMemoizedVNode(int slotIndex, object?[]? deps, VNode result)
        {
            if (deps == null) throw new ArgumentNullException(nameof(deps));
            var fiber = Resolve("StoreMemoizedVNode");
#if UNITY_EDITOR
            // The StrictMode diagnostic render is isolated from the commit: staging its throwaway
            // output here would first spare that tree from the diagnostic's own retirement sweep
            // (the mark reads NextCachedResult) and then strand it when the next real render
            // restages the slot — one leaked subtree per double render. Mirrors the other
            // externally-visible hook writes the diagnostic pass no-ops.
            if (fiber.IsStrictDiagnosticPass) return;
#endif
            if (fiber.MemoSlots == null || slotIndex < 0 || slotIndex >= fiber.MemoSlots.Count)
            {
                throw new InvalidOperationException(
                    $"{ComponentName(fiber)}: StoreMemoizedVNode slotIndex ({slotIndex}) is invalid."
                    + " Did you obtain slotIndex from TryGetMemoizedVNode immediately beforehand?");
            }
            var slot = fiber.MemoSlots[slotIndex];
            // Stage the freshly computed memo. The committed LastDeps / CachedResult are promoted when the
            // render-phase loop settles, so a discarded intermediate attempt cannot poison the committed baseline.
            slot.NextDeps = deps;
            slot.NextCachedResult = result;
        }

        #endregion

        #region Internals

        private static void RequestRender(ComponentFiber fiber)
        {
            FiberWorkLoop.RequestRenderFromHook(fiber);
        }

        private static Action? WrapDisposable(Func<IDisposable> factory)
        {
            var disposable = factory.Invoke();
            return disposable == null ? null : disposable.Dispose;
        }

        internal static string ComponentName(ComponentFiber? fiber)
        {
            var method = fiber!.Body?.Method;
            if (method == null) return "[Component]";
            var displayName = ComponentMethodRegistry.TryGetDisplayName(method);
            if (displayName != null) return displayName;
            var type = method.DeclaringType?.Name;
            return type != null ? $"{type}.{method.Name}" : method.Name;
        }

        // Builds the Rules-of-Hooks violation raised when a hook slot's stored type does not match the type
        // the current render expects (the slot order changed between renders). actual is the stored slot's
        // runtime type; expectedDisplay is the human-readable name of the type this render wants. Shared by
        // every position-based hook so the message stays identical across them.
        private static InvalidOperationException HookSlotTypeMismatch(
            ComponentFiber? fiber, string hookName, Type actual, string expectedDisplay, int index)
            => new InvalidOperationException(
                $"{ComponentName(fiber)}: {hookName} type changed between the previous render ({actual.Name})" +
                $" and the current render ({expectedDisplay}) (slot #{index}). This violates the Rules of Hooks.");

        #endregion
    }

}
