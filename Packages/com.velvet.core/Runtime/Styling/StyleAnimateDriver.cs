using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-element state for a running animate-* motion. Holds the recurring scheduled tick (paused on
    // teardown), the loop start time, and the pan-axis decision (pan modes only).
    internal sealed class StyleAnimateBinding
    {
        public AnimateSpec Spec;
        // Start of the loop on Clock. The phase is derived from elapsed time, so a dropped frame never
        // accumulates drift (unlike a per-tick increment).
        public double StartTime;
        public MotionClock Clock = MotionClock.Realtime;
        // The recurring tick. Scheduled on the PANEL ROOT (not the element) so a keyed reorder — which
        // briefly detaches the element and would make UI Toolkit silently drop a per-element scheduled item —
        // does not stall the animation. Paused on teardown.
        public IVisualElementScheduledItem? Scheduled;
        // Pan axis for Gradient/Shimmer, derived from the gradient's angle at attach (vertical when the
        // gradient flows more up/down than left/right). Unused by the non-pan modes.
        public bool PanVertical;
        // For an attach that happened off-panel: the deferred-scheduling callback, unregistered on teardown
        // if it never fired (so it does not linger on the element across pool reuse).
        public EventCallback<AttachToPanelEvent>? PendingAttach;
    }

    // Drives the animate-* motions. The texture is baked ONCE (the static gradient path); this only writes a
    // cheap inline style per frame — a background-position offset (Gradient/Shimmer), a hue-rotate filter
    // angle (Hue), an opacity (Pulse), or a rotation (Spin) — so a continuously-animating gradient costs no
    // per-frame texture work. The phase math is pure (PanOffsetPx / HueAngleDeg / PulseOpacity / SpinAngleDeg /
    // Phase) and unit-tested directly; the scheduler wiring is exercised at runtime (the EditMode PlayerLoop
    // does not tick, so tests drive ApplyFrame at explicit phases instead).
    internal static class StyleAnimateDriver
    {
        // Tick interval (~60fps). The phase is time-derived, so the exact cadence only affects smoothness.
        // Internal: shared with StyleAnimationScheduler's spring tick and ring co-fade tick, which want the
        // SAME ~60fps cadence rather than a second (or third) hand-copied literal.
        internal const long TickMs = 16;

        // Oversize factor for the Gradient pan: the background is twice the box along the pan axis, so the
        // box window slides across the full gradient (offset range [-box, 0]) without revealing an edge.
        private const float GradientOversize = 200f;
        // Pulse opacity bounds: oscillates between full and half (matches the conventional attention pulse).
        private const float PulseMinOpacity = 0.5f;
        private const float PulseMaxOpacity = 1f;

        // The loop each element runs, for ReassertLoop.
        private static readonly ConditionalWeakTable<VisualElement, StyleAnimateBinding> s_running = new();

        // The slots each driver on the element writes over its loop, by driver.
        private static readonly ConditionalWeakTable<VisualElement, Dictionary<object, MotionTransitionSlots>> s_held = new();

        // The slots a Motion driver keeps from a loop while it drives them. Framer Motion hands opacity to
        // the browser's own animation engine, whose animations outrank a CSS animation; the transform
        // shorthands it writes as inline style on the main thread, which a CSS animation outranks.
        private const MotionTransitionSlots MotionHeldSlots = MotionTransitionSlots.Opacity;

        /// <summary>
        /// Records which of <paramref name="drivenSlots"/> <paramref name="owner"/> keeps from the element's loop
        /// until it calls this again: a Motion driver passes what it drives when it starts or drops channels,
        /// and <see cref="MotionTransitionSlots.None"/> when it lets go.
        /// </summary>
        public static void HoldAgainstLoop(VisualElement element, object owner, MotionTransitionSlots drivenSlots)
        {
            var held = s_held.GetValue(element, static _ => new Dictionary<object, MotionTransitionSlots>());
            held.Remove(owner);
            var kept = drivenSlots & MotionHeldSlots;
            if (kept != MotionTransitionSlots.None)
            {
                held[owner] = kept;
            }
        }

        /// <summary>
        /// Forgets every hold on an element being torn down or returned to a pool — the backstop for a teardown
        /// that pre-empts a driver's own release, on the terms of <see cref="MotionNativeTransitionGuard.ReleaseAll"/>.
        /// </summary>
        public static void ForgetHolds(VisualElement element) => s_held.Remove(element);

        /// <summary>
        /// Writes the element's running loop over whatever a per-frame driver just wrote or released, unless a
        /// driver holds the loop's slot (see <see cref="HoldAgainstLoop"/>) — so a loop keeps its slot the way a
        /// CSS animation outranks an inline style. The spring and bezier drivers and the filter tween call this
        /// after each of their writes; the loop's own tick runs on a separate scheduled item, so without it the
        /// slot would show whichever of the two ran last in a frame.
        /// </summary>
        // True while an animate-hue loop drives the element's filter, which a filter utility's change does not reach
        // (StyleFilterEngineWrite).
        internal static bool DrivesFilter(VisualElement element)
            => s_running.TryGetValue(element, out var binding) && binding.Spec.Mode == AnimateMode.Hue;

        public static void ReassertLoop(VisualElement element)
        {
            if (s_running.TryGetValue(element, out var binding))
            {
                ApplyCurrentFrame(element, binding);
            }
        }

        private static void ApplyCurrentFrame(VisualElement element, StyleAnimateBinding binding)
        {
            if (s_held.TryGetValue(element, out var held))
            {
                var slot = GuardedSlots(binding.Spec.Mode);
                foreach (var slots in held.Values)
                {
                    if ((slots & slot) != MotionTransitionSlots.None)
                    {
                        return;
                    }
                }
            }
            var elapsed = binding.Clock.NowSec - binding.StartTime;
            ApplyFrame(element, binding, Phase(elapsed, binding.Spec.DurationSec));
        }

        // Attaches a motion to an element whose gradient (the pan modes) is already applied. Sets the
        // once-per-attach background sizing for pan modes, takes the transition suspension, then schedules the
        // recurring tick on the panel root (deferred to attach when the element is off-panel). Returns the
        // binding to store.
        public static StyleAnimateBinding Attach(VisualElement element, AnimateSpec spec, bool panVertical,
            MotionClock? clock = null)
        {
            clock ??= MotionClock.Realtime;
            var binding = new StyleAnimateBinding
            {
                Spec = spec,
                StartTime = clock.NowSec,
                Clock = clock,
                PanVertical = panVertical,
            };

            // Pan modes need their background sizing established once (the per-frame write only moves the
            // position). Gradient oversizes the pan axis; Shimmer keeps the stretched box so its
            // transparent-ended band can sweep fully in and out. Both disable repeat so off-box is empty.
            if (spec.Mode == AnimateMode.Gradient || spec.Mode == AnimateMode.Shimmer)
            {
                ApplyPanSizing(element, spec.Mode, panVertical);
            }

            SyncTransitionSuspension(element, binding);
            ScheduleOrDefer(element, binding);
            s_running.AddOrUpdate(element, binding);
            return binding;
        }

        /// <summary>
        /// Suspends (or hands back) the element's native transitions over the slot this mode writes. Called at
        /// attach and again on each patch that leaves the motion running, because unlike a Motion play this
        /// binding outlives class-list changes: the transition utilities beside it can come and go while it runs.
        /// </summary>
        public static void SyncTransitionSuspension(VisualElement element, StyleAnimateBinding binding)
            => MotionNativeTransitionGuard.SyncSuspension(element, binding, GuardedSlots(binding.Spec.Mode));

        // The slot each mode writes on a tick. The pan modes also write background-size and
        // background-repeat, but at attach and once -- and above this call, so a transition covering
        // either takes that write whatever this returns.
#pragma warning disable CS8524 // no discard arm: a new mode has to name the slots it guards
        private static MotionTransitionSlots GuardedSlots(AnimateMode mode) => mode switch
        {
            AnimateMode.Spin => MotionTransitionSlots.Rotate,
            AnimateMode.Pulse => MotionTransitionSlots.Opacity,
            AnimateMode.Hue => MotionTransitionSlots.Filter,
            AnimateMode.Gradient or AnimateMode.Shimmer => MotionTransitionSlots.BackgroundPosition,
            AnimateMode.None => MotionTransitionSlots.None,
        };
#pragma warning restore CS8524

        // Tears down a running motion: hands back any transition suspension, pauses the tick, removes any
        // deferred-attach callback, and restores the styles the motion drove. Pan modes restore the gradient's
        // stretch-to-fill (the gradient itself may still be bound) and clear the panned position; each
        // shared-slot mode clears the slot it owned (filter for Hue, opacity for Pulse, rotate for Spin).
        public static void Detach(VisualElement element, StyleAnimateBinding binding)
        {
            MotionNativeTransitionGuard.Release(element, binding);
            s_running.Remove(element);
            binding.Scheduled?.Pause();
            binding.Scheduled = null;
            if (binding.PendingAttach != null)
            {
                element.UnregisterCallback(binding.PendingAttach);
                binding.PendingAttach = null;
            }

            if (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer)
            {
                // Restore the gradient's stretch-to-fill (matches GradientBackground.Apply) and drop the pan.
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
                element.style.backgroundPositionX = StyleKeyword.Null;
                element.style.backgroundPositionY = StyleKeyword.Null;
                element.style.backgroundRepeat = StyleKeyword.Null;
            }
            else if (binding.Spec.Mode == AnimateMode.Hue)
            {
                // The element's filter layers, variant ones included, were composed but not written while the loop
                // ran. An animation that ends starts no transition, so they are written at once.
                using (StyleFilterEngineWrite.WithoutTransition())
                {
                    StyleArbitraryValueResolver.RecomposeFilter(element);
                }
            }
            else if (binding.Spec.Mode == AnimateMode.Spin)
            {
                // Same ownership rule as the Pulse branch below, over the rotate slot.
                element.style.rotate = StyleKeyword.Null;
            }
            else if (binding.Spec.Mode == AnimateMode.Pulse)
            {
                // Pulse owns the opacity slot while active (a static opacity-* is shadowed — Pulse wins). Null
                // returns it to no-inline-opacity; a surviving class-driven opacity is re-asserted by the
                // reconciler right after Detach (a NAMED opacity-* re-resolves, an opacity-[.x] is re-applied).
                MotionOpacity.Write(element, StyleKeyword.Null);
            }
        }

        // Re-asserts a pan mode's background sizing. A steady-state patch (the animate spec is unchanged) may
        // follow a gradient re-bake — GradientBackground.Apply resets backgroundSize to 100% stretch-to-fill —
        // which would drag the Gradient pan's clamped edge into the box. Re-applying the oversize (and the
        // NoRepeat) keeps the pan correct. No-op for the non-pan modes (they own no background sizing).
        public static void ReapplyPanSizing(VisualElement element, StyleAnimateBinding binding)
        {
            if (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer)
            {
                ApplyPanSizing(element, binding.Spec.Mode, binding.PanVertical);
            }
        }

        // Decides the pan axis from a gradient's angle: vertical when the gradient flows more up/down than
        // left/right. 0/180 (to top / to bottom) → vertical; 90/270 (to right / to left) → horizontal.
        public static bool PanVerticalForAngle(float angleDeg)
        {
            var rad = angleDeg * Mathf.Deg2Rad;
            return Mathf.Abs(Mathf.Cos(rad)) > Mathf.Abs(Mathf.Sin(rad));
        }

        // Normalized loop position in [0,1), time-derived so a dropped tick never accumulates drift.
        public static float Phase(double elapsedSec, float durationSec)
        {
            if (durationSec <= 0f)
            {
                return 0f;
            }
            var frac = (float)(elapsedSec / durationSec);
            frac -= Mathf.Floor(frac);
            return frac < 0f ? frac + 1f : frac;
        }

        // The background-position offset (pixels) along the pan axis at loop position t, for a box of the given
        // extent. Gradient ping-pongs across the oversized background (range [-box, 0] via a triangle wave);
        // Shimmer sweeps one-way fully across the box (range [-box, +box] via a sawtooth).
        public static float PanOffsetPx(AnimateMode mode, float t, float box)
        {
            if (mode == AnimateMode.Shimmer)
            {
                return ((2f * t) - 1f) * box;
            }
            // Gradient: triangle wave 0→1→0 over the loop, panned leftward/upward by up to one box extent.
            var tri = 1f - Mathf.Abs((2f * t) - 1f);
            return -tri * box;
        }

        // The hue-rotate angle (degrees) at loop position t — a full 0..360 rotation per loop.
        public static float HueAngleDeg(float t) => 360f * t;

        // Linear, one turn per loop. Tailwind's spinner uses a linear timing function, and an eased one reads
        // as a stutter at the wrap because the loop restarts at full speed.
        public static float SpinAngleDeg(float t) => 360f * t;

        // Tailwind's pulse: opacity at half by the loop's midpoint, each half eased with
        // cubic-bezier(0.4, 0, 0.6, 1), since a keyframe animation applies its timing function per interval.
        public static float PulseOpacity(float t)
        {
            // MUTANT_SURVIVES(equivalent): at t = 0.5 the falling half ends and the rising half starts on the same
            // half opacity, so `<=` changes nothing.
            var falling = t < 0.5f;
            var progress = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.6f, 1f, falling ? t * 2f : (t * 2f) - 1f);
            return falling
                ? Mathf.LerpUnclamped(PulseMaxOpacity, PulseMinOpacity, progress)
                : Mathf.LerpUnclamped(PulseMinOpacity, PulseMaxOpacity, progress);
        }

        // Applies one frame at loop position t. Pan modes read the element's resolved box (so they need a
        // laid-out element); Hue is geometry-independent. Public so tests drive specific phases without the
        // runtime scheduler (which the EditMode PlayerLoop does not tick).
        public static void ApplyFrame(VisualElement element, StyleAnimateBinding binding, float t)
        {
            switch (binding.Spec.Mode)
            {
                case AnimateMode.Gradient:
                case AnimateMode.Shimmer:
                {
                    var box = binding.PanVertical ? element.resolvedStyle.height : element.resolvedStyle.width;
                    // Pre-layout (or off-panel) the resolved box is NaN / 0; skip the write so a NaN offset is
                    // never applied — the next tick after layout resolves writes a valid offset. Mirrors the
                    // geometry guards on the clip-path / skew paths.
                    if (float.IsNaN(box) || box <= 0f)
                    {
                        break;
                    }
                    var offset = PanOffsetPx(binding.Spec.Mode, t, box);
                    if (binding.PanVertical)
                    {
                        element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, offset);
                    }
                    else
                    {
                        element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, offset);
                    }
                    break;
                }
                case AnimateMode.Hue:
                {
                    var fn = new FilterFunction(FilterFunctionType.HueRotate);
                    fn.AddParameter(new FilterParameter(HueAngleDeg(t)));
                    // A FRESH list every frame is REQUIRED, not wasteful: UI Toolkit's inline-filter setter
                    // dirties the element only when the backing list REFERENCE changes (it ref-compares, not
                    // content-compares), so reusing one mutated list would repaint frame 1 then freeze the hue.
                    // The only cost is a 1-element list of a struct.
                    element.style.filter = new List<FilterFunction> { fn };
                    break;
                }
                case AnimateMode.Pulse:
                {
                    // Geometry-free: opacity is a value-compared float, so writing it each frame dirties the
                    // element correctly (no reference-list pitfall like the filter slot above).
                    MotionOpacity.Write(element, PulseOpacity(t));
                    break;
                }
                case AnimateMode.Spin:
                {
                    // Geometry-free, and a value-compared struct like opacity rather than a list like filter.
                    element.style.rotate = new Rotate(Angle.Degrees(SpinAngleDeg(t)));
                    break;
                }
            }
        }

        private static void ApplyPanSizing(VisualElement element, AnimateMode mode, bool panVertical)
        {
            if (mode == AnimateMode.Gradient)
            {
                var pan = Length.Percent(GradientOversize);
                var cross = Length.Percent(100f);
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(panVertical ? cross : pan, panVertical ? pan : cross));
            }
            else
            {
                // Shimmer keeps the gradient's stretch-to-fill; the transparent-ended band sweeps fully across.
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
            }
            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        }

        // Schedules the recurring tick on the panel root, or defers to AttachToPanelEvent when the element is
        // off-panel at attach (mirrors the exit scheduler: a host only exists once attached).
        private static void ScheduleOrDefer(VisualElement element, StyleAnimateBinding binding)
        {
            if (element.panel != null)
            {
                StartTick(element, binding);
                return;
            }
            EventCallback<AttachToPanelEvent>? onAttach = null;
            onAttach = _ =>
            {
                element.UnregisterCallback(onAttach);
                binding.PendingAttach = null;
                // Only start if this binding is still the live one (a cancel-before-attach clears Scheduled
                // and unregisters this; but guard the StartTime baseline against a long off-panel delay).
                binding.StartTime = binding.Clock.NowSec;
                StartTick(element, binding);
            };
            element.RegisterCallback(onAttach);
            binding.PendingAttach = onAttach;
        }

        private static void StartTick(VisualElement element, StyleAnimateBinding binding)
        {
            var host = element.panel.visualTree;
            binding.Scheduled = host.schedule.Execute(() =>
            {
                ApplyCurrentFrame(element, binding);
            }).Every(TickMs);
        }
    }
}
