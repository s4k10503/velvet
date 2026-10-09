#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // transition-duration and transition-timing-function are set as inline styles rather than in USS, with
    // C# as the Single Source of Truth for every play's timing.
    internal sealed class StyleAnimationScheduler
    {
        // UIToolkit's schedule.Execute runs on the next frame, so 50ms is set to absorb the
        // 60fps (16ms) - 30fps (33ms) frame delay.
        private const long AnimationGraceMs = 50;
        // Far above any legitimate UI transition length; catches a sec/ms unit mixup (an order-of-magnitude
        // mistake) rather than acting as a real cap.
        private const float MaxDurationSec = 10f;

        // EasingMode values are finite, so caching is safe.
        private static readonly Dictionary<EasingMode, List<EasingFunction>> s_easingCache = new();

#if UNITY_EDITOR
        // The cache lingers in EditorTest environments without Domain Reload, but it is safe because
        // the same EasingMode always produces the same EasingFunction instance.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields() => s_easingCache.Clear();
#endif

        // The half of a play that is the same whichever driver runs it: which element, which class sets the
        // swap moves between, what a cancel must put back, when the swap may start, and what to run once it
        // lands. A struct taken by `in` so the three drivers share one definition of a play without putting an
        // object on the heap — one of these is built per element per enter on a Motion mount.
        private readonly struct VariantPlay
        {
            internal VisualElement Element { get; init; }
            internal string[] FromClasses { get; init; }
            internal string[] ToClasses { get; init; }
            // The persistent resting set a cancel must restore — a variant exit's from-classes, a variant
            // enter's to-classes; null for preset plays, whose classes are all transient.
            internal string[]? RestingClasses { get; init; }
            internal Action? OnComplete { get; init; }
            internal Action? OnSwap { get; init; }
            internal float DelaySec { get; init; }
            internal float AdditionalDelaySec { get; init; }
            // Selects both the exit-shaped self-cancel and which bookkeeping map the play registers into — a
            // separate map field would only ever repeat that same choice.
            internal bool IsExit { get; init; }
        }

        private readonly Dictionary<VisualElement, PendingAnimation> _pendingExits = new();
        private readonly Dictionary<VisualElement, PendingAnimation> _pendingEnters = new();
        // Scoped to this scheduler instance so a leaked or double-returned rent stays contained to it instead
        // of corrupting a pool every other scheduler instance also draws from (see TimeValueListPool).
        private readonly TimeValueListPool _listPool = new();

        // The next-frame class swap (EnterFromClass -> EnterToClass) is what fires the CSS transition.
        // additionalDelaySec: extra delay (seconds) added on top of the StyleTransitionConfig delay, used by
        // AnimatePresenceNode.StaggerSec to sequentially delay child elements. 0 (default) means no extra delay.
        public void PlayEnter(VisualElement? element, StyleTransitionConfig? config, Action? onComplete = null, float additionalDelaySec = 0f)
        {
            if (element == null || config == null)
            {
                return;
            }

            // A spring or bezier config carries no enter classes: they are settable only inside this assembly, and
            // only the tween presets set them. So the play moves nothing and completes at once, and it leaves a
            // pending enter running, as a zero-duration tween's does: cancelling it would drop the pose swap an
            // inherited label change starts in the same render (FiberNodePatcher.PatchMotion).
            if (config.Type == TransitionType.Spring)
            {
                ValidateSpringParameters(config.Stiffness, config.Damping, config.Mass);
                onComplete?.Invoke();
                return;
            }

            if (config.Type == TransitionType.Bezier)
            {
                ValidateBezierParameters(config.BezierX1, config.BezierY1, config.BezierX2, config.BezierY2,
                    config.DurationSec);
                onComplete?.Invoke();
                return;
            }

            // A classic (non-variant) enter's classes are all transient overlays on top of the element's base
            // classes, so nothing rests and RestingClasses stays null.
            var play = new VariantPlay
            {
                Element = element,
                FromClasses = config.EnterFromClasses,
                ToClasses = config.EnterToClasses,
                RestingClasses = null,
                OnComplete = onComplete,
                DelaySec = config.DelaySec,
                AdditionalDelaySec = additionalDelaySec,
                IsExit = false,
            };

            // Classic transition enter: the to-classes are a TRANSIENT overlay, so they are removed on
            // completion (variantMode: false). Per-property overrides are wired only where a variant swap sets
            // transition-property: all (see PlayVariantEnter / PlayExit); a preset's own USS-declared
            // transition-property is untouched, so PropertyOverrides is not read here.
            PlayEnterInternal(in play, config.DurationSec, config.Easing,
                variantMode: false, propertyOverrides: null);
        }

        // Config-taking overload: every production call site holds its timing/spring knobs as a
        // StyleTransitionConfig, so this unpacks it once here instead of each call site repeating the unpack.
        // onSwap: run at the swap, once the classes have moved. Only a play for which RunsOnSwap holds runs it,
        // and one cancelled before its swap never does.
        // appliedClasses: the element's whole resting class set once this play lands — its own classes and the
        // pose's — where the caller knows it (see CancelSwapAhead); the to classes stand in for it elsewhere.
        public void PlayVariantEnter(VisualElement? element, string[]? fromClasses, string[]? toClasses,
            StyleTransitionConfig config, Action? onComplete = null, float additionalDelaySec = 0f,
            Action? onSwap = null, string[]? appliedClasses = null)
        {
            PlayVariantEnter(element, fromClasses, toClasses, config.DurationSec, config.Easing, config.DelaySec,
                onComplete, additionalDelaySec, config.PropertyOverrides,
                config.Type, config.Stiffness, config.Damping, config.Mass,
                config.BezierX1, config.BezierY1, config.BezierX2, config.BezierY2, onSwap, appliedClasses,
                MotionRepeat.Of(config));
        }

        internal static bool RunsOnSwap(StyleTransitionConfig config)
            => config.Type == TransitionType.Spring
                || (config.Type == TransitionType.Bezier && config.DurationSec != 0f)
                || TweensOnSwap(config);

        internal static bool TweensOnSwap(StyleTransitionConfig config)
            => config.Type == TransitionType.Tween && IsPlayableDuration(config.DurationSec);

        // A swap on such a config plays nothing: it lands the properties its pose names (LandNamedProperties) and
        // moves no classes. A bezier's zero duration is its None; a spring has no duration.
        internal static bool LandsAtOnce(StyleTransitionConfig config)
            => config.Type == TransitionType.Tween ? !IsPlayableDuration(config.DurationSec)
                : config.Type == TransitionType.Bezier && config.DurationSec == 0f;

        internal bool IsSwapPending(VisualElement element, Action onSwap)
            // MUTANT_SURVIVES(unreachable): a registered hold's swap is always the element's pending enter.
            // Each call that ends or replaces that enter releases, lands or replaces the hold before it returns:
            // the swap's onSwap, ResolveInlineHold, an exit, a presence enter that cancels it, and a teardown.
            => _pendingEnters.TryGetValue(element, out var enter) && ReferenceEquals(enter.OnSwap, onSwap);

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
        // bezierX1/bezierY1/bezierX2/bezierY2: the config's cubic-bezier control points (cubic-bezier(0.4, 0,
        // 0.2, 1) by default), meaningful only when type is Bezier — passed through the same way, as is repeat.
        public void PlayVariantEnter(VisualElement? element, string[]? fromClasses, string[]? toClasses,
            float durationSec, EasingMode easing, float delaySec, Action? onComplete = null, float additionalDelaySec = 0f,
            IReadOnlyList<StylePropertyTransition>? propertyOverrides = null,
            TransitionType type = TransitionType.Tween, float stiffness = 100f, float damping = 10f, float mass = 1f,
            float bezierX1 = 0.4f, float bezierY1 = 0f, float bezierX2 = 0.2f, float bezierY2 = 1f,
            Action? onSwap = null, string[]? appliedClasses = null, MotionRepeat repeat = default)
        {
            if (element == null)
            {
                return;
            }

            // The to-classes are the persistent resting state on every branch, so a cancel must restore them.
            var resolvedTo = toClasses ?? System.Array.Empty<string>();
            var play = new VariantPlay
            {
                Element = element,
                FromClasses = fromClasses ?? System.Array.Empty<string>(),
                ToClasses = resolvedTo,
                RestingClasses = resolvedTo,
                OnComplete = onComplete,
                OnSwap = onSwap,
                DelaySec = delaySec,
                AdditionalDelaySec = additionalDelaySec,
                IsExit = false,
            };

            WarnRepeatNotPlayed(type, repeat);
            if (type == TransitionType.Spring)
            {
                StartSpringVariant(in play, stiffness, damping, mass, repeat);
                return;
            }

            if (type == TransitionType.Bezier && durationSec != 0f)
            {
                StartBezierVariant(in play, bezierX1, bezierY1, bezierX2, bezierY2, durationSec, repeat);
                return;
            }

            if (!IsPlayableDuration(durationSec))
            {
                CancelSwapAhead(element, resolvedTo, appliedClasses ?? resolvedTo);
                RestPendingAt(element, resolvedTo, appliedClasses ?? resolvedTo);
            }
            PlayEnterInternal(in play, durationSec, easing, variantMode: true, propertyOverrides);
        }

        private void PlayEnterInternal(in VariantPlay play, float durationSec, EasingMode easing,
            bool variantMode, IReadOnlyList<StylePropertyTransition>? propertyOverrides)
        {
            var element = play.Element;
            var fromClasses = play.FromClasses;
            var toClasses = play.ToClasses;
            var onComplete = play.OnComplete;

            // DurationSec=0 / invalid: complete immediately. For variantMode this happens BEFORE any strip, so
            // the element keeps its already-applied resting (to) classes and mounts directly at animate.
            if (!ValidateDuration(durationSec, onComplete))
            {
                return;
            }

            // Cancel any existing enter animation.
            CancelEnter(element);

            var (staggerDelayMs, delayOffsetSec) = SplitNativeDelay(play.DelaySec, play.AdditionalDelaySec,
                variantMode ? propertyOverrides : null);
            var neverStarts = NeverStarts(play.AdditionalDelaySec);

            // Step 1: set duration / easing as inline styles, then show the from-state. In variantMode the
            // element already carries the resting to-classes, so strip them first so they don't fight the from-state.
            var (durationList, delayList) = ApplyTransitionStyles(element, new TweenSpec
            {
                DurationSec = durationSec, Easing = easing, DelaySec = play.DelaySec, DelayOffsetSec = delayOffsetSec,
                AllProperties = variantMode, PropertyOverrides = propertyOverrides,
            }, out var timing);
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
                // A variant enter's to-classes ARE the persistent resting state the strip above just
                // removed — a cancel (an exit starting mid-enter, a teardown) must put them back, or the
                // element is left without its resting variant for whatever follows (a classic exit with no
                // variant from-classes to re-add them would play out on a stripped element).
                RestingClasses = play.RestingClasses,
                DurationList = durationList,
                DelayList = delayList,
                Timing = timing,
                AnimatingElement = element,
                OnSwap = play.OnSwap,
                SwapAhead = variantMode,
                OnComplete = onComplete,
            };
            // Seed the ring band at the from-value (0 = invisible) NOW (synchronously, before the next-frame
            // swap) so there is no first-frame flash; the tick (started at the swap) then ramps it to follow
            // the caster's opacity. Released on completion / cancel.
            RingCoFadeCoordinator.BindRing(element, pending, 0f);
            _pendingEnters[element] = pending;

            // Schedules the deferred swap (and its own completion timeout) on the PANEL-ROOT host, not the
            // entering element itself — mirrors PlayExit's identical ScheduleOnHost: a keyed reorder can
            // transiently detach an already-attached element (RemoveFromHierarchy + re-Insert), and a one-shot
            // scheduled item's delay restarts in full on every re-attach — repeated reorders would keep
            // resetting the wait and could stall the enter forever (or, for a stagger claimed from an
            // ancestor's orchestration, drop the claimed delay's clear entirely). The element only needs to
            // stay attached for its OWN CSS transition to keep tweening — it does, a reorder moves it, never
            // removes it.
            void ScheduleOnHost()
            {
                // Superseded before it ever got to schedule (a cancel-before-attach removed this exact pending).
                if (!_pendingEnters.TryGetValue(element, out var cur) || !ReferenceEquals(cur, pending))
                {
                    return;
                }

                var panel = element.panel;
                if (panel == null)
                {
                    return;
                }
                var host = panel.visualTree;

                if (neverStarts)
                {
                    return;
                }
                var startAction = new Action(() =>
                    RunEnterStartAction(pending, host, variantMode, onComplete));

                if (staggerDelayMs > 0)
                {
                    // With extra delay: start after staggerDelayMs.
                    var scheduled = host.schedule.Execute(startAction);
                    scheduled.ExecuteLater(staggerDelayMs);
                    pending.ScheduledItem = scheduled;
                }
                else
                {
                    // No extra delay: still deferred by one nominal frame (StyleAnimateDriver.TickMs)
                    // rather than a zero-delay schedule.Execute. This call runs from inside the same
                    // timer tick that mounts the element (a standalone Motion's create, or an
                    // AnimatePresence-driven enter), and a zero-delay item becomes runnable within that
                    // very tick — before the panel has resolved the from-state's style even once. With
                    // nothing to compare against, the CSS transition sees no property change and the
                    // whole enter snaps straight to the end pose instead of tweening. A same-tick swap is
                    // possible with any deferral shorter than roughly a frame, so a full nominal frame is
                    // used instead of a token 1ms: it reliably survives the from-state's first style pass
                    // at any practical frame rate while still landing on (about) the very next update.
                    var scheduled = host.schedule.Execute(startAction);
                    scheduled.ExecuteLater(StyleAnimateDriver.TickMs);
                    pending.ScheduledItem = scheduled;
                }
            }

            // A stable host only exists once the element is attached. Off-panel at call time is the COMMON
            // case here — a standalone Motion's mount enter plays inside CreateElement, before the element is
            // inserted into the tree — so defer scheduling until attach, exactly like PlayExit's own off-panel
            // branch below.
            if (element.panel != null)
            {
                ScheduleOnHost();
            }
            else
            {
                DeferUntilAttached(element, pending, ScheduleOnHost);
            }
        }

        // The deferred-swap body PlayEnterInternal's ScheduleOnHost runs once attached (immediately, or after
        // the stagger/next-frame delay): swaps the from/to classes (firing the CSS transition), starts the
        // shadow co-fade tick, then schedules the completion timeout.
        private void RunEnterStartAction(PendingAnimation pending, VisualElement host, bool variantMode,
            Action? onComplete)
        {
            // Every value this needs beyond the host and the two completion knobs was recorded on the pending
            // when the play was registered, so it is read back rather than threaded through the closure.
            var element = pending.AnimatingElement!;
            var toClasses = pending.ToClasses!;
            if (!_pendingEnters.ContainsKey(element))
            {
                return;
            }

            pending.SwapAhead = false;
            StyleAnimationClassUtils.RemoveClasses(element, pending.FromClasses);
            StyleAnimationClassUtils.AddClasses(element, toClasses);
            RunOnSwap(pending);
            // The CSS opacity transition is now firing — start sampling the caster's opacity each frame so its
            // ring band fades in lockstep with it.
            RingCoFadeCoordinator.StartRingCoFadeTick(pending);

            // Step 3: after the duration, restore the element's transition timing. Classic enter also removes the
            // transient to-classes; variantMode KEEPS them (they are the persistent resting variant). Sized from the
            // SLOWEST animating property (SlowestPropertyTimeoutMs) rather than just the top-level
            // durationSec/delaySec: PropertyOverrides can give one property a longer duration than the
            // top-level value, and completing on the top-level timing alone would restore the element's own
            // transition-duration (snapping the still-mid-tween slower property to its resting value) before
            // it actually finishes.
            var durationMs = (long)SlowestPropertyTimeoutMs(pending.DurationList, pending.DelayList)
                + AnimationGraceMs;
            var timeout = host.schedule.Execute(() =>
            {
                if (_pendingEnters.Remove(element, out var completed))
                {
                    if (!variantMode)
                    {
                        StyleAnimationClassUtils.RemoveClasses(element, toClasses);
                    }
                    // Target is opaque now — stop the co-fade and release the band's inline opacity.
                    RingCoFadeCoordinator.EndRingCoFade(completed);
                    RestoreTransitionStyles(element, completed.Timing);
                    _listPool.ReturnDurationList(completed.DurationList);
                    _listPool.ReturnDelayList(completed.DelayList);
                    onComplete?.Invoke();
                }
            });
            timeout.ExecuteLater(durationMs);
            pending.TimeoutItem = timeout;
        }

        // The next-frame class swap (ExitFromClass -> ExitToClass) is what fires the CSS transition; onComplete
        // runs after the duration (the caller removes the element).
        // restoreFromOnCancel: when true, the exit's FromClasses are the persistent resting state (a variant
        // exit's variants[animate]); cancelling the exit (the key is re-added mid-exit) re-applies them so the
        // element returns to its resting variant instead of being left without it. Default false (preset exits,
        // whose FromClasses are transient).
        // additionalDelaySec: extra delay before the exit transition fires, on top of config.DelaySec. Used for
        // exit staggering (each removed child delayed by stagger × its index), mirroring the enter stagger.
        // onSwap: as PlayVariantEnter's.
        public void PlayExit(VisualElement? element, StyleTransitionConfig? config, Action? onComplete,
            // MUTANT_SURVIVES(unreachable): every PlayExit call outside the test assemblies passes the flag.
            // Only a test's call reads this default, and none of those reads what a cancel restores.
            bool restoreFromOnCancel = false, float additionalDelaySec = 0f, Action? onSwap = null)
        {
            if (element == null || config == null)
            {
                onComplete?.Invoke();
                return;
            }

            var play = new VariantPlay
            {
                Element = element,
                FromClasses = config.ExitFromClasses,
                ToClasses = config.ExitToClasses,
                RestingClasses = restoreFromOnCancel ? config.ExitFromClasses : null,
                OnComplete = onComplete,
                OnSwap = onSwap,
                DelaySec = config.DelaySec,
                AdditionalDelaySec = additionalDelaySec,
                IsExit = true,
            };

            WarnRepeatNotPlayed(config.Type, MotionRepeat.Of(config));
            if (config.Type == TransitionType.Spring)
            {
                // Deferred to attach when off-panel: a presence exit can start while its subtree is transiently
                // detached by a keyed reorder (see the tween exit's own ScheduleOnHost below) and this exit must
                // still eventually complete so the reconciler's ghost-removal re-render fires.
                StartSpringVariant(in play, config.Stiffness, config.Damping, config.Mass, MotionRepeat.Of(config));
                return;
            }

            if (config.Type == TransitionType.Bezier)
            {
                // One curve drives both directions (no ExitBezier* fields — mirrors the spring reusing its single
                // stiffness/damping/mass for the exit). Deferred-to-attach when off-panel, same as the spring exit.
                StartBezierVariant(in play, config.BezierX1, config.BezierY1, config.BezierX2, config.BezierY2,
                    config.DurationSec, MotionRepeat.Of(config));
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
            var (staggerDelayMs, delayOffsetSec) = SplitNativeDelay(config.DelaySec, additionalDelaySec,
                restoreFromOnCancel ? config.PropertyOverrides : null);
            var (durationList, delayList) = ApplyTransitionStyles(element, new TweenSpec
            {
                DurationSec = config.DurationSec, Easing = exitEasing, DelaySec = config.DelaySec,
                DelayOffsetSec = delayOffsetSec, AllProperties = restoreFromOnCancel,
                PropertyOverrides = restoreFromOnCancel ? config.PropertyOverrides : null,
            }, out var timing);
            StyleAnimationClassUtils.AddClasses(element, fromClasses);

            // Step 2: swap classes on the next frame.
            var pending = new PendingAnimation
            {
                FromClasses = fromClasses,
                ToClasses = toClasses,
                RestingClasses = restoreFromOnCancel ? fromClasses : null,
                DurationList = durationList,
                DelayList = delayList,
                Timing = timing,
                AnimatingElement = element,
                OnSwap = onSwap,
            };
            // Seed the ring band at the from-value (1 = opaque, the element's current state); the tick (started
            // at the swap) then ramps it down to follow the caster's fading opacity. Released on completion /
            // cancel.
            RingCoFadeCoordinator.BindRing(element, pending, 1f);
            _pendingExits[element] = pending;


            // Schedule the exit's frame callbacks on a STABLE host (the panel root), not on the exiting
            // element itself. An element-bound scheduled item pauses while its element is off the panel and
            // its delay RESTARTS in full on re-attach, and a reconcile reorder briefly detaches a
            // still-exiting ghost (RemoveFromHierarchy + re-Insert) to move it — repeated moves would keep
            // restarting the startAction/timeout, stall the exit, and leak the
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
                if (NeverStarts(additionalDelaySec))
                {
                    return;
                }
                var host = panel.visualTree;
                var startAction = new Action(() => RunExitStartAction(pending, host, onComplete));

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
            // one-shot callbacks on a host whose delay restarts in full the next time the element moves,
            // stalling the exit and leaking the ghost (the very failure the stable-host scheduling exists to
            // prevent).
            if (element.panel != null)
            {
                ScheduleOnHost();
            }
            else
            {
                DeferUntilAttached(element, pending, ScheduleOnHost);
            }
        }

        // The exit sibling of RunEnterStartAction: the deferred-swap body PlayExit's ScheduleOnHost runs once
        // attached — swaps the from/to classes (firing the CSS transition), starts the shadow co-fade tick, then
        // schedules the completion timeout. No variantMode parameter: unlike an enter, an exit never keeps its
        // to-classes on completion (the caller removes the element).
        private void RunExitStartAction(PendingAnimation pending, VisualElement host, Action? onComplete)
        {
            var element = pending.AnimatingElement!;
            if (!_pendingExits.ContainsKey(element))
            {
                return;
            }

            StyleAnimationClassUtils.RemoveClasses(element, pending.FromClasses);
            StyleAnimationClassUtils.AddClasses(element, pending.ToClasses!);
            RunOnSwap(pending);
            // The CSS opacity fade-out is now firing — sample the caster's opacity each frame on the
            // stable host so its ring band fades out in lockstep (and keeps ticking through any
            // reconcile-reorder detach of the exiting ghost, which is why the host is the panel root).
            RingCoFadeCoordinator.StartRingCoFadeTick(pending);

            // Step 3: invoke onComplete after the duration. Sized from the SLOWEST animating property
            // (SlowestPropertyTimeoutMs) rather than just the top-level DurationSec/DelaySec: a variant
            // exit's PropertyOverrides can give one property a longer duration than the top-level value,
            // and completing on the top-level timing alone would drop the ghost — removing its element —
            // while that slower property is still mid-tween.
            var durationMs = (long)SlowestPropertyTimeoutMs(pending.DurationList, pending.DelayList)
                + AnimationGraceMs;
            var timeout = host.schedule.Execute(() =>
            {
                if (_pendingExits.Remove(element, out var completed))
                {
                    RingCoFadeCoordinator.EndRingCoFade(completed);
                    // A completed exit's element can outlive its drop (a re-entry preempting the drop
                    // render), so the exit's timing must not stay on it for later class changes to tween by.
                    RestoreTransitionStyles(element, completed.Timing);
                    _listPool.ReturnDurationList(completed.DurationList);
                    _listPool.ReturnDelayList(completed.DelayList);
                    onComplete?.Invoke();
                }
            });
            timeout.ExecuteLater(durationMs);
            pending.TimeoutItem = timeout;
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
        // IMMEDIATELY at rest; MotionSpringClassParser then resolves whatever channels that swap touches
        // (opacity / translate / scale / rotate, plus the color- and length-valued properties) into a from/to
        // pair each, and a per-frame physics tick (StartSpringTick) drives them via inline styles until they
        // settle — replacing the tween's fixed-duration completion timeout with a dynamic settle check.
        // Both directions defer the tick start until attach when off-panel: a standalone Motion's enter plays
        // during element creation (FiberNodeFactory), and a presence enter plays before the entering element is
        // placed into the tree (GeneralPathReconciler) — so BOTH, like a presence exit, can start while still
        // detached (see PlayExit's own ScheduleOnHost/AttachToPanelEvent for the same rationale on the exit side).
        private void StartSpringVariant(in VariantPlay play, float stiffness, float damping, float mass,
            MotionRepeat repeat)
        {
            // Copied out because a local function cannot capture an `in` parameter.
            var element = play.Element;
            var fromClasses = play.FromClasses;
            var toClasses = play.ToClasses;
            var onComplete = play.OnComplete;
            var delaySec = play.DelaySec;
            var additionalDelaySec = play.AdditionalDelaySec;
            var isExit = play.IsExit;
            var map = isExit ? _pendingExits : _pendingEnters;

            // Read BEFORE the class swap below lands: the element's own current inline translate is whatever
            // its UNRELATED (non-swapped) classes rested it at — e.g. a base translate-y-8 alongside a
            // variant pair that only touches translate-x. Used as the resting value for a translate axis the
            // swap names on neither side (see Resolve's own doc).
            var restingTranslate = element.style.translate.value;
            var plan = MotionSpringClassParser.Resolve(fromClasses, toClasses,
                restingTranslate.x.value, restingTranslate.y.value,
                MotionSlotContext.Read(element, fromClasses, toClasses, slot => DrivenByRunningPlay(element, slot)));
            // An invalid configuration (see ValidateSpringParameters) degrades exactly like an empty plan
            // below: no state is built, so the shared "land the classes, complete immediately" branch handles
            // it without a separate code path.
            var state = ValidateSpringParameters(stiffness, damping, mass)
                ? MotionSpringDriver.Create(plan, stiffness, damping, mass, repeat)
                : null;
            // A spring this one interrupts hands on its velocity, read before the cancel below ends it.
            var interrupted = map.GetValueOrDefault(element)?.Spring;
            if (state != null && interrupted != null)
            {
                MotionSpringDriver.InheritVelocity(state, interrupted);
            }

            // Cancel any existing animation of this SAME flavor first (mirrors PlayEnterInternal's
            // CancelEnter(element) / PlayExit's CancelExit(element) self-cancel).
            CancelPending(map, element, animateReversal: isExit);

            // Land the classes at rest either way — nothing recognized to animate (or invalid spring
            // parameters) degrades to a plain, instantaneous class swap (the spring equivalent of a
            // zero-duration tween).
            StyleAnimationClassUtils.RemoveClasses(element, fromClasses);
            StyleAnimationClassUtils.AddClasses(element, toClasses);
            play.OnSwap?.Invoke();

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
                RestingClasses = play.RestingClasses,
                AnimatingElement = element,
                Spring = state,
            };
            // Seed the ring band with the spring exactly like the tween paths (PlayEnterInternal / PlayExit):
            // at the from-value NOW (synchronously, before the spring ever ticks) so there is no first-frame
            // flash, then the recurring tick — started alongside the spring's own tick in StartSpringTick —
            // samples the caster's opacity each frame so the band tracks it exactly as it does a tween. isExit
            // selects the same start value PlayExit uses (1 = opaque, the resting state before fading out); a
            // standalone enter always starts invisible (0), mirroring PlayEnterInternal — matching those
            // hardcoded values (rather than reading the spring's own opacity channel, which may not even exist
            // for a translate/scale/rotate-only play) keeps a spring's band behavior identical to a tween's for
            // the same enter/exit direction.
            RingCoFadeCoordinator.BindRing(element, pending, isExit ? 1f : 0f);
            state.OnSettled = onComplete;
            map[element] = pending;

            void ScheduleStart()
            {
                // Superseded before it ever got to start (a cancel-before-attach removed this exact pending).
                if (!map.TryGetValue(element, out var current) || !ReferenceEquals(current, pending))
                {
                    return;
                }
                var totalDelaySec = delaySec + additionalDelaySec;
                if (NeverStarts(totalDelaySec))
                {
                    return;
                }
                if (totalDelaySec <= 0f)
                {
                    StartSpringTick(element, pending, -totalDelaySec);
                    return;
                }
                var totalDelayMs = (long)(totalDelaySec * 1000);

                // A delayed start is parked on the panel-root host, not element.schedule: ScheduleStart only
                // ever runs once attached (called directly below, or from DeferUntilAttached's onAttach), but
                // a keyed reorder can transiently detach the element again during the delay window itself, and
                // a one-shot scheduled item's delay restarts in full on every re-attach (mirrors PlayExit's
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

        // Starts a bezier-driven variant enter/exit (StyleTransitionConfig.Type == Bezier). Structurally the
        // spring's sibling (StartSpringVariant), not the tween's: it bypasses CSS transitions to sample an EXACT
        // cubic-bezier curve, so the from→to class swap lands IMMEDIATELY at rest and a per-frame tick
        // (StartBezierTick) drives the resolved channels via inline styles. The one difference from the spring is
        // its completion is a fixed duration (BezierTweenDriver.Step reports done once elapsed reaches it) rather
        // than a dynamic settle. x1/y1/x2/y2 are the CSS cubic-bezier control points; durationSec is the fixed
        // length of one pass, which repeat plays again; VariantPlay owns the rest, and StartSpringVariant the
        // shared off-panel-defer contract.
        private void StartBezierVariant(in VariantPlay play, float x1, float y1, float x2, float y2,
            float durationSec, MotionRepeat repeat)
        {
            // Copied out because a local function cannot capture an `in` parameter.
            var element = play.Element;
            var fromClasses = play.FromClasses;
            var toClasses = play.ToClasses;
            var onComplete = play.OnComplete;
            var delaySec = play.DelaySec;
            var additionalDelaySec = play.AdditionalDelaySec;
            var map = play.IsExit ? _pendingExits : _pendingEnters;

            // Read BEFORE the class swap lands (same rationale as StartSpringVariant): a translate axis the swap
            // names on neither side rests at the element's own current inline translate rather than snapping to 0.
            var restingTranslate = element.style.translate.value;
            var plan = MotionSpringClassParser.Resolve(fromClasses, toClasses,
                restingTranslate.x.value, restingTranslate.y.value,
                MotionSlotContext.Read(element, fromClasses, toClasses, slot => DrivenByRunningPlay(element, slot)));
            // An invalid configuration (see ValidateBezierParameters) degrades exactly like an empty plan below:
            // no state is built, so the shared "land the classes, complete immediately" branch handles it. A zero
            // duration is one such case — it completes immediately with no warning, exactly like a Tween's None.
            var state = ValidateBezierParameters(x1, y1, x2, y2, durationSec)
                ? BezierTweenDriver.Create(plan, x1, y1, x2, y2, durationSec, repeat)
                : null;

            CancelPending(map, element, animateReversal: play.IsExit);

            StyleAnimationClassUtils.RemoveClasses(element, fromClasses);
            StyleAnimationClassUtils.AddClasses(element, toClasses);
            play.OnSwap?.Invoke();

            if (state == null)
            {
                onComplete?.Invoke();
                return;
            }

            BezierTweenDriver.ApplyCurrentValues(element, state);

            var pending = new PendingAnimation
            {
                FromClasses = fromClasses,
                ToClasses = toClasses,
                RestingClasses = play.RestingClasses,
                AnimatingElement = element,
                Bezier = state,
            };
            RingCoFadeCoordinator.BindRing(element, pending, play.IsExit ? 1f : 0f);
            state.OnSettled = onComplete;
            map[element] = pending;

            void ScheduleStart()
            {
                // Superseded before it ever got to start (a cancel-before-attach removed this exact pending).
                if (!map.TryGetValue(element, out var current) || !ReferenceEquals(current, pending))
                {
                    return;
                }
                var totalDelaySec = delaySec + additionalDelaySec;
                if (NeverStarts(totalDelaySec))
                {
                    return;
                }
                if (totalDelaySec <= 0f)
                {
                    StartBezierTick(element, pending, -totalDelaySec);
                    return;
                }
                var totalDelayMs = (long)(totalDelaySec * 1000);

                // See StartSpringVariant for why a delayed start parks on the panel-root host (survives a
                // transient reorder detach) rather than element.schedule.
                var host = element.panel?.visualTree;
                if (host == null)
                {
                    return;
                }
                var scheduled = host.schedule.Execute(() =>
                {
                    if (map.TryGetValue(element, out var stillCurrent) && ReferenceEquals(stillCurrent, pending))
                    {
                        StartBezierTick(element, pending);
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
        // tick's own rationale: a recurring item survives a keyed reorder's detach/re-attach of the animating
        // element on its own (UI Toolkit pauses and reschedules it automatically), but the panel root is
        // already where the co-fade sampling and the rest of this pending animation's bookkeeping run, so this
        // tick shares that same stable host rather than tracking a second one. Each tick reads the elapsed time
        // from the SAME clock the scheduler itself used to decide when to fire this callback
        // (TimerState.deltaTime, backed by Panel.TimeSinceStartupMs — the panel's
        // own time source, which a test's simulated panel overrides) rather than sampling a different clock
        // (e.g. Time.realtimeSinceStartupAsDouble) that could disagree with it, so the elapsed time always matches
        // what actually elapsed on the clock this tick is scheduled against. No-op if there is no host (should
        // not happen for the on-panel / already-deferred-to-attach cases this is called from, but this guards
        // rather than throws).
        private void StartSpringTick(VisualElement element, PendingAnimation pending, float preRollSec = 0f)
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

            // The spring's own physics tick is now live — start sampling the caster's opacity each frame
            // (StartRingCoFadeTick) so a co-faded ring band tracks it exactly like a tween's, from the same
            // moment its CSS transition would have started firing.
            RingCoFadeCoordinator.StartRingCoFadeTick(pending);

            for (var remaining = preRollSec; remaining > 0f; remaining -= PreRollStepSec)
            {
                if (MotionSpringDriver.Step(element, state, Math.Min(remaining, PreRollStepSec)))
                {
                    FinishSpring(element, pending, state);
                    return;
                }
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

                if (MotionSpringDriver.Step(element, state, dt))
                {
                    FinishSpring(element, pending, state);
                }
            }).Every(StyleAnimateDriver.TickMs);
        }

        private const float PreRollStepSec = StyleAnimateDriver.TickMs / 1000f;

        private void FinishSpring(VisualElement element, PendingAnimation pending, MotionSpringState state)
        {
            state.Tick?.Pause();
            state.Tick = null;
            if (MotionSpringDriver.EndsAtFrom(state))
            {
                // Held as FinishBezier holds a bezier play that ends on its from-values.
                RingCoFadeCoordinator.HoldRingCoFade(pending);
                state.OnSettled?.Invoke();
                return;
            }
            // Removes this entry from whichever of the two bookkeeping maps currently owns it — ordinarily
            // the map this play was started into, but an exit-cancel reversal hand-off (CancelPending) can
            // have MOVED it into _pendingEnters since then, so both are probed rather than assuming the
            // original one still holds it.
            if (!RemoveIfCurrent(_pendingExits, element, pending))
            {
                RemoveIfCurrent(_pendingEnters, element, pending);
            }
            // Target is at rest now — stop the co-fade and release the band's inline opacity (a no-op when
            // this element carries none, the common case).
            RingCoFadeCoordinator.EndRingCoFade(pending);
            MotionSpringDriver.ClearInlineOverrides(element, state);
            ReapplyMotionOwnedInlineValues(element);
            state.OnSettled?.Invoke();
        }

        // The bezier sibling of StartSpringTick: identical panel-root recurring-tick shape (same stable-host and
        // same-clock deltaTime rationale — see StartSpringTick), stepping the fixed-duration bezier tween instead
        // of the spring's physics and finalizing once BezierTweenDriver.Step reports elapsed has reached the
        // duration rather than a dynamic settle. No-op if there is no host (guards rather than throws).
        private void StartBezierTick(VisualElement element, PendingAnimation pending, float preRollSec = 0f)
        {
            var state = pending.Bezier;
            if (state == null)
            {
                return;
            }
            var host = element.panel?.visualTree;
            if (host == null)
            {
                return;
            }

            RingCoFadeCoordinator.StartRingCoFadeTick(pending);

            if (preRollSec > 0f && BezierTweenDriver.Step(element, state, preRollSec))
            {
                FinishBezier(element, pending, state);
                return;
            }

            state.Tick = host.schedule.Execute((TimerState ts) =>
            {
                var dt = ts.deltaTime / 1000f;
                if (dt <= 0f)
                {
                    return;
                }

                if (BezierTweenDriver.Step(element, state, dt))
                {
                    FinishBezier(element, pending, state);
                }
            }).Every(StyleAnimateDriver.TickMs);
        }

        private void FinishBezier(VisualElement element, PendingAnimation pending, BezierTweenState state)
        {
            state.Tick?.Pause();
            state.Tick = null;
            if (BezierTweenDriver.EndsAtFrom(state))
            {
                // The classes the play landed are its to-pose, so clearing the inline values would jump the element
                // there. The play stays registered holding its from-values, and its ring band the opacity they
                // leave, until a cancel releases them as it releases a running play's.
                RingCoFadeCoordinator.HoldRingCoFade(pending);
                state.OnSettled?.Invoke();
                return;
            }
            // Probe both maps, not just the one this play started into: an exit-cancel reversal hand-off
            // (CancelPending) can have MOVED it into _pendingEnters since then (same as the spring path).
            if (!RemoveIfCurrent(_pendingExits, element, pending))
            {
                RemoveIfCurrent(_pendingEnters, element, pending);
            }
            RingCoFadeCoordinator.EndRingCoFade(pending);
            BezierTweenDriver.ClearInlineOverrides(element, state);
            ReapplyMotionOwnedInlineValues(element);
            state.OnSettled?.Invoke();
        }

        private bool DrivenByRunningPlay(VisualElement element, ArbitraryProperty slot)
            => Drives(_pendingEnters, element, slot) || Drives(_pendingExits, element, slot);

        private static bool Drives(Dictionary<VisualElement, PendingAnimation> map, VisualElement element,
            ArbitraryProperty slot)
        {
            if (!map.TryGetValue(element, out var pending))
            {
                return false;
            }
            var longhands = StyleArbitraryLonghands.Of(slot);
            if (pending.Spring is { } spring)
            {
                return DrivesAxis(slot, spring.Opacity, spring.TranslateX, spring.TranslateY, spring.Scale, spring.Rotate)
                    || (spring.Colors?.Exists(c => StyleArbitraryLonghands.Of(c.Property).Overlaps(longhands)) ?? false)
                    || (spring.Lengths?.Exists(l => StyleArbitraryLonghands.Of(l.Property).Overlaps(longhands)) ?? false);
            }
            if (pending.Bezier is { } bezier)
            {
                return DrivesAxis(slot, bezier.Opacity, bezier.TranslateX, bezier.TranslateY, bezier.Scale, bezier.Rotate)
                    || (bezier.Colors?.Exists(c => StyleArbitraryLonghands.Of(c.Property).Overlaps(longhands)) ?? false)
                    || (bezier.Lengths?.Exists(l => StyleArbitraryLonghands.Of(l.Property).Overlaps(longhands)) ?? false);
            }
            return false;
        }

        // Same ownership rule as MotionSpringClassParser.PairTranslate.
        private static bool DrivesAxis(ArbitraryProperty slot, object? opacity, object? translateX, object? translateY,
            object? scale, object? rotate)
            => (slot == ArbitraryProperty.Opacity && opacity != null)
                || ((slot == ArbitraryProperty.TranslateX || slot == ArbitraryProperty.TranslateY)
                    // MUTANT_SURVIVES(equivalent, logic): scheduler translates are paired by Resolve and created and released together by both drivers.
                    && (translateX != null || translateY != null))
                || (slot == ArbitraryProperty.Scale && scale != null)
                || (slot == ArbitraryProperty.Rotate && rotate != null);

        // Removes element's entry from map, but only when it is STILL exactly pending (a later cancel/supersede
        // may have already replaced or removed it) — the identity check a settled tick and a cancelled play both
        // need before touching a map entry that might no longer be theirs. Returns whether it removed anything,
        // so a caller checking more than one candidate map (see StartSpringTick) can stop at the first hit.
        private static bool RemoveIfCurrent(Dictionary<VisualElement, PendingAnimation> map, VisualElement element, PendingAnimation pending)
        {
            if (map.TryGetValue(element, out var current) && ReferenceEquals(current, pending))
            {
                map.Remove(element);
                return true;
            }
            return false;
        }

        // A driver's ClearInlineOverrides (spring or bezier) nulls whichever style slots it wrote, letting the
        // cascade take back over — but a class the element still carries can OWN one of those same slots as a
        // resolver-applied inline value with no USS rule behind it at all (translate-x-4, translate-x-[100px],
        // opacity-[.5], w-[240px], bg-[#fff] — see MotionSpringClassParser's own scope
        // note: translate has no USS form whatsoever), so clearing the slot loses that value instead of letting
        // it fall back to a cascade rule that does not exist. DiffClassList only re-applies such a value when a
        // class REMOVAL triggers it; nothing removes a class here (the swap already landed its classes back
        // when the motion started), so nobody else re-asserts it. Re-read the element's OWN current class list
        // and re-apply whatever inline-resolved values it still names, mirroring
        // FiberAnimateMotionApplier.RestoreSharedInlineSlot's identical problem for the animate-* motions.
        // Driver-agnostic (takes only the element, reads its own class list), so both the spring and bezier
        // settle/cancel paths share it.
        private static void ReapplyMotionOwnedInlineValues(VisualElement element)
        {
            List<string>? classes = null;
            foreach (var cls in element.GetClasses())
            {
                (classes ??= new List<string>()).Add(cls);
            }
            if (classes != null)
            {
                FiberNodePatcher.ReapplyArbitraryValues(element, classes.ToArray());
            }
        }

        // Cancels the exit animation on the given element and removes the applied CSS classes; the
        // element reverses toward its resting classes with the transition kept alive (the inline
        // transition styles are cleared only after the reversal has run its course).
        // restingClasses: what a variant exit's cancel restores in place of the resting classes the exit started
        // from, for a caller that has re-applied the element's resting state since; keptClasses: the classes that
        // resting state carries, which the cancel leaves on the element, or puts back where the exit's swap
        // removed them, rather than removing with the exit's.
        public void CancelExit(VisualElement element, string[]? restingClasses = null, string[]? keptClasses = null)
            => CancelPending(_pendingExits, element, animateReversal: true, restingOverride: restingClasses,
                keptClasses: keptClasses);

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

        // Reached only from PlayVariantEnter's tween path when the duration is not playable. It cancels an earlier
        // tween variant enter whose swap has not run, which would otherwise put that enter's target classes back
        // beside this play's. The cancel puts back what RestingAt gives. The cancelled enter's phase ends with the
        // cancel, so its completion runs then, inside the call that starts this play.
        private void CancelSwapAhead(VisualElement element, string[] restingTo, string[] appliedClasses)
        {
            if (_pendingEnters.TryGetValue(element, out var pending) && pending.SwapAhead)
            {
                CancelPending(_pendingEnters, element, restingOverride: RestingAt(pending, restingTo, appliedClasses));
                pending.OnComplete?.Invoke();
            }
        }

        // The play a zero-duration swap leaves running rests at the new pose from here on: a later cancel of it puts
        // back that pose rather than the one the play was started toward, and keeps what the element's applied set
        // holds. A play with no resting classes — a classic enter, a preset exit's reversal — puts nothing back.
        private void RestPendingAt(VisualElement element, string[] restingTo, string[] appliedClasses)
        {
            if (!_pendingEnters.TryGetValue(element, out var pending) || pending.RestingClasses == null)
            {
                return;
            }
            pending.RestingClasses = RestingAt(pending, restingTo, appliedClasses);
            if (pending.KeptClasses != null)
            {
                pending.KeptClasses = appliedClasses;
            }
        }

        // restingTo, and those of the play's own classes, which its cancel removes, that appliedClasses still holds:
        // a Motion's own class is among them where the play's poses repeat it.
        private static string[] RestingAt(PendingAnimation pending, string[] restingTo, string[] appliedClasses)
        {
            var resting = new List<string>(restingTo);
            foreach (var removed in new[] { pending.FromClasses, pending.ToClasses })
            {
                foreach (var cls in removed ?? Array.Empty<string>())
                {
                    if (Array.IndexOf(appliedClasses, cls) >= 0)
                    {
                        resting.Add(cls);
                    }
                }
            }
            return resting.ToArray();
        }

        // Whether a spring or bezier enter is still running on the element, whose settle re-applies the inline
        // values its class list names (FiberNodePatcher.RemoveStaleInlineTokens). A bezier or spring enter
        // holding its from-values after its last pass counts, and its cancel is what re-applies them.
        internal bool IsDriving(VisualElement element)
            => _pendingEnters.TryGetValue(element, out var enter) && (enter.Spring != null || enter.Bezier != null);

        // A zero-duration pose lands the properties it names: the enter or reversal still running on the element
        // stops animating them and keeps animating the rest. The caller runs this before it writes the pose, so
        // an inline value it writes is timed by the transition this leaves (InlineStyleTransitionTimingTests).
        internal void LandNamedProperties(VisualElement element, string[] poseClasses, StyleTransitionConfig config)
        {
            if (!LandsAtOnce(config) || !_pendingEnters.TryGetValue(element, out var pending))
            {
                return;
            }
            var named = FiberNodePatcher.LonghandsOf(poseClasses);
            if (pending.Spring != null || pending.Bezier != null)
            {
                if (pending.Spring != null)
                {
                    MotionSpringDriver.ReleaseChannels(element, pending.Spring, named);
                }
                else
                {
                    BezierTweenDriver.ReleaseChannels(element, pending.Bezier!, named);
                }
                // A released slot takes the value the play's resting classes give it, which are the ones the sync
                // that follows diffs from: a value the pose repeats is not written by that sync.
                FiberNodePatcher.ReapplyArbitraryValues(element,
                    Writing(pending.RestingClasses ?? pending.ToClasses!, named));
                RemoveOwnedInlineTokens(element, pending, named);
            }
            else if (pending.RestingClasses != null)
            {
                LandOnHeldTransition(element, pending, poseClasses, LandedLonghands(poseClasses));
            }
        }

        // The play's settle re-applies the inline values the element's class list names, so the inline-resolved
        // resting classes writing what the pose now owns leave that list. An exit's cancel restored them before
        // handing this play its reversal, and no class diff takes them off. The play's own to-classes leave it
        // with the rest of the old pose's (FiberNodePatcher.RemoveStaleInlineTokens).
        private static void RemoveOwnedInlineTokens(VisualElement element, PendingAnimation pending,
            StyleLonghandSet named)
        {
            foreach (var cls in pending.RestingClasses ?? Array.Empty<string>())
            {
                RemoveIfOwned(element, cls, named);
            }
        }

        private static void RemoveIfOwned(VisualElement element, string cls, StyleLonghandSet named)
        {
            if (FiberNodePatcher.IsInlineResolved(cls) && Writes(cls, named))
            {
                element.RemoveFromClassList(cls);
            }
        }

        private static bool Writes(string cls, StyleLonghandSet named)
            => LandedLonghands(new[] { cls }).Overlaps(named);

        // LonghandsOf holds the filter family out of the cascade comparison (StyleArbitraryLonghands), while a
        // landing still has to time a filter the pose names.
        private static StyleLonghandSet LandedLonghands(string[] classes)
        {
            var longhands = FiberNodePatcher.LonghandsOf(classes);
            return Array.Exists(classes,
                cls => StyleFilterValueParser.IsFilterLeaf(StyleArbitraryValueResolver.StripImportant(cls, out _)))
                ? longhands.Union(StyleLonghandSet.Of(StyleLonghand.Filter))
                : longhands;
        }

        private static string[] Writing(string[] classes, StyleLonghandSet named)
            => Array.FindAll(classes, cls => Writes(cls, named));

        // Lands each named longhand of a variant tween within two frames while the entries before it keep timing
        // the rest of what they timed (MotionZeroDurationLandingTests). One whose value the pose changes gets a 1ms
        // entry appended; `filter`'s comes with one for `background-size` on the same timing, which the engine
        // animates an inline filter write by, so the engine lands a pose's blur (StyleFilterTransitionDriver
        // .EngineTimesFilterWrites). One whose value
        // the pose repeats from the play's target leaves the list, which rests it at that target
        // (HeldTransitionOverrideEngineTests), through the rewrite MotionNativeTransitionGuard uses for the slots
        // a driver owns. A classic enter's or a preset exit's transition-property is its USS one, which is why
        // only a play with resting classes reaches here. Rejected: a zero duration, under which an earlier `all`
        // still times the longhand.
        private static void LandOnHeldTransition(VisualElement element, PendingAnimation pending,
            string[] poseClasses, StyleLonghandSet named)
        {
            var repeated = RepeatedLonghands(pending.RestingClasses!, poseClasses, named);
            if (!repeated.IsEmpty)
            {
                MotionNativeTransitionGuard.ExcludeFromHeldList(element, repeated);
            }
            var names = new List<StylePropertyName>(element.style.transitionProperty.value ?? s_noNames);
            var count = names.Count;
            var durations = Wrapped(element.style.transitionDuration.value ?? s_zeroDurations, count);
            var easings = Wrapped(element.style.transitionTimingFunction.value ?? s_easeEasings, count);
            // Written only where the play wrote one (ApplyTransitionStyles), so a USS delay keeps applying.
            var delayList = element.style.transitionDelay.value;
            var delays = delayList != null ? Wrapped(delayList, count) : null;
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                if (named.Contains(longhand) && !repeated.Contains(longhand))
                {
                    AppendLanding(names, durations, easings, delays, longhand);
                    if (longhand == StyleLonghand.Filter)
                    {
                        AppendLanding(names, durations, easings, delays, StyleLonghand.BackgroundSize);
                    }
                }
            }
            element.style.transitionProperty = names;
            MotionTweenTiming.Write(element, durations, easings, delays);
        }

        private static void AppendLanding(List<StylePropertyName> names, List<TimeValue> durations,
            List<EasingFunction> easings, List<TimeValue>? delays, StyleLonghand longhand)
        {
            names.Add(new StylePropertyName(StyleUtilityProperties.UssName(longhand)));
            durations.Add(new TimeValue(1f, TimeUnit.Millisecond));
            easings.Add(new EasingFunction(EasingMode.Linear));
            delays?.Add(new TimeValue(0f, TimeUnit.Millisecond));
        }

        private static readonly List<StylePropertyName> s_noNames = new();
        private static readonly List<TimeValue> s_zeroDurations = new() { new TimeValue(0f) };
        private static readonly List<EasingFunction> s_easeEasings = new() { new EasingFunction(EasingMode.Ease) };

        // The named longhands the pose writes with the values the target classes give them.
        private static StyleLonghandSet RepeatedLonghands(string[] target, string[] poseClasses, StyleLonghandSet named)
        {
            var repeated = StyleLonghandSet.Empty;
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                var one = StyleLonghandSet.Of(longhand);
                if (named.Overlaps(one) && SameValues(Writing(target, one), Writing(poseClasses, one)))
                {
                    repeated = repeated.Union(one);
                }
            }
            return repeated;
        }

        private static bool SameValues(string[] a, string[] b)
        {
            var keysA = Array.ConvertAll(a, FiberNodePatcher.ValueKey);
            var keysB = Array.ConvertAll(b, FiberNodePatcher.ValueKey);
            return Array.TrueForAll(keysA, key => Array.IndexOf(keysB, key) >= 0)
                && Array.TrueForAll(keysB, key => Array.IndexOf(keysA, key) >= 0);
        }

        private static List<T> Wrapped<T>(List<T> list, int count)
        {
            var wrapped = new List<T>();
            for (var i = 0; i < count; i++)
            {
                wrapped.Add(list[i % list.Count]);
            }
            return wrapped;
        }

        // Whether the given element is currently exiting.
        public bool IsExiting(VisualElement element) => _pendingExits.ContainsKey(element);

        // Cancels every animation and removes the applied CSS classes and inline styles.
        public void CancelAll()
        {
            CancelAllInMap(_pendingExits);
            CancelAllInMap(_pendingEnters);
        }

        private static void RunOnSwap(PendingAnimation pending)
        {
            var onSwap = pending.OnSwap;
            pending.OnSwap = null;
            onSwap?.Invoke();
        }

        private static bool IsPlayableDuration(float durationSec)
            // MUTANT_SURVIVES(equivalent, boundary): durationSec != 0f already rules out both zeros.
            // They are the only values where durationSec < 0f and durationSec <= 0f disagree.
            => durationSec != 0f && !(durationSec < 0f || durationSec > MaxDurationSec);

        // Once per scheduler. A tween hands its interpolation to UI Toolkit's transitions, which play it once.
        private bool _warnedRepeatNotPlayed;

        internal const string RepeatNotPlayedWarning =
            "StyleTransitionConfig.Repeat is not played by TransitionType.Tween; this transition plays once. Use "
            + "Type = TransitionType.Bezier or TransitionType.Spring for a play that repeats.";

        private void WarnRepeatNotPlayed(TransitionType type, MotionRepeat repeat)
        {
            if (type != TransitionType.Tween || repeat.Count == 0f || _warnedRepeatNotPlayed)
            {
                return;
            }
            _warnedRepeatNotPlayed = true;
            FiberLogger.LogWarning("Motion", RepeatNotPlayedWarning);
        }

        internal static bool ValidateDuration(float durationSec, Action? onComplete)
        {
            if (durationSec == 0f)
            {
                onComplete?.Invoke();
                return false;
            }
            if (!IsPlayableDuration(durationSec))
            {
                UnityEngine.Debug.LogWarning(
                    $"[StyleAnimationScheduler] Invalid DurationSec: {durationSec}. Expected 0 < duration <= {MaxDurationSec}.");
                onComplete?.Invoke();
                return false;
            }
            return true;
        }

        // Mirrors ValidateDuration's guard, for the spring path: under a non-finite or non-positive stiffness,
        // damping or mass, MotionSpringDriver.PassDurationSec finds no sample at which a spring the play has to
        // move rests, so a channel on Framer's main-thread rule never ends. Left unvalidated, the panel-root tick
        // this drives would run indefinitely and its completion callback — the ONLY thing that removes a
        // presence exit's ghost — would never fire.
        internal static bool ValidateSpringParameters(float stiffness, float damping, float mass)
        {
            if (SpringIntegrator.AreValidParameters(stiffness, damping, mass))
            {
                return true;
            }

            FiberLogger.LogWarning("Spring",
                $"Invalid spring parameters (stiffness={stiffness}, damping={damping}, mass={mass}). " +
                "Expected finite, positive values for all three. Completing immediately instead of ticking forever.");
            return false;
        }

        // Validates a bezier play's control points and duration. Duration is validated like the tween path
        // (ValidateDuration), NOT ignored like a spring's: a zero duration is an intentional "no animation" (the
        // same silent immediate-complete as StyleTransitionConfig.None), while a negative / out-of-range duration
        // or a non-finite control point IS a misconfiguration and warns. A NaN/Infinity control point would
        // propagate into every inline style write (LerpUnclamped) and never reach a target, so the tick this
        // drives would run forever and its completion callback — the only thing that removes a presence exit's
        // ghost — would never fire. The control points' x range ([0,1], a monotone timing function) is validated
        // downstream in CubicBezierEvaluator instead: it degrades an out-of-range value to the default curve
        // rather than the forever-tick failure mode this method exists to catch, so it is not re-checked here.
        internal static bool ValidateBezierParameters(float x1, float y1, float x2, float y2, float durationSec)
        {
            if (durationSec == 0f)
            {
                return false;
            }

            if (float.IsFinite(x1) && float.IsFinite(y1) && float.IsFinite(x2) && float.IsFinite(y2)
                && durationSec > 0f && durationSec <= MaxDurationSec)
            {
                return true;
            }

            FiberLogger.LogWarning("Bezier",
                $"Invalid bezier parameters (x1={x1}, y1={y1}, x2={x2}, y2={y2}, duration={durationSec}). " +
                $"Expected finite control points and 0 < duration <= {MaxDurationSec}. " +
                "Completing immediately instead of ticking forever.");
            return false;
        }

        // C# becomes the Single Source of Truth, so they need not be defined in USS.
        // The EasingFunction lists are cached per EasingMode and the TimeValue lists rented from TimeValueListPool,
        // and the element receives MotionTweenTiming's copy of each, never one of these.
        private static readonly List<UnityEngine.UIElements.StylePropertyName> s_allTransitionProperties =
            new() { new UnityEngine.UIElements.StylePropertyName("all") };

        // A delay that never runs out — a BeforeChildren wait on a parent repeating without end — leaves the play
        // registered at its from-pose and never starts it, as Framer Motion never starts a child animation that waits
        // on one. A cancel still finds it.
        private static bool NeverStarts(float delaySec) => float.IsPositiveInfinity(delaySec);

        private static (long swapDelayMs, float transitionOffsetSec) SplitNativeDelay(float delaySec,
            float additionalDelaySec, IReadOnlyList<StylePropertyTransition>? overrides)
        {
            if (NeverStarts(additionalDelaySec))
            {
                return (0, 0f);
            }
            var earliestDelaySec = Math.Min(0f, delaySec);
            if (overrides != null)
            {
                foreach (var property in overrides)
                {
                    earliestDelaySec = Math.Min(earliestDelaySec, property.DelaySec ?? delaySec);
                }
            }
            var swapDelaySec = Math.Max(0f, additionalDelaySec + earliestDelaySec);
            return ((long)(swapDelaySec * 1000), additionalDelaySec - swapDelaySec);
        }

        private readonly struct TweenSpec
        {
            public float DurationSec { get; init; }
            public EasingMode Easing { get; init; }
            public float DelaySec { get; init; }
            public float DelayOffsetSec { get; init; }
            // A variant swap's: transition-property: all, and the per-property overrides that extend it.
            public bool AllProperties { get; init; }
            public IReadOnlyList<StylePropertyTransition>? PropertyOverrides { get; init; }
        }

        private (List<TimeValue> durationList, List<TimeValue>? delayList) ApplyTransitionStyles(
            VisualElement element, TweenSpec spec, out MotionTweenTiming.Play timing)
        {
            var (durationSec, easing, delaySec, delayOffsetSec) =
                (spec.DurationSec, spec.Easing, spec.DelaySec, spec.DelayOffsetSec);
            var (allProperties, propertyOverrides) = (spec.AllProperties, spec.PropertyOverrides);
            timing = MotionTweenTiming.Begin(element);
            // Per-property overrides extend the "all" catch-all with an explicit property list — reachable only
            // where a variant swap would otherwise set transition-property: all (allProperties), matching the
            // contract documented on StyleTransitionConfig.PropertyOverrides. Every other combination (no
            // overrides, or a preset transition that never sets allProperties) falls through unchanged below.
            if (allProperties && propertyOverrides is { Count: > 0 })
            {
                return ApplyPropertyOverrideTransitionStyles(element, durationSec, easing, delaySec, propertyOverrides, delayOffsetSec);
            }

            var durationMs = (int)(durationSec * 1000);
            var durationList = _listPool.RentDurationList(durationMs);
            MotionTweenTiming.Write(element, durationList, GetOrCreateEasingList(easing), null);

            // Variant animations swap user utility classes (e.g. opacity-0 ↔ opacity-100) that carry no
            // transition-* of their own, so UITK has no property to tween and the swap would snap. Set
            // transition-property: all so the changed computed values animate (a variant tween supplies just
            // a duration). Preset transitions keep their USS transition-property and don't pass this flag.
            if (allProperties)
            {
                element.style.transitionProperty = s_allTransitionProperties;
            }

            delaySec += delayOffsetSec;
            List<TimeValue>? delayList = null;
            if (delaySec != 0f || delayOffsetSec != 0f)
            {
                var delayMs = (int)(delaySec * 1000);
                delayList = _listPool.RentDelayList(delayMs);
                MotionTweenTiming.Write(element, null, null, delayList);
            }

            return (durationList, delayList);
        }

        // Per-overrides-list cache of the property-name list transition-property is set to: WHICH properties
        // are named never depends on the enter/exit direction (unlike the easing list below, an override's own
        // Property is never direction-dependent), and PropertyOverrides is fixed at config-authoring time and
        // typically shared across every element/render a given Motion plays — so building this once per
        // distinct overrides list (auto-evicted when that list itself is collected, mirroring
        // StyleArbitraryValueResolver's per-element layer cache) avoids repeating an identical List<T> on every
        // single enter/exit that reuses the same config.
        private static readonly ConditionalWeakTable<IReadOnlyList<StylePropertyTransition>, List<StylePropertyName>> s_propertyNameListCache = new();

        // Per-property override path: transition-property becomes "all" on the top-level timing followed by the
        // overridden properties (in declaration order), as Framer's per-value transition leaves every value it does
        // not name on the default one. The "all" entry has to come first: UI Toolkit takes the last entry naming a
        // property among those whose span is positive (MotionPerPropertyTransitionTests pins both halves).
        // Duration / delay are positionally-matched lists, rented EMPTY
        // and filled directly in the loop below (rather than staged through an intermediary int[] first) from
        // the SAME pools the single-entry path above uses, so they are returned through the existing
        // PendingAnimation.DurationList / DelayList bookkeeping unchanged. A null override field falls back to
        // the value the caller already resolved for this direction (durationSec / easing / delaySec — for an
        // exit, easing here is already ExitEasing ?? Easing, so the fallback stays direction-correct without
        // this method needing to know enter from exit).
        // The easing list IS direction-dependent (an override's null Easing falls back to defaultEasing, which
        // differs between an enter and an exit) and is rebuilt each call rather than cached: PropertyOverrides
        // is a handful of entries, so the allocation is bounded and one-shot per animation start (never
        // per-frame) — the per-mode EasingFunction INSTANCES it holds are still reused via the existing static
        // cache (GetOrCreateEasingList) rather than reallocated.
        private (List<TimeValue> durationList, List<TimeValue>? delayList) ApplyPropertyOverrideTransitionStyles(
            VisualElement element, float defaultDurationSec, EasingMode defaultEasing, float defaultDelaySec,
            IReadOnlyList<StylePropertyTransition> overrides, float delayOffsetSec)
        {
            var count = overrides.Count;
            var propertyNames = s_propertyNameListCache.GetValue(overrides, static ov =>
            {
                var names = new List<StylePropertyName> { s_allTransitionProperties[0] };
                for (var i = 0; i < ov.Count; i++)
                {
                    names.Add(new StylePropertyName(ov[i].Property));
                }
                return names;
            });
            var easingList = new List<EasingFunction>(count);
            var durationList = _listPool.RentEmptyDurationList(count);
            var delayList = _listPool.RentEmptyDelayList(count);
            var hasDelay = delayOffsetSec != 0f;
            // i = -1 is the leading "all" entry: an override with every field left null takes the top-level timing.
            for (var i = -1; i < count; i++)
            {
                var o = i < 0 ? default : overrides[i];
                easingList.Add(GetOrCreateEasingList(o.Easing ?? defaultEasing)[0]);
                var durationMs = (int)((o.DurationSec ?? defaultDurationSec) * 1000);
                var delaySec = (o.DelaySec ?? defaultDelaySec) + delayOffsetSec;
                if (delaySec != 0f)
                {
                    hasDelay = true;
                }
                var delayMs = (int)(delaySec * 1000);
                // UI Toolkit drops an entry whose span is not positive before it looks for the last one naming a
                // property, which would leave that property on the "all" entry's timing: a 1ms span lands it
                // instead, as LandOnHeldTransition does (MotionPerPropertyTransitionTests).
                if (Math.Max(0, durationMs) + delayMs <= 0)
                {
                    durationMs = 1;
                    delayMs = 0;
                }
                durationList.Add(new TimeValue(durationMs, TimeUnit.Millisecond));
                delayList.Add(new TimeValue(delayMs, TimeUnit.Millisecond));
            }

            element.style.transitionProperty = propertyNames;
            MotionTweenTiming.Write(element, durationList, easingList, null);

            // Mirrors the single-entry path: transition-delay is set only when at least one property actually
            // needs one (an all-zero delay list is behaviorally identical to leaving it unset) — the rented list
            // is returned immediately rather than handed to the caller for a later ReturnDelayList that would
            // never come (this play's own bookkeeping only tracks a DelayList when it set one).
            if (!hasDelay)
            {
                _listPool.ReturnDelayList(delayList);
                return (durationList, null);
            }
            MotionTweenTiming.Write(element, null, null, delayList);
            return (durationList, delayList);
        }

        // If the timeout callback already ran, map.Remove returns false and the cancellation is skipped.
        // This is safe because the classes have already been cleaned up in that case.
        // forTeardown: true only from CancelExitForTeardown — the element is being torn down for good (pool
        // return / disposal), not merely interrupted, so a reversal (tween or spring) is never appropriate
        // even when animateReversal is requested and the element still happens to be attached: see
        // CancelExitForTeardown for why handing off to one would corrupt the element after it is pooled.
        private void CancelPending(Dictionary<VisualElement, PendingAnimation> map, VisualElement element,
            bool animateReversal = false, bool forTeardown = false, string[]? restingOverride = null,
            string[]? keptClasses = null)
        {
            if (map.Remove(element, out var pending))
            {
                // Written back onto the pending, since a reversal hand-off below carries it on and a later cancel
                // of that reversal reads both again.
                if (restingOverride != null && pending.RestingClasses != null)
                {
                    pending.RestingClasses = restingOverride;
                }
                if (keptClasses != null)
                {
                    pending.KeptClasses = keptClasses;
                }
                keptClasses = pending.KeptClasses;
                // Pause() corresponds to cancelling a one-shot schedule produced by schedule.Execute().
                // Removing from the dictionary also makes the ContainsKey check inside the callback fail,
                // providing defense in depth.
                pending.ScheduledItem?.Pause();
                pending.TimeoutItem?.Pause();
                // Remove the off-panel deferred-attach callback if it never fired (cancel-before-attach), else it
                // and its captured PendingAnimation linger on the element across pool reuse.
                if (pending.PendingAttach != null) element.UnregisterCallback(pending.PendingAttach);
                StyleAnimationClassUtils.RemoveClasses(element, pending.FromClasses, keptClasses);
                StyleAnimationClassUtils.RemoveClasses(element, pending.ToClasses, keptClasses);
                // A variant exit's FromClasses ARE the resting state (variants[animate]); cancelling the exit
                // (key re-added mid-exit) must return the element to that resting variant rather than strip it.
                // Re-add after the removals so the element is left in its resting state and stays consistent with
                // the MotionAppliedClasses cache (which still records the resting class as applied).
                if (pending.RestingClasses != null)
                {
                    StyleAnimationClassUtils.AddClasses(element, pending.RestingClasses);
                }
                // Interrupted enter / exit: the target returns to its resting (opaque) state, so stop this
                // tween's co-fade and release the band's inline opacity back to the cascade.
                RingCoFadeCoordinator.EndRingCoFade(pending);

                if (pending.Spring != null)
                {
                    CancelSpringPending(element, pending, pending.Spring, animateReversal, forTeardown);
                    return;
                }

                if (pending.Bezier != null)
                {
                    CancelBezierPending(element, pending, pending.Bezier, animateReversal, forTeardown);
                    return;
                }

                CancelTweenPending(element, pending, animateReversal, forTeardown);
            }
        }

        // Spring branch of CancelPending's post-removal dispatch: hand off to a reversal spring, or stop the
        // tick and drop its inline overrides outright. See CancelPending for the shared prefix (dictionary
        // removal, class/resting-class bookkeeping, co-fade end) this runs after.
        private void CancelSpringPending(VisualElement element, PendingAnimation pending, MotionSpringState spring,
            bool animateReversal, bool forTeardown)
        {
            if (!forTeardown && animateReversal && element.panel != null && spring.Tick != null)
            {
                // Hand off to a reversal spring: retarget every channel toward the value it STARTED
                // from, released from the value and velocity it was last sampled at (see
                // MotionSpringDriver.Retarget), drop the original completion (a reversal settling is
                // not "finishing" anything the original caller asked for), and move ownership into the
                // enter map — mirroring the tween reversal's own move into _pendingEnters below. The recurring tick keeps running uninterrupted throughout;
                // only the springs it samples and its eventual finalize action change. Requires a tick that has
                // actually started (spring.Tick != null): a cancel that lands before then — still
                // parked behind its delay — has no running tick to keep alive, and nothing would ever
                // start one for it (its ScheduledItem was already paused above, and a still-off-panel
                // PendingAttach was already unregistered), so handing off here would just park a dead
                // entry in _pendingEnters forever instead of finalizing below.
                MotionSpringDriver.Retarget(spring);
                spring.OnSettled = null;
                CancelPending(_pendingEnters, element);
                _pendingEnters[element] = pending;
            }
            else
            {
                // No reversal (a plain CancelEnter, an off-panel exit with nothing left to interpolate
                // against, a cancel before the tick ever started, or a teardown cancel that must never
                // hand off regardless): stop the tick now and drop the inline overrides immediately.
                spring.Tick?.Pause();
                spring.Tick = null;
                MotionSpringDriver.ClearInlineOverrides(element, spring);
                ReapplyMotionOwnedInlineValues(element);
            }
        }

        // Bezier branch of CancelPending's post-removal dispatch: the bezier sibling of CancelSpringPending —
        // see CancelPending for the shared prefix this runs after.
        private void CancelBezierPending(VisualElement element, PendingAnimation pending, BezierTweenState bezier,
            bool animateReversal, bool forTeardown)
        {
            if (!forTeardown && animateReversal && element.panel != null && bezier.Tick != null)
            {
                // Hand off to a reversal exactly like the spring branch above: freeze each channel at its
                // current sampled value, retarget it back toward its resting value, drop the original
                // completion, and move ownership into the enter map. Requires a tick that has actually
                // started (bezier.Tick != null) — a cancel that lands while still parked behind the delay
                // has no running tick to redirect and nothing would ever start one, so it finalizes below.
                BezierTweenDriver.Retarget(bezier);
                bezier.OnSettled = null;
                CancelPending(_pendingEnters, element);
                _pendingEnters[element] = pending;
            }
            else
            {
                bezier.Tick?.Pause();
                bezier.Tick = null;
                BezierTweenDriver.ClearInlineOverrides(element, bezier);
                ReapplyMotionOwnedInlineValues(element);
            }
        }

        // Tween branch of CancelPending's post-removal dispatch (neither Spring nor Bezier set) — see
        // CancelPending for the shared prefix this runs after.
        private void CancelTweenPending(VisualElement element, PendingAnimation pending,
            bool animateReversal, bool forTeardown)
        {
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
                RestoreTransitionStyles(element, pending.Timing);
                _listPool.ReturnDurationList(pending.DurationList);
                _listPool.ReturnDelayList(pending.DelayList);
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
                Timing = pending.Timing,
            };
            var timeoutMs = SlowestPropertyTimeoutMs(pending.DurationList, pending.DelayList);
            var timeout = element.schedule.Execute(() =>
            {
                if (_pendingEnters.Remove(element))
                {
                    RestoreTransitionStyles(element, reversal.Timing);
                    _listPool.ReturnDurationList(reversal.DurationList);
                    _listPool.ReturnDelayList(reversal.DelayList);
                }
            });
            timeout.ExecuteLater((long)timeoutMs);
            reversal.TimeoutItem = timeout;
            _pendingEnters[element] = reversal;
        }

        // How long a tween must stay alive before it is safe to restore the element's transition timing / fire
        // completion: the SLOWEST animating property's delay + duration. For the single-entry case (no
        // PropertyOverrides) that is just duration[0] + delay[0], same as a plain top-level DurationSec/DelaySec;
        // PropertyOverrides can give each property its own duration / delay, so an interrupted reversal, and an
        // enter/exit's own completion, must both wait for whichever property finishes last, not just the first
        // (or the first-declared) one — otherwise a slower property's transition-duration gets replaced (snapping
        // it to the resting value, or dropping the ghost) while it is still mid-tween. Shared by
        // ScheduleReversalCleanup (a cancelled exit's reversal) and PlayEnterInternal / PlayExit's own
        // completion timeout, all three of which already hold the exact duration/delay lists ApplyTransitionStyles
        // built for this play, so the slowest-property computation only has to live here once.
        private static float SlowestPropertyTimeoutMs(List<TimeValue>? durationList, List<TimeValue>? delayList)
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
                RingCoFadeCoordinator.EndRingCoFade(pending);
                if (pending.Spring != null)
                {
                    // A hard stop, no reversal: pause the tick and drop the inline overrides it owns.
                    pending.Spring.Tick?.Pause();
                    MotionSpringDriver.ClearInlineOverrides(element, pending.Spring);
                    ReapplyMotionOwnedInlineValues(element);
                }
                if (pending.Bezier != null)
                {
                    // A hard stop, no reversal, as the spring branch above.
                    pending.Bezier.Tick?.Pause();
                    BezierTweenDriver.ClearInlineOverrides(element, pending.Bezier);
                    ReapplyMotionOwnedInlineValues(element);
                }
                RestoreTransitionStyles(element, pending.Timing);
                _listPool.ReturnDurationList(pending.DurationList);
                _listPool.ReturnDelayList(pending.DelayList);
            }
            map.Clear();
        }

        private void RestoreTransitionStyles(VisualElement element, MotionTweenTiming.Play? timing)
        {
            MotionTweenTiming.End(element, timing);
            // Release the variant transition-property: all (set by ApplyTransitionStyles for variant swaps).
            // A no-op for preset transitions, which never set it inline (USS provides transition-property).
            // Routed through the guard rather than nulled here: a per-frame animate-* driver can be holding the
            // same slot suspended for longer than this play, and it owns what the slot goes back to.
            MotionNativeTransitionGuard.RestoreAfterForeignWrite(element);
        }

        private static List<EasingFunction> GetOrCreateEasingList(EasingMode easing)
        {
            if (!s_easingCache.TryGetValue(easing, out var list))
            {
                list = new List<EasingFunction>(1) { new(easing) };
                s_easingCache[easing] = list;
            }
            return list;
        }

        // Fields are sufficient since this is a private sealed class, not a public API surface.
        private sealed class PendingAnimation
        {
            public IVisualElementScheduledItem? ScheduledItem;
            public IVisualElementScheduledItem? TimeoutItem;
            public string[]? FromClasses;
            public string[]? ToClasses;
            // Classes to RE-ADD when this animation is cancelled (interrupted). Set for variant-driven
            // plays, whose class set includes the persistent resting state (variants[animate]) — an exit's
            // FromClasses, an enter's ToClasses: cancelling such a play must return the element to its
            // resting variant (interrupt behavior), not strip it. Null for preset exits / enters, whose
            // classes are all transient and correctly removed on cancel.
            public string[]? RestingClasses;
            public List<TimeValue>? DurationList;
            public List<TimeValue>? DelayList;
            // The tween timing this play wrote, which a reversal it hands on to carries.
            public MotionTweenTiming.Play? Timing;
            // The element whose transition-interpolated opacity the co-fade tick samples each frame (the
            // animating subtree root). Stored so the tick reads it without recapturing.
            public VisualElement? AnimatingElement;
            // The recurring ring co-fade tick (panel-root scheduled). Paused on completion / cancel. Null when
            // the animating element carries no band.
            public IVisualElementScheduledItem? RingTick;
            // For an exit started while the element was off-panel: the AttachToPanelEvent callback that defers
            // scheduling until attach. The callback unregisters itself when it fires, but a cancel-before-attach
            // never fires it, so it must be unregistered on cancel — otherwise it (and the closure pinning this
            // PendingAnimation) lingers on the element, surviving even pool reuse. Null for on-panel exits.
            public EventCallback<AttachToPanelEvent>? PendingAttach;
            // Non-null for a spring-driven entry (StyleAnimationScheduler.StartSpringVariant) — the per-channel
            // integrators/targets and the recurring tick. Null for a tween entry, which never touches this
            // (DurationList/DelayList/ScheduledItem/TimeoutItem are the tween's own equivalents). Which of the
            // two bookkeeping maps (_pendingEnters / _pendingExits) currently holds this entry is NOT tracked
            // as a field here — a spring's exit-cancel hand-off can MOVE it from one to the other, and the
            // settled tick just probes both (see RemoveIfCurrent) rather than keeping that choice in sync.
            public MotionSpringState? Spring;
            // Non-null for a bezier-driven entry (StartBezierVariant) — the bezier sibling of Spring. Mutually
            // exclusive with it per play (which one is set depends on config.Type); both null for a plain tween.
            public BezierTweenState? Bezier;
            // A variant play's onSwap until the swap runs it; null once it has run, and for a play given none.
            public Action? OnSwap;
            // True from a tween variant enter's registration until its swap runs; false for every other entry.
            public bool SwapAhead;
            // A tween enter's completion, which CancelSwapAhead runs when it cancels the entry before its swap.
            public Action? OnComplete;
            // The classes a CancelExit caller kept (see CancelExit), for a reversal that is cancelled in turn.
            public string[]? KeptClasses;

            // The animating element's OWN ring band, when it has one. Only its own: a band belonging to a
            // DESCENDANT is a child of a descendant, so UI Toolkit's opacity compositing already fades it.
            public VisualElement? RingOverlay;
        }

        // Ring-band co-fade bookkeeping for every StyleAnimationScheduler play (tween / spring / bezier, enter /
        // exit). A ring band is a separate element parented BESIDE its caster (see RingOverlay), so it is
        // outside the subtree the caster's opacity composites and only an explicit per-frame push fades it
        // along. A paint that the caster's own opacity does reach needs no push, because the renderer already
        // scales it (pinned by PaintOpacityParityPlaybackTests) — which is why nothing else is driven
        // here today, and equally why a paint MOVED out of its caster would have to join. Depends only on the
        // PendingAnimation instance each play already carries, not on any of the scheduler's own map/pool state.
        private static class RingCoFadeCoordinator
        {
            // Binds the element's own band (if it has one) to this play and seeds it at the play's start factor
            // (0 for an enter, 1 for an exit), synchronously, so there is no first-frame flash before the tick
            // takes over.
            internal static void BindRing(VisualElement element,
                StyleAnimationScheduler.PendingAnimation pending, float startFactor)
            {
                if (RingOverlay.TryGet(element) is not { } binding)
                {
                    return;
                }
                pending.RingOverlay = binding.Overlay;
                binding.Overlay.style.opacity = startFactor;
            }

            // Starts the recurring co-fade tick: every frame, sample the animating element's current (transition-
            // interpolated) opacity and push it to the band. Scheduled on the PANEL ROOT (not the animating
            // element): a recurring item survives a keyed reorder's detach/re-attach of the animating element on
            // its own (UI Toolkit pauses and reschedules it automatically), but the panel root is already the one
            // stable host every other piece of this animation's bookkeeping runs against, so the tick shares it
            // instead of tracking its own. No-op when the element carries no band (the common case), so a
            // ringless animation costs nothing. Paused by EndRingCoFade on completion / cancel.
            internal static void StartRingCoFadeTick(StyleAnimationScheduler.PendingAnimation pending)
            {
                if (pending.RingOverlay == null)
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
                pending.RingTick = host.schedule.Execute(() =>
                {
                    var overlay = pending.RingOverlay;
                    if (overlay == null)
                    {
                        return;
                    }
                    var raw = animatingElement.resolvedStyle.opacity;
                    overlay.style.opacity = float.IsNaN(raw) ? 1f : UnityEngine.Mathf.Clamp01(raw);
                }).Every(StyleAnimateDriver.TickMs);
            }

            // Stops the co-fade tick of a play holding its from-values past its end, leaving the band at the opacity
            // its element holds; the cancel that ends the hold releases it through EndRingCoFade.
            internal static void HoldRingCoFade(StyleAnimationScheduler.PendingAnimation pending)
            {
                pending.RingTick?.Pause();
                if (pending.RingOverlay != null && pending.AnimatingElement != null)
                {
                    pending.RingOverlay.style.opacity = UnityEngine.Mathf.Clamp01(MotionOpacity.Own(pending.AnimatingElement));
                }
            }

            // Stops the co-fade tick and releases the band's inline opacity (null-safe; balanced one-for-one
            // with BindRing).
            internal static void EndRingCoFade(StyleAnimationScheduler.PendingAnimation pending)
            {
                pending.RingTick?.Pause();
                if (pending.RingOverlay == null)
                {
                    return;
                }
                // Null, not 1: the inline opacity is this play's alone, so releasing it returns the band
                // to whatever the cascade says rather than pinning it opaque.
                pending.RingOverlay.style.opacity = StyleKeyword.Null;
                pending.RingOverlay = null;
            }
        }

        // TimeValue buffer pool for the transition-duration / transition-delay inline style lists every tween play
        // rents: reuses an existing List<TimeValue> instead of allocating one per animation start. Duration and
        // delay lists pool separately (distinct backing Stacks) even though both are structurally List<TimeValue>,
        // since a duration list and a delay list are never interchangeable at a call site. One instance per
        // StyleAnimationScheduler (not shared process-wide): every rent is returned by that same scheduler's own
        // completion/cancel path, so a leaked or double-returned entry from a bug in one scheduler cannot corrupt
        // a pool other, unrelated scheduler instances also draw from.
        private sealed class TimeValueListPool
        {
            // A loose upper bound on concurrently animating elements, not a hard capacity limit.
            private const int MaxPoolSize = 16;

            private readonly Stack<List<TimeValue>> _durationPool = new();
            private readonly Stack<List<TimeValue>> _delayPool = new();

            internal List<TimeValue> RentDurationList(int ms) => RentTimeValueList(_durationPool, ms);
            internal void ReturnDurationList(List<TimeValue>? list) => ReturnTimeValueList(_durationPool, list);
            internal List<TimeValue> RentDelayList(int ms) => RentTimeValueList(_delayPool, ms);
            internal void ReturnDelayList(List<TimeValue>? list) => ReturnTimeValueList(_delayPool, list);
            // n-entry siblings for PropertyOverrides: rented EMPTY (the caller fills them directly, positionally
            // matching each override — see StyleAnimationScheduler.ApplyPropertyOverrideTransitionStyles) rather
            // than pre-filled from an intermediary int[], but drawn from the SAME pools the single-entry methods
            // above use (a rented list's capacity just grows to whatever size it was last asked for, so
            // ReturnTimeValueList already handles returning either shape without change).
            internal List<TimeValue> RentEmptyDurationList(int capacity) => RentEmptyTimeValueList(_durationPool, capacity);
            internal List<TimeValue> RentEmptyDelayList(int capacity) => RentEmptyTimeValueList(_delayPool, capacity);

            // Single-entry hot path: every enter / exit without PropertyOverrides rents exactly one of these per
            // animation.
            private static List<TimeValue> RentTimeValueList(Stack<List<TimeValue>> pool, int ms)
            {
                var list = RentEmptyTimeValueList(pool, capacity: 1);
                list.Add(new TimeValue(ms, TimeUnit.Millisecond));
                return list;
            }

            // Rents a list with no entries — either a fresh one sized to capacity, or a pooled one cleared of
            // whatever it held last. Shared by the single-entry rent above (which adds its one TimeValue itself)
            // and the PropertyOverrides path (which fills several, positionally, in its own loop).
            private static List<TimeValue> RentEmptyTimeValueList(Stack<List<TimeValue>> pool, int capacity)
            {
                if (!pool.TryPop(out var list))
                {
                    list = new List<TimeValue>(capacity);
                }
                else
                {
                    list.Clear();
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
        }

    }
}
