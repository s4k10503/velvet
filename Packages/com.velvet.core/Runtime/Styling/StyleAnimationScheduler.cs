#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Manages the animation lifecycle for AnimatePresenceNode.
    // schedule.Execute-based class swapping plus timeout management.
    // transition-duration and transition-timing-function are set as inline styles, with C# as the
    // Single Source of Truth.
    internal sealed class StyleAnimationScheduler
    {
        // Grace time after a CSS transition completes (ms).
        // UIToolkit's schedule.Execute runs on the next frame, so 50ms is set to absorb the
        // 60fps (16ms) - 30fps (33ms) frame delay.
        private const long AnimationGraceMs = 50;
        private const float MaxDurationSec = 10f;
        private const int MaxPoolSize = 16;
        // Spring tick interval (~60fps), matching the shadow co-fade tick and StyleAnimateDriver's own cadence.
        private const long SpringTickMs = 16;

        // Static cache from EasingMode to List<EasingFunction>. EasingMode values are finite, so caching is safe.
        private static readonly Dictionary<EasingMode, List<EasingFunction>> s_easingCache = new();

#if UNITY_EDITOR
        // The cache lingers in EditorTest environments without Domain Reload, but it is safe because
        // the same EasingMode always produces the same EasingFunction instance.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields() => s_easingCache.Clear();
#endif

        private readonly Dictionary<VisualElement, PendingAnimation> _pendingExits = new();
        private readonly Dictionary<VisualElement, PendingAnimation> _pendingEnters = new();
        // Plain class-diff variant swaps (parent→child label propagation, staggerChildren/delayChildren — see
        // PlayDelayedVariantSwap) awaiting their deferred inline transition-delay clear. Separate from the
        // enter/exit maps above: a delayed swap has no from/to class lifecycle of its own (the class diff
        // already happened via SyncClassDrivenStyling before this is called), so PendingAnimation's
        // enter/exit-only fields would all sit unused.
        private readonly Dictionary<VisualElement, PendingDelayedSwap> _pendingDelayedSwaps = new();
        private readonly Stack<List<TimeValue>> _durationPool = new();
        private readonly Stack<List<TimeValue>> _delayPool = new();

        // Starts the mount animation.
        // 1. Adds EnterFromClass and sets duration / easing as inline styles.
        // 2. On the next frame, removes EnterFromClass and adds EnterToClass (firing the CSS transition).
        // 3. After the duration, removes EnterToClass and clears inline styles (clean state).
        // additionalDelaySec:
        // Extra delay (seconds) added on top of the StyleTransitionConfig delay.
        // Used by AnimatePresenceNode.StaggerSec to sequentially delay child elements.
        // 0 (default) means no extra delay.
        public void PlayEnter(VisualElement? element, StyleTransitionConfig? config, Action? onComplete = null, float additionalDelaySec = 0f)
        {
            if (element == null || config == null)
            {
                return;
            }

            if (config.Type == TransitionType.Spring)
            {
                // Reachable only in principle: a classic (non-variant) transition's EnterFromClass/EnterToClass
                // are internal-setter-only, so a caller-authored spring config passed here has none set and this
                // no-ops to an immediate complete (see StartSpringVariant) — kept for completeness/symmetry with
                // PlayVariantEnter / PlayExit rather than silently ignoring Type on this call path.
                StartSpringVariant(element, config.EnterFromClasses, config.EnterToClasses, config.DelaySec,
                    config.Stiffness, config.Damping, config.Mass, onComplete, additionalDelaySec,
                    _pendingEnters, restingClasses: null, isExit: false);
                return;
            }

            // Classic transition enter: the to-classes are a TRANSIENT overlay (resting state = the element's
            // base classes), so they are removed on completion (variantMode: false). Per-property overrides are
            // wired only where a variant swap sets transition-property: all (see PlayVariantEnter / PlayExit); a
            // preset's own USS-declared transition-property is untouched, so PropertyOverrides is not read here.
            PlayEnterInternal(element, config.EnterFromClasses, config.EnterToClasses,
                config.DurationSec, config.Easing, config.DelaySec, onComplete, additionalDelaySec,
                variantMode: false, propertyOverrides: null);
        }

        // Variant-driven enter (initial → animate). Unlike PlayEnter, the
        // element already carries the toClasses (the resting variants[animate], applied
        // at create) when this is called: step 1 strips them to reveal the fromClasses
        // (variants[initial]), step 2 swaps back to the to-classes (firing the transition), and — the key
        // difference — the to-classes are KEPT after completion, since they ARE the persistent resting state
        // (the animate target persists). A zero/invalid duration leaves the element at its
        // already-applied resting state (no strip, mounts directly at animate).
        // type/stiffness/damping/mass: the enclosing StyleTransitionConfig's spring knobs (Tween/100/10/1 by
        // default — the caller passes the config's own values through, since this overload takes the
        // already-unpacked timing primitives rather than the config itself).
        public void PlayVariantEnter(VisualElement? element, string[]? fromClasses, string[]? toClasses,
            float durationSec, EasingMode easing, float delaySec, Action? onComplete = null, float additionalDelaySec = 0f,
            IReadOnlyList<StylePropertyTransition>? propertyOverrides = null,
            TransitionType type = TransitionType.Tween, float stiffness = 100f, float damping = 10f, float mass = 1f)
        {
            if (element == null)
            {
                return;
            }

            if (type == TransitionType.Spring)
            {
                StartSpringVariant(element, fromClasses ?? System.Array.Empty<string>(), toClasses ?? System.Array.Empty<string>(),
                    delaySec, stiffness, damping, mass, onComplete, additionalDelaySec,
                    _pendingEnters, restingClasses: null, isExit: false);
                return;
            }

            PlayEnterInternal(element, fromClasses ?? System.Array.Empty<string>(), toClasses ?? System.Array.Empty<string>(),
                durationSec, easing, delaySec, onComplete, additionalDelaySec, variantMode: true, propertyOverrides);
        }

        private void PlayEnterInternal(VisualElement element, string[] fromClasses, string[] toClasses,
            float durationSec, EasingMode easing, float delaySec, Action? onComplete, float additionalDelaySec, bool variantMode,
            IReadOnlyList<StylePropertyTransition>? propertyOverrides)
        {
            // DurationSec=0 / invalid: complete immediately. For variantMode this happens BEFORE any strip, so
            // the element keeps its already-applied resting (to) classes and mounts directly at animate.
            if (!ValidateDuration(durationSec, onComplete))
            {
                return;
            }

            // Cancel any existing enter animation.
            CancelEnter(element);

            var staggerDelayMs = (long)(additionalDelaySec * 1000);

            // Step 1: set duration / easing as inline styles, then show the from-state. In variantMode the
            // element already carries the resting to-classes, so strip them first so they don't fight the from-state.
            var (durationList, delayList) = ApplyTransitionStyles(element, durationSec, easing, delaySec,
                allProperties: variantMode, propertyOverrides: propertyOverrides);
            if (variantMode)
            {
                StyleAnimationClassUtils.RemoveClasses(element, toClasses);
            }
            StyleAnimationClassUtils.AddClasses(element, fromClasses);

            // Step 2: swap classes on the next frame (fires the CSS transition).
            var pending = new PendingAnimation
            {
                FromClasses = fromClasses,
                ToClasses = toClasses,
                DurationList = durationList,
                DelayList = delayList,
                AnimatingElement = element,
            };
            // Co-fade drop-shadows with the element instead of hiding them: register this enter as a shadow
            // driver at the from-value (0 = invisible) NOW (synchronously, before the next-frame swap) so there
            // is no first-frame flash, then the tick (started at the swap) ramps each shadow to follow the
            // caster's opacity. Released on completion / cancel.
            pending.Shadows = CollectShadowsForCoFade(element, pending, 0f);
            _pendingEnters[element] = pending;

            // schedule.Execute does not fire when the panel is disconnected, but CancelEnter is invoked
            // via Reconciler.RemoveElement / CleanupDescendants, so the dictionary does not leak.
            var startAction = new Action(() =>
            {
                if (!_pendingEnters.ContainsKey(element))
                {
                    return;
                }

                StyleAnimationClassUtils.RemoveClasses(element, fromClasses);
                StyleAnimationClassUtils.AddClasses(element, toClasses);
                // The CSS opacity transition is now firing — start sampling the caster's opacity each frame so
                // descendant shadows fade in lockstep with it.
                StartShadowCoFadeTick(pending);

                // Step 3: after the duration, clear inline styles. Classic enter also removes the transient
                // to-classes; variantMode KEEPS them (they are the persistent resting variant).
                var durationMs = (long)(durationSec * 1000) + (long)(delaySec * 1000) + AnimationGraceMs;
                var timeout = element.schedule.Execute(() =>
                {
                    if (_pendingEnters.Remove(element, out var completed))
                    {
                        if (!variantMode)
                        {
                            StyleAnimationClassUtils.RemoveClasses(element, toClasses);
                        }
                        // Target is opaque now — stop the co-fade and restore the shadows to full strength.
                        EndShadowCoFade(completed);
                        ClearTransitionStyles(element);
                        ReturnDurationList(completed.DurationList);
                        ReturnDelayList(completed.DelayList);
                        onComplete?.Invoke();
                    }
                });
                timeout.ExecuteLater(durationMs);
                pending.TimeoutItem = timeout;
            });

            if (staggerDelayMs > 0)
            {
                // With extra delay: start after staggerDelayMs.
                var scheduled = element.schedule.Execute(startAction);
                scheduled.ExecuteLater(staggerDelayMs);
                pending.ScheduledItem = scheduled;
            }
            else
            {
                // No extra delay: run on the next frame (matches existing behavior).
                pending.ScheduledItem = element.schedule.Execute(startAction);
            }
        }

        // Starts the unmount animation.
        // 1. Adds ExitFromClass and sets duration / easing as inline styles.
        // 2. On the next frame, removes ExitFromClass and adds ExitToClass (firing the CSS transition).
        // 3. After the duration, invokes onComplete (the element is removed).
        // restoreFromOnCancel:
        // When true, the exit's FromClasses are the persistent resting state (a variant exit's
        // variants[animate]); cancelling the exit (the key is re-added mid-exit) re-applies them so the
        // element returns to its resting variant instead of being left without it. Default false (preset exits,
        // whose FromClasses are transient).
        // additionalDelaySec:
        // Extra delay before the exit transition fires, on top of config.DelaySec. Used for exit
        // staggering (each removed child delayed by stagger × its index), mirroring the enter stagger.
        public void PlayExit(VisualElement? element, StyleTransitionConfig? config, Action? onComplete,
            bool restoreFromOnCancel = false, float additionalDelaySec = 0f)
        {
            if (element == null || config == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (config.Type == TransitionType.Spring)
            {
                // Deferred to attach when off-panel: a presence exit can start while its subtree is transiently
                // detached by a keyed reorder (see the tween exit's own ScheduleOnHost below) and this exit must
                // still eventually complete so the reconciler's ghost-removal re-render fires.
                StartSpringVariant(element, config.ExitFromClasses, config.ExitToClasses, config.DelaySec,
                    config.Stiffness, config.Damping, config.Mass, onComplete, additionalDelaySec,
                    _pendingExits, restingClasses: restoreFromOnCancel ? config.ExitFromClasses : null,
                    isExit: true);
                return;
            }

            // StyleTransitionConfig.None (DurationSec=0): complete immediately (no warning).
            if (!ValidateDuration(config.DurationSec, onComplete))
            {
                return;
            }

            // Cancel any existing exit animation.
            CancelExit(element);

            var fromClasses = config.ExitFromClasses;
            var toClasses = config.ExitToClasses;
            var exitEasing = config.ExitEasing ?? config.Easing;

            // Step 1: add the exit initial-state class and set duration / easing as inline styles. A variant exit
            // (restoreFromOnCancel) swaps variant utility classes, so it needs transition-property: all to tween
            // — the same condition that gates reading PropertyOverrides (a preset exit's own USS-declared
            // transition-property is untouched).
            var (durationList, delayList) = ApplyTransitionStyles(element, config.DurationSec, exitEasing,
                config.DelaySec, allProperties: restoreFromOnCancel,
                propertyOverrides: restoreFromOnCancel ? config.PropertyOverrides : null);
            StyleAnimationClassUtils.AddClasses(element, fromClasses);

            // Step 2: swap classes on the next frame.
            var pending = new PendingAnimation
            {
                FromClasses = fromClasses,
                ToClasses = toClasses,
                RestingClasses = restoreFromOnCancel ? fromClasses : null,
                DurationList = durationList,
                DelayList = delayList,
                AnimatingElement = element,
            };
            // Co-fade drop-shadows OUT with the element instead of hiding them: register this exit as a shadow
            // driver at the from-value (1 = opaque, the element's current state); the tick (started at the swap)
            // then ramps each shadow down to follow the caster's fading opacity. Released on completion / cancel.
            pending.Shadows = CollectShadowsForCoFade(element, pending, 1f);
            _pendingExits[element] = pending;

            var staggerDelayMs = (long)(additionalDelaySec * 1000);

            // Schedule the exit's frame callbacks on a STABLE host (the panel root), not on the exiting
            // element itself. UI Toolkit silently drops an element's scheduled items the moment it leaves the
            // panel, and a reconcile reorder briefly detaches a still-exiting ghost (RemoveFromHierarchy +
            // re-Insert) to move it — which would drop the startAction/timeout, stall the exit, and leak the
            // ghost (it never completes, so the diff never removes it). The panel root never detaches during
            // the presence's life, so its scheduled items always fire; the exiting child only needs to stay
            // attached for its CSS opacity tween (it does — a committed ghost stays in the DOM). Exit
            // completion is driven from a global frame loop rather than a per-node timer.
            void ScheduleOnHost()
            {
                // Schedule only if this exact exit is still the live one. A deferred (off-panel) exit can be
                // cancelled or superseded by a newer PlayExit before the element attaches; the stale attach
                // callback must not then schedule on top of the replacement.
                if (!_pendingExits.TryGetValue(element, out var cur) || !ReferenceEquals(cur, pending))
                {
                    return;
                }

                var panel = element.panel;
                if (panel == null)
                {
                    return;
                }
                var host = panel.visualTree;
                var startAction = new Action(() =>
                {
                    if (!_pendingExits.ContainsKey(element))
                    {
                        return;
                    }

                    StyleAnimationClassUtils.RemoveClasses(element, fromClasses);
                    StyleAnimationClassUtils.AddClasses(element, toClasses);
                    // The CSS opacity fade-out is now firing — sample the caster's opacity each frame on the
                    // stable host so descendant shadows fade out in lockstep (and keep ticking through any
                    // reconcile-reorder detach of the exiting ghost, which is why the host is the panel root).
                    StartShadowCoFadeTick(pending);

                    // Step 3: invoke onComplete after the duration.
                    var durationMs = (long)(config.DurationSec * 1000) + (long)(config.DelaySec * 1000) + AnimationGraceMs;
                    var timeout = host.schedule.Execute(() =>
                    {
                        if (_pendingExits.Remove(element, out var completed))
                        {
                            EndShadowCoFade(completed);
                            ReturnDurationList(completed.DurationList);
                            ReturnDelayList(completed.DelayList);
                            onComplete?.Invoke();
                        }
                    });
                    timeout.ExecuteLater(durationMs);
                    pending.TimeoutItem = timeout;
                });

                // Delay the swap by the stagger offset (each removed child fades on its turn), else run next frame.
                if (staggerDelayMs > 0)
                {
                    var scheduled = host.schedule.Execute(startAction);
                    scheduled.ExecuteLater(staggerDelayMs);
                    pending.ScheduledItem = scheduled;
                }
                else
                {
                    pending.ScheduledItem = host.schedule.Execute(startAction);
                }
            }

            // A stable host only exists once the element is attached. If it is off-panel at exit start (the
            // presence boundary reconciled while its subtree was temporarily detached), defer scheduling until
            // the element attaches — scheduling on the still-detached element here would put the exit's
            // callbacks on a host that UI Toolkit drops the next time the element moves, stalling the exit and
            // leaking the ghost (the very failure the stable-host scheduling exists to prevent).
            if (element.panel != null)
            {
                ScheduleOnHost();
            }
            else
            {
                DeferUntilAttached(element, pending, ScheduleOnHost);
            }
        }

        // Defers an action until the element attaches to a panel — shared by any animation that can start
        // while its element is off-panel (freshly created but not yet inserted, or transiently detached by a
        // keyed reorder) and therefore has no working schedule / panel-root host to run on yet. The callback
        // unregisters itself once it fires; pending.PendingAttach is tracked so a cancel-before-attach (see
        // CancelPending) can remove the still-dangling registration instead of leaking it — and the closure
        // pinning the caller's PendingAnimation — on the element across pool reuse.
        private static void DeferUntilAttached(VisualElement element, PendingAnimation pending, Action onAttach)
        {
            EventCallback<AttachToPanelEvent>? handler = null;
            handler = _ =>
            {
                element.UnregisterCallback(handler);
                pending.PendingAttach = null;
                onAttach();
            };
            element.RegisterCallback(handler);
            pending.PendingAttach = handler;
        }

        // Starts a spring-driven variant enter/exit (StyleTransitionConfig.Type == Spring). Unlike the tween
        // path, a spring needs no CSS-transition-triggering frame boundary, so the from→to class swap lands
        // IMMEDIATELY at rest; MotionSpringClassParser then resolves whatever numeric channels that swap
        // touches (opacity / translate / scale / rotate) into a from/to pair each, and a per-frame physics tick
        // (StartSpringTick) drives them via inline styles until they settle — replacing the tween's fixed-
        // duration completion timeout with a dynamic settle check.
        // map: the caller's own map (_pendingEnters for an enter, _pendingExits for an exit) so a later
        // CancelEnter/CancelExit/CancelAll finds this entry exactly like a tween one.
        // restingClasses: non-null only for a variant exit (restoreFromOnCancel) — see PendingAnimation.RestingClasses.
        // isExit: selects the exit-shaped self-cancel (mirrors calling the PUBLIC CancelExit rather than
        // CancelEnter below). Both directions defer the tick start until attach when off-panel: a standalone
        // Motion's enter plays during element creation (FiberNodeFactory), and a presence enter plays before
        // the entering element is placed into the tree (GeneralPathReconciler) — so BOTH, like a presence
        // exit, can start while still detached (see PlayExit's own ScheduleOnHost/AttachToPanelEvent for the
        // same rationale on the exit side).
        private void StartSpringVariant(VisualElement element, string[] fromClasses, string[] toClasses,
            float delaySec, float stiffness, float damping, float mass, Action? onComplete, float additionalDelaySec,
            Dictionary<VisualElement, PendingAnimation> map, string[]? restingClasses, bool isExit)
        {
            var plan = MotionSpringClassParser.Resolve(fromClasses, toClasses);
            // An invalid configuration (see ValidateSpringParameters) degrades exactly like an empty plan
            // below: no state is built, so the shared "land the classes, complete immediately" branch handles
            // it without a separate code path.
            var state = ValidateSpringParameters(stiffness, damping, mass)
                ? MotionSpringDriver.Create(plan, stiffness, damping, mass)
                : null;

            // Cancel any existing animation of this SAME flavor first (mirrors PlayEnterInternal's
            // CancelEnter(element) / PlayExit's CancelExit(element) self-cancel).
            CancelPending(map, element, animateReversal: isExit);

            // Land the classes at rest either way — nothing recognized to animate (or invalid spring
            // parameters) degrades to a plain, instantaneous class swap (the spring equivalent of a
            // zero-duration tween).
            StyleAnimationClassUtils.RemoveClasses(element, fromClasses);
            StyleAnimationClassUtils.AddClasses(element, toClasses);

            if (state == null)
            {
                onComplete?.Invoke();
                return;
            }

            MotionSpringDriver.ApplyCurrentValues(element, state);

            var pending = new PendingAnimation
            {
                FromClasses = fromClasses,
                ToClasses = toClasses,
                RestingClasses = restingClasses,
                AnimatingElement = element,
                Spring = state,
            };
            state.OnSettled = onComplete;
            map[element] = pending;
            pending.OwningMap = map;

            void ScheduleStart()
            {
                // Superseded before it ever got to start (a cancel-before-attach removed this exact pending).
                if (!map.TryGetValue(element, out var current) || !ReferenceEquals(current, pending))
                {
                    return;
                }
                var totalDelayMs = (long)((delaySec + additionalDelaySec) * 1000);
                if (totalDelayMs <= 0)
                {
                    StartSpringTick(element, pending);
                    return;
                }

                // A delayed start is parked on the panel-root host, not element.schedule: ScheduleStart only
                // ever runs once attached (called directly below, or from DeferUntilAttached's onAttach), but
                // a keyed reorder can transiently detach the element again during the delay window itself,
                // and UI Toolkit silently drops a detached element's own scheduled items (mirrors PlayExit's
                // ScheduleOnHost rationale). The host should therefore always be available here; the null
                // guard is defensive (mirrors StartSpringTick's own should-not-happen bail) rather than an
                // expected path.
                var host = element.panel?.visualTree;
                if (host == null)
                {
                    return;
                }
                var scheduled = host.schedule.Execute(() =>
                {
                    // Re-check on fire, not just on schedule: the host outlives a transient detach, so this
                    // closure can still run after a later cancel/supersede replaced this exact pending.
                    if (map.TryGetValue(element, out var stillCurrent) && ReferenceEquals(stillCurrent, pending))
                    {
                        StartSpringTick(element, pending);
                    }
                });
                scheduled.ExecuteLater(totalDelayMs);
                pending.ScheduledItem = scheduled;
            }

            if (element.panel != null)
            {
                ScheduleStart();
            }
            else
            {
                DeferUntilAttached(element, pending, ScheduleStart);
            }
        }

        // Starts the recurring spring tick on the panel root — the stable host, mirroring the shadow co-fade
        // tick's own rationale: a keyed reorder can transiently detach the animating element, and UI Toolkit
        // silently drops a DETACHED element's own scheduled items, but the panel root never detaches during the
        // tree's life. Each tick reads the elapsed time from the SAME clock the scheduler itself used to decide
        // when to fire this callback (TimerState.deltaTime, backed by Panel.TimeSinceStartupMs — the panel's
        // own time source, which a test's simulated panel overrides) rather than sampling a different clock
        // (e.g. Time.realtimeSinceStartupAsDouble) that could disagree with it: a hitch is still absorbed by
        // SpringIntegrator's own dt clamp, but the elapsed time now always matches what actually elapsed on the
        // clock this tick is scheduled against. No-op if there is no host (should not happen for the on-panel /
        // already-deferred-to-attach cases this is called from, but this guards rather than throws).
        private void StartSpringTick(VisualElement element, PendingAnimation pending)
        {
            var state = pending.Spring;
            if (state == null)
            {
                return;
            }
            var host = element.panel?.visualTree;
            if (host == null)
            {
                return;
            }

            state.Tick = host.schedule.Execute((TimerState ts) =>
            {
                // TimerState.start is the previous callback's time for a repeating item (or the schedule time
                // for the first firing), so deltaTime is already exactly the elapsed interval this tick needs
                // — no separate "last tick" bookkeeping to maintain.
                var dt = ts.deltaTime / 1000f;
                if (dt <= 0f)
                {
                    return;
                }

                var settled = MotionSpringDriver.Step(element, state, dt);
                if (!settled)
                {
                    return;
                }

                state.Tick?.Pause();
                state.Tick = null;
                if (pending.OwningMap != null && pending.OwningMap.TryGetValue(element, out var current)
                    && ReferenceEquals(current, pending))
                {
                    pending.OwningMap.Remove(element);
                }
                EndShadowCoFade(pending); // No-op: a spring-driven entry never registers a shadow co-fade driver.
                MotionSpringDriver.ClearInlineOverrides(element, state);
                state.OnSettled?.Invoke();
            }).Every(SpringTickMs);
        }

        // Cancels the exit animation on the given element and removes the applied CSS classes; the
        // element reverses toward its resting classes with the transition kept alive (the inline
        // transition styles are cleared only after the reversal has run its course).
        public void CancelExit(VisualElement element) => CancelPending(_pendingExits, element, animateReversal: true);

        // Cancels the exit animation on an element being torn down for good (pool return / disposal) — never
        // hands off to a reversal, regardless of whether the element is still attached at the moment this
        // runs. FiberElementCleaner releases scheduler resources BEFORE the caller physically detaches the
        // element (DOM operations are the caller's own job), so element.panel can still be non-null here even
        // though the element is on its way to the pool. An ordinary CancelExit's reversal hand-off assumes the
        // element keeps living: for a spring, it re-adds a live entry into the enter map whose recurring,
        // panel-root-scheduled tick keeps calling MotionSpringDriver.Step and writing inline styles — nothing
        // ever calls CancelEnter on this element again to catch that re-added entry, so it would otherwise
        // keep corrupting whatever the pooled element is reused for next.
        public void CancelExitForTeardown(VisualElement element) =>
            CancelPending(_pendingExits, element, animateReversal: true, forTeardown: true);

        // Cancels the enter animation on the given element and removes the applied CSS classes and inline styles.
        public void CancelEnter(VisualElement element) => CancelPending(_pendingEnters, element);

        // Whether the given element is currently exiting.
        public bool IsExiting(VisualElement element) => _pendingExits.ContainsKey(element);

        // Applies an ADDITIONAL inline transition-delay to a plain class-diff variant swap — the parent→child
        // label-propagation path (FiberNodePatcher.PatchMotion's staggerChildren/delayChildren orchestration),
        // which swaps classes directly with no enter/exit lifecycle of its own and no transition-property:all
        // override, relying on the element's OWN utility classes (e.g. transition-opacity duration-300) to
        // declare whatever is to be delayed. totalDelaySec is this swap's FULL transition-delay (the Motion's
        // own configured DelaySec plus any staggerChildren/delayChildren orchestration offset — the caller
        // already added them); durationSec is that swap's own transition duration, used only to size the
        // deferred clear below. A non-positive totalDelaySec is a no-op.
        // The delay is cleared once the swap's transition would have finished, so a LATER, unrelated patch on
        // the same element does not inherit a stale delay. On a real panel the clear is deferred via
        // schedule.Execute (mirrors PlayEnter/PlayExit's own completion timing); off-panel there is nothing to
        // interpolate against, and schedule.Execute never fires for a detached element, so the delay is never
        // applied in the first place instead of setting up a schedule that would never run — net-equivalent to
        // CancelExit's reversal cleanup, which clears an already-applied delay immediately off-panel for the
        // same reason.
        public void PlayDelayedVariantSwap(VisualElement? element, float totalDelaySec, float durationSec)
        {
            if (element == null || totalDelaySec <= 0f)
            {
                return;
            }

            // Cancel any previous still-parked delay on this element first, so an interrupted stagger (a second
            // label flip before the first swap's grace period elapsed) does not leave a stale timer racing
            // this one's clear (also covers a rare case where an ON-panel schedule was parked and the element
            // has since gone off-panel: schedule.Execute never fires for a detached element, so that stale
            // entry would otherwise leak).
            CancelDelayedVariantSwap(element);

            if (element.panel == null)
            {
                // Nothing to interpolate without a panel, and schedule.Execute never fires for a detached
                // element, so there is nothing worth setting in the first place — mirrors CancelExit's
                // off-panel-immediate-clear rule for a reversal cleanup.
                return;
            }

            var delayMs = (int)(totalDelaySec * 1000);
            var delayList = RentDelayList(delayMs);
            element.style.transitionDelay = delayList;
            var pending = new PendingDelayedSwap { DelayList = delayList };
            var timeoutMs = (long)(totalDelaySec * 1000) + (long)(durationSec * 1000) + AnimationGraceMs;
            var timeout = element.schedule.Execute(() =>
            {
                if (_pendingDelayedSwaps.Remove(element, out var completed))
                {
                    element.style.transitionDelay = StyleKeyword.Null;
                    ReturnDelayList(completed.DelayList);
                }
            });
            timeout.ExecuteLater(timeoutMs);
            pending.TimeoutItem = timeout;
            _pendingDelayedSwaps[element] = pending;
        }

        // Cancels a still-parked delayed-swap clear (superseded by a follow-up swap, or the element being torn
        // down) and clears the inline transition-delay immediately. Safe to call when none is pending (no-op).
        public void CancelDelayedVariantSwap(VisualElement element)
        {
            if (_pendingDelayedSwaps.Remove(element, out var pending))
            {
                pending.TimeoutItem?.Pause();
                element.style.transitionDelay = StyleKeyword.Null;
                ReturnDelayList(pending.DelayList);
            }
        }

        // Cancels every animation and removes the applied CSS classes and inline styles.
        public void CancelAll()
        {
            CancelAllInMap(_pendingExits);
            CancelAllInMap(_pendingEnters);
            CancelAllDelayedSwaps();
        }

        private void CancelAllDelayedSwaps()
        {
            foreach (var (element, pending) in _pendingDelayedSwaps)
            {
                pending.TimeoutItem?.Pause();
                element.style.transitionDelay = StyleKeyword.Null;
                ReturnDelayList(pending.DelayList);
            }
            _pendingDelayedSwaps.Clear();
        }

        private static bool ValidateDuration(float durationSec, Action? onComplete)
        {
            if (durationSec == 0f)
            {
                onComplete?.Invoke();
                return false;
            }
            if (durationSec < 0f || durationSec > MaxDurationSec)
            {
                UnityEngine.Debug.LogWarning(
                    $"[StyleAnimationScheduler] Invalid DurationSec: {durationSec}. Expected 0 < duration <= {MaxDurationSec}.");
                onComplete?.Invoke();
                return false;
            }
            return true;
        }

        // Mirrors ValidateDuration's guard, for the spring path: a non-finite or non-positive stiffness/damping
        // makes SpringIntegrator.Step's settle predicate unsatisfiable forever — zero/negative stiffness never
        // pulls the value toward its target, zero/negative damping never dissipates velocity, and NaN
        // propagates into every inline style write and never compares equal to anything (including itself), so
        // IsSettled never returns true. Left unvalidated, the panel-root tick this drives would run
        // indefinitely and its completion callback — the ONLY thing that removes a presence exit's ghost —
        // would never fire. Mass gets its own numeric safety clamp inside SpringIntegrator.Step (a non-positive
        // mass would otherwise divide by zero or flip the restoring force's sign), but that clamp has no way to
        // warn the caller, so it is still validated here for the same failure modes.
        private static bool ValidateSpringParameters(float stiffness, float damping, float mass)
        {
            if (float.IsFinite(stiffness) && stiffness > 0f
                && float.IsFinite(damping) && damping > 0f
                && float.IsFinite(mass) && mass > 0f)
            {
                return true;
            }

            FiberLogger.LogWarning("Spring",
                $"Invalid spring parameters (stiffness={stiffness}, damping={damping}, mass={mass}). " +
                "Expected finite, positive values for all three. Completing immediately instead of ticking forever.");
            return false;
        }

        // Sets transition-duration and transition-timing-function as inline styles.
        // C# becomes the Single Source of Truth, so they need not be defined in USS.
        // GC tuning: the EasingFunction list is cached statically per EasingMode; TimeValue lists are
        // reused via per-instance pools.
        // transition-property: all — a shared, never-mutated list (StyleList retains the reference as-is, so a
        // static instance is safe; ClearTransitionStyles releases it via StyleKeyword.Null).
        private static readonly List<UnityEngine.UIElements.StylePropertyName> s_allTransitionProperties =
            new() { new UnityEngine.UIElements.StylePropertyName("all") };

        private (List<TimeValue> durationList, List<TimeValue>? delayList) ApplyTransitionStyles(
            VisualElement element, float durationSec, EasingMode easing, float delaySec = 0f, bool allProperties = false,
            IReadOnlyList<StylePropertyTransition>? propertyOverrides = null)
        {
            // Per-property overrides replace the "all" catch-all with an explicit property list — reachable only
            // where a variant swap would otherwise set transition-property: all (allProperties), matching the
            // contract documented on StyleTransitionConfig.PropertyOverrides. Every other combination (no
            // overrides, or a preset transition that never sets allProperties) falls through unchanged below.
            if (allProperties && propertyOverrides is { Count: > 0 })
            {
                return ApplyPropertyOverrideTransitionStyles(element, durationSec, easing, delaySec, propertyOverrides);
            }

            var durationMs = (int)(durationSec * 1000);
            var durationList = RentDurationList(durationMs);
            element.style.transitionDuration = durationList;
            element.style.transitionTimingFunction = GetOrCreateEasingList(easing);

            // Variant animations swap user utility classes (e.g. opacity-0 ↔ opacity-100) that carry no
            // transition-* of their own, so UITK has no property to tween and the swap would snap. Set
            // transition-property: all so the changed computed values animate (a variant tween supplies just
            // a duration). Preset transitions keep their USS transition-property and don't pass this flag.
            if (allProperties)
            {
                element.style.transitionProperty = s_allTransitionProperties;
            }

            // When delaySec <= 0, transition-delay is not set (negative values are ignored, as documented in StyleTransitionConfig).
            List<TimeValue>? delayList = null;
            if (delaySec > 0f)
            {
                var delayMs = (int)(delaySec * 1000);
                delayList = RentDelayList(delayMs);
                element.style.transitionDelay = delayList;
            }

            return (durationList, delayList);
        }

        // Per-property override path: transition-property becomes EXACTLY the overridden properties (in
        // declaration order) instead of "all" — matching CSS semantics where an explicit transition-property list
        // transitions only what it names. Duration / delay are positionally-matched n-entry lists rented from the
        // SAME pools the single-entry path above uses (RentDurationList / RentDelayList n-entry overloads), so
        // they are returned through the existing PendingAnimation.DurationList / DelayList bookkeeping unchanged.
        // A null override field falls back to the value the caller already resolved for this direction
        // (durationSec / easing / delaySec — for an exit, easing here is already ExitEasing ?? Easing, so the
        // fallback stays direction-correct without this method needing to know enter from exit).
        // The property-name and easing lists are rebuilt each call instead of pooled: PropertyOverrides is a
        // handful of entries fixed at config-authoring time, so the allocation is bounded and one-shot per
        // animation start (never per-frame) — but the per-mode EasingFunction INSTANCES are still reused via the
        // existing static cache (GetOrCreateEasingList) rather than reallocated, mirroring its intent.
        private (List<TimeValue> durationList, List<TimeValue>? delayList) ApplyPropertyOverrideTransitionStyles(
            VisualElement element, float defaultDurationSec, EasingMode defaultEasing, float defaultDelaySec,
            IReadOnlyList<StylePropertyTransition> overrides)
        {
            var count = overrides.Count;
            var propertyNames = new List<StylePropertyName>(count);
            var easingList = new List<EasingFunction>(count);
            var durationMs = new int[count];
            var delayMs = new int[count];
            var hasDelay = false;
            for (var i = 0; i < count; i++)
            {
                var o = overrides[i];
                propertyNames.Add(new StylePropertyName(o.Property));
                easingList.Add(GetOrCreateEasingList(o.Easing ?? defaultEasing)[0]);
                durationMs[i] = (int)((o.DurationSec ?? defaultDurationSec) * 1000);
                var delaySec = o.DelaySec ?? defaultDelaySec;
                if (delaySec > 0f)
                {
                    hasDelay = true;
                    delayMs[i] = (int)(delaySec * 1000);
                }
            }

            var durationList = RentDurationList(durationMs);
            element.style.transitionProperty = propertyNames;
            element.style.transitionDuration = durationList;
            element.style.transitionTimingFunction = easingList;

            // Mirrors the single-entry path: transition-delay is set only when at least one property actually
            // needs one (an all-zero delay list is behaviorally identical to leaving it unset).
            List<TimeValue>? delayList = null;
            if (hasDelay)
            {
                delayList = RentDelayList(delayMs);
                element.style.transitionDelay = delayList;
            }

            return (durationList, delayList);
        }

        // If the timeout callback already ran, map.Remove returns false and the cancellation is skipped.
        // This is safe because the classes have already been cleaned up in that case.
        // forTeardown: true only from CancelExitForTeardown — the element is being torn down for good (pool
        // return / disposal), not merely interrupted, so a reversal (tween or spring) is never appropriate
        // even when animateReversal is requested and the element still happens to be attached: see
        // CancelExitForTeardown for why handing off to one would corrupt the element after it is pooled.
        private void CancelPending(Dictionary<VisualElement, PendingAnimation> map, VisualElement element,
            bool animateReversal = false, bool forTeardown = false)
        {
            if (map.Remove(element, out var pending))
            {
                // Pause() corresponds to cancelling a one-shot schedule produced by schedule.Execute().
                // Removing from the dictionary also makes the ContainsKey check inside the callback fail,
                // providing defense in depth.
                pending.ScheduledItem?.Pause();
                pending.TimeoutItem?.Pause();
                // Remove the off-panel deferred-attach callback if it never fired (cancel-before-attach), else it
                // and its captured PendingAnimation linger on the element across pool reuse.
                if (pending.PendingAttach != null) element.UnregisterCallback(pending.PendingAttach);
                StyleAnimationClassUtils.RemoveClasses(element, pending.FromClasses);
                StyleAnimationClassUtils.RemoveClasses(element, pending.ToClasses);
                // A variant exit's FromClasses ARE the resting state (variants[animate]); cancelling the exit
                // (key re-added mid-exit) must return the element to that resting variant rather than strip it.
                // Re-add after the removals so the element is left in its resting state and stays consistent with
                // the MotionAppliedClasses cache (which still records the resting class as applied).
                if (pending.RestingClasses != null)
                {
                    StyleAnimationClassUtils.AddClasses(element, pending.RestingClasses);
                }
                // Interrupted enter / exit: the target returns to its resting (opaque) state, so stop this
                // tween's co-fade and drop its driver — the shadow snaps back to full (product collapses to 1)
                // unless an enclosing fade still drives it.
                EndShadowCoFade(pending);

                if (pending.Spring != null)
                {
                    var spring = pending.Spring;
                    if (!forTeardown && animateReversal && element.panel != null && spring.Tick != null)
                    {
                        // Hand off to a reversal spring: retarget every channel toward the value it STARTED
                        // from (continuity — each channel's SpringIntegrator instance, and therefore its
                        // current value/velocity, is untouched by this), drop the original completion (a
                        // reversal settling is not "finishing" anything the original caller asked for), and
                        // move ownership into the enter map — mirroring the tween reversal's own move into
                        // _pendingEnters below. The recurring tick keeps running uninterrupted throughout;
                        // only its targets and its eventual finalize action change. Requires a tick that has
                        // actually started (spring.Tick != null): a cancel that lands before then — still
                        // parked behind its delay — has no running tick to keep alive, and nothing would ever
                        // start one for it (its ScheduledItem was already paused above, and a still-off-panel
                        // PendingAttach was already unregistered), so handing off here would just park a dead
                        // entry in _pendingEnters forever instead of finalizing below.
                        MotionSpringDriver.Retarget(spring);
                        spring.OnSettled = null;
                        CancelPending(_pendingEnters, element);
                        _pendingEnters[element] = pending;
                        pending.OwningMap = _pendingEnters;
                    }
                    else
                    {
                        // No reversal (a plain CancelEnter, an off-panel exit with nothing left to interpolate
                        // against, a cancel before the tick ever started, or a teardown cancel that must never
                        // hand off regardless): stop the tick now and drop the inline overrides immediately.
                        spring.Tick?.Pause();
                        spring.Tick = null;
                        MotionSpringDriver.ClearInlineOverrides(element, spring);
                    }
                    return;
                }

                if (!forTeardown && animateReversal && element.panel != null && pending.DurationList is { Count: > 0 })
                {
                    // A cancelled exit retargets a still-attached element back to its resting
                    // classes. Clearing the inline transition styles in this same call would make
                    // the next style resolve snap straight to the resting values; keep the
                    // transition alive instead so the panel interpolates from the currently
                    // resolved value, and defer the clear (and list return) until the reversal has
                    // run its course. Off-panel there is nothing to interpolate (and scheduling
                    // would plant a fresh deferred-attach callback), so clear immediately.
                    ScheduleReversalCleanup(element, pending);
                }
                else
                {
                    ClearTransitionStyles(element);
                    ReturnDurationList(pending.DurationList);
                    ReturnDelayList(pending.DelayList);
                }
            }
        }

        // Parks the cancelled animation's transition styles until the reversal tween completes,
        // then clears them and reclaims the lists. The parked entry lives in the ENTER map — the
        // reversal is a motion toward the resting state — so a follow-up enter/exit on the same
        // element cancels it like any other pending animation instead of racing its deferred
        // cleanup (the recursive CancelPending call takes the non-reversal branch: the parked entry
        // carries only RestingClasses, which re-adding is idempotent).
        private void ScheduleReversalCleanup(VisualElement element, PendingAnimation pending)
        {
            CancelPending(_pendingEnters, element);
            var reversal = new PendingAnimation
            {
                RestingClasses = pending.RestingClasses,
                DurationList = pending.DurationList,
                DelayList = pending.DelayList,
            };
            var timeoutMs = MaxReversalTimeoutMs(pending.DurationList, pending.DelayList);
            var timeout = element.schedule.Execute(() =>
            {
                if (_pendingEnters.Remove(element))
                {
                    ClearTransitionStyles(element);
                    ReturnDurationList(reversal.DurationList);
                    ReturnDelayList(reversal.DelayList);
                }
            });
            timeout.ExecuteLater((long)timeoutMs);
            reversal.TimeoutItem = timeout;
            _pendingEnters[element] = reversal;
        }

        // How long the reversal must stay alive before it is safe to clear the inline transition styles: the
        // SLOWEST animating property's delay + duration. For the single-entry case (no PropertyOverrides) that
        // is just duration[0] + delay[0], same as before; PropertyOverrides can give each property its own
        // duration / delay, so an interrupted variant exit's reversal must wait for whichever one finishes
        // last, not just the first, or a slower property's transition-duration would be cleared (snapping it to
        // the resting value) while it is still mid-tween.
        private static float MaxReversalTimeoutMs(List<TimeValue>? durationList, List<TimeValue>? delayList)
        {
            if (durationList is not { Count: > 0 })
            {
                return 0f;
            }
            var maxMs = 0f;
            for (var i = 0; i < durationList.Count; i++)
            {
                var ms = durationList[i].value;
                if (delayList is { Count: > 0 })
                {
                    ms += delayList[Math.Min(i, delayList.Count - 1)].value;
                }
                if (ms > maxMs)
                {
                    maxMs = ms;
                }
            }
            return maxMs;
        }

        private void CancelAllInMap(Dictionary<VisualElement, PendingAnimation> map)
        {
            foreach (var (element, pending) in map)
            {
                pending.ScheduledItem?.Pause();
                pending.TimeoutItem?.Pause();
                if (pending.PendingAttach != null) element.UnregisterCallback(pending.PendingAttach);
                StyleAnimationClassUtils.RemoveClasses(element, pending.FromClasses);
                StyleAnimationClassUtils.RemoveClasses(element, pending.ToClasses);
                EndShadowCoFade(pending);
                if (pending.Spring != null)
                {
                    // A hard stop, no reversal: pause the tick and drop the inline overrides it owns (the
                    // tween-only ClearTransitionStyles/ReturnDurationList/ReturnDelayList below are no-ops for
                    // a spring entry, which never touches transition-* styles or rents a TimeValue list).
                    pending.Spring.Tick?.Pause();
                    MotionSpringDriver.ClearInlineOverrides(element, pending.Spring);
                }
                ClearTransitionStyles(element);
                ReturnDurationList(pending.DurationList);
                ReturnDelayList(pending.DelayList);
            }
            map.Clear();
        }

        // Collects every drop-shadow paint under an element (the element itself and its descendants) and
        // registers this animation as a co-fade driver on each at the given start factor (0 for an enter,
        // 1 for an exit). The shadow is painted as a baked quad in the caster's own generateVisualContent and
        // does NOT honor UI Toolkit opacity (neither inherited from an animating ancestor nor inline), so while
        // a FadeSlideUp / Fade tweens the target's opacity the scheduler samples that opacity each frame
        // (StartShadowCoFadeTick) and scales each shadow's alpha by it — the shadow fades WITH its element
        // instead of being hidden then popping in. The returned list lets completion / cancel end the SAME
        // co-fade without re-walking the subtree; null when there are none (the common case) so nothing is
        // retained and no tick is scheduled. The driver token is the PendingAnimation, and a binding's opacity
        // is the PRODUCT of its active drivers, so a nested animation whose own fade completes first does NOT
        // reveal a shadow an enclosing, still-running fade also covers.
        private static List<(VisualElement element, DropShadowBinding binding)>? CollectShadowsForCoFade(
            VisualElement element, object driver, float startFactor)
        {
            List<(VisualElement, DropShadowBinding)>? shadows = null;
            CollectShadows(element, ref shadows);
            if (shadows == null)
            {
                return null;
            }
            foreach (var (el, binding) in shadows)
            {
                DropShadowSilhouette.SetCoFade(binding, el, driver, startFactor);
            }
            return shadows;
        }

        // Depth-first walk gathering each element that carries a shadow paint binding. The shadow is the
        // caster's own paint, not a separate child element, so the binding is looked up per element via
        // DropShadowSilhouette's side-channel.
        private static void CollectShadows(VisualElement element,
            ref List<(VisualElement, DropShadowBinding)>? shadows)
        {
            var binding = DropShadowSilhouette.TryGet(element);
            if (binding != null)
            {
                (shadows ??= new List<(VisualElement, DropShadowBinding)>()).Add((element, binding));
            }
            var count = element.childCount;
            for (var i = 0; i < count; i++)
            {
                CollectShadows(element[i], ref shadows);
            }
        }

        // Starts the recurring co-fade tick: every frame, sample the animating element's current (transition-
        // interpolated) opacity and push it to each collected descendant shadow, so they fade in lockstep with
        // the element. Scheduled on the PANEL ROOT (not the animating element) so a keyed reorder that briefly
        // detaches the subtree — UI Toolkit drops a detached element's scheduled items — does not stall the
        // fade. No-op when the subtree carries no shadows (the common case), so a shadowless animation costs
        // nothing. Paused by EndShadowCoFade on completion / cancel.
        private static void StartShadowCoFadeTick(PendingAnimation pending)
        {
            if (pending.Shadows == null)
            {
                return;
            }
            var animatingElement = pending.AnimatingElement;
            if (animatingElement == null)
            {
                return;
            }
            var host = animatingElement.panel?.visualTree;
            if (host == null)
            {
                return;
            }
            pending.ShadowTick = host.schedule.Execute(() =>
            {
                var raw = animatingElement.resolvedStyle.opacity;
                var factor = float.IsNaN(raw) ? 1f : UnityEngine.Mathf.Clamp01(raw);
                foreach (var (el, binding) in pending.Shadows)
                {
                    DropShadowSilhouette.SetCoFade(binding, el, pending, factor);
                }
            }).Every(16);
        }

        // Stops the co-fade tick and drops this animation's driver from each shadow (null-safe; balanced
        // one-for-one with CollectShadowsForCoFade). When a shadow's last driver is removed it returns to full
        // strength; an enclosing, still-running fade keeps driving it.
        private static void EndShadowCoFade(PendingAnimation pending)
        {
            pending.ShadowTick?.Pause();
            if (pending.Shadows == null)
            {
                return;
            }
            foreach (var (el, binding) in pending.Shadows)
            {
                DropShadowSilhouette.EndCoFade(binding, el, pending);
            }
        }

        private void ClearTransitionStyles(VisualElement element)
        {
            // Cleared via the implicit conversion from StyleKeyword to StyleList<T>.
            // The clear releases UIElements' internal list reference, making pool return safe.
            element.style.transitionDuration = StyleKeyword.Null;
            element.style.transitionTimingFunction = StyleKeyword.Null;
            element.style.transitionDelay = StyleKeyword.Null;
            // Release the variant transition-property: all (set by ApplyTransitionStyles for variant swaps).
            // A no-op for preset transitions, which never set it inline (USS provides transition-property).
            element.style.transitionProperty = StyleKeyword.Null;
        }

        // StyleList<T> retains the List reference as-is (no copy), so cached lists must not be mutated
        // after creation.
        // The reference is released when StyleKeyword.Null is assigned in ClearTransitionStyles, so it is safe.
        private static List<EasingFunction> GetOrCreateEasingList(EasingMode easing)
        {
            if (!s_easingCache.TryGetValue(easing, out var list))
            {
                list = new List<EasingFunction>(1) { new(easing) };
                s_easingCache[easing] = list;
            }
            return list;
        }

        private List<TimeValue> RentDurationList(int ms) => RentTimeValueList(_durationPool, ms);
        private List<TimeValue> RentDurationList(IReadOnlyList<int> msValues) => RentTimeValueList(_durationPool, msValues);
        private void ReturnDurationList(List<TimeValue>? list) => ReturnTimeValueList(_durationPool, list);
        private List<TimeValue> RentDelayList(int ms) => RentTimeValueList(_delayPool, ms);
        private List<TimeValue> RentDelayList(IReadOnlyList<int> msValues) => RentTimeValueList(_delayPool, msValues);
        private void ReturnDelayList(List<TimeValue>? list) => ReturnTimeValueList(_delayPool, list);

        // Single-entry hot path (unchanged): every enter / exit without PropertyOverrides rents exactly one of
        // these per animation.
        private static List<TimeValue> RentTimeValueList(Stack<List<TimeValue>> pool, int ms)
        {
            if (!pool.TryPop(out var list))
            {
                list = new List<TimeValue>(1);
            }
            else
            {
                list.Clear();
            }
            list.Add(new TimeValue(ms, TimeUnit.Millisecond));
            return list;
        }

        // n-entry sibling for PropertyOverrides: builds a positionally-matched TimeValue per override, reusing
        // the SAME pool as the single-entry path above (a rented list's capacity just grows to whatever size it
        // was last asked for; Clear()+Add() reuse is agnostic to entry count) — so ReturnTimeValueList below
        // already handles returning either shape without change.
        private static List<TimeValue> RentTimeValueList(Stack<List<TimeValue>> pool, IReadOnlyList<int> msValues)
        {
            if (!pool.TryPop(out var list))
            {
                list = new List<TimeValue>(msValues.Count);
            }
            else
            {
                list.Clear();
            }
            for (var i = 0; i < msValues.Count; i++)
            {
                list.Add(new TimeValue(msValues[i], TimeUnit.Millisecond));
            }
            return list;
        }

        private static void ReturnTimeValueList(Stack<List<TimeValue>> pool, List<TimeValue>? list)
        {
            if (list != null && pool.Count < MaxPoolSize)
            {
                pool.Push(list);
            }
        }

        // State of an in-progress animation. Fields are sufficient since this is a private sealed class.
        // Created and referenced only inside StyleAnimationScheduler.
        private sealed class PendingAnimation
        {
            public IVisualElementScheduledItem? ScheduledItem;
            public IVisualElementScheduledItem? TimeoutItem;
            public string[]? FromClasses;
            public string[]? ToClasses;
            // Classes to RE-ADD when this animation is cancelled (interrupted). Set for a variant-driven exit
            // whose FromClasses ARE the persistent resting state (variants[animate]): cancelling such an exit
            // must return the element to its resting variant (interrupt behavior), not strip it. Null for
            // preset exits / enters, whose FromClasses are transient and correctly removed on cancel.
            public string[]? RestingClasses;
            public List<TimeValue>? DurationList;
            public List<TimeValue>? DelayList;
            // Drop-shadow paints this animation co-fades (registered as a driver at step 1), ended one-for-one
            // on completion / cancel. Each entry is the caster element and its paint binding. Null when the
            // animated subtree has no shadow.
            public List<(VisualElement element, DropShadowBinding binding)>? Shadows;
            // The element whose transition-interpolated opacity the co-fade tick samples each frame (the
            // animating subtree root). Stored so the tick reads it without recapturing.
            public VisualElement? AnimatingElement;
            // The recurring co-fade tick (panel-root scheduled). Paused on completion / cancel. Null when the
            // animated subtree has no shadow.
            public IVisualElementScheduledItem? ShadowTick;
            // For an exit started while the element was off-panel: the AttachToPanelEvent callback that defers
            // scheduling until attach. The callback unregisters itself when it fires, but a cancel-before-attach
            // never fires it, so it must be unregistered on cancel — otherwise it (and the closure pinning this
            // PendingAnimation) lingers on the element, surviving even pool reuse. Null for on-panel exits.
            public EventCallback<AttachToPanelEvent>? PendingAttach;
            // Non-null for a spring-driven entry (StyleAnimationScheduler.StartSpringVariant) — the per-channel
            // integrators/targets and the recurring tick. Null for a tween entry, which never touches this
            // (DurationList/DelayList/ScheduledItem/TimeoutItem are the tween's own equivalents).
            public MotionSpringState? Spring;
            // The map this entry currently lives in (_pendingEnters or _pendingExits) — kept in sync on a
            // spring's exit-cancel hand-off (which MOVES the entry from _pendingExits into _pendingEnters so the
            // reversal keeps ticking uninterrupted). Only the spring tick reads this (to remove itself from
            // whichever map currently owns it once it settles); a tween entry's own completion timeout already
            // closes over its own map directly and never needs this.
            public Dictionary<VisualElement, PendingAnimation>? OwningMap;
        }

        // State for a delayed variant swap's pending inline transition-delay clear (see
        // PlayDelayedVariantSwap). Deliberately separate from PendingAnimation, which carries enter/exit-only
        // fields (FromClasses/ToClasses/Shadows/PendingAttach/…) that a plain class-diff swap — having no
        // lifecycle of its own beyond "clear this one inline style later" — never needs.
        private sealed class PendingDelayedSwap
        {
            public IVisualElementScheduledItem? TimeoutItem;
            public List<TimeValue>? DelayList;
        }
    }
}
