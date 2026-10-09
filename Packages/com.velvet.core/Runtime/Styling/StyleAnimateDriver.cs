using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a loop knows of a slot it writes that its element's classes also give: the element's own inline value, which
    // a keyframe's implicit first and last frame start from, and the value the loop last left there. An inline value
    // that differs from the one the loop left was written by something else since, and is the element's own from then
    // on; one that matches is the loop's frame and is not.
    internal struct LoopSlot<TStyle> where TStyle : struct, System.IEquatable<TStyle>
    {
        private TStyle _own;
        private TStyle _written;
        private bool _hasWritten;

        public void Seed(TStyle inline) => _own = inline;

        // True while the slot still holds the frame this loop last wrote, so it is the loop's rather than anyone's own.
        public bool HoldsWrittenFrame(TStyle inline) => _hasWritten && inline.Equals(_written);

        public TStyle OwnValue => _own;

        // adopt is false for a write the caller knows is not the element's own (a Motion driver's frame, a
        // crossfade's), which leaves the inline value out of the element's own.
        public TStyle Own(TStyle inline, bool adopt)
        {
            if (adopt && (!_hasWritten || !inline.Equals(_written)))
            {
                _own = inline;
            }
            return _own;
        }

        public void Wrote(TStyle inline)
        {
            _written = inline;
            _hasWritten = true;
        }
    }

    // Per-element state for a running animate-* motion. Holds the recurring scheduled tick (paused on
    // teardown), the loop start time, and the pan-axis decision (pan modes only).
    internal sealed class StyleAnimateBinding
    {
        public AnimateSpec Spec;
        // Wall-clock start of the loop (Time.realtimeSinceStartupAsDouble). The phase is derived from elapsed
        // time, so a dropped frame never accumulates drift (unlike a per-tick increment).
        public double StartTime;
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
        // The slots the non-pan modes write over what the element's classes give them. A mode uses the ones it writes.
        public LoopSlot<StyleFloat> Opacity;
        public LoopSlot<StyleRotate> Rotation;
        public LoopSlot<StyleScale> Scale;
        public LoopSlot<StyleTranslate> Translation;
        // What the element's classes give those slots, re-read when the class list or anything else that decides the
        // element's rules changes.
        public CascadeSnapshot Cascade;
        public VisualElement? Element;
        // The loop's share of the slots it writes, kept current whether or not the loop writes them: the offset in the
        // element's pixels a bounce lifts it by, and the factor a ping scales it by. A layoutId projection that holds
        // the slots adds these to its own frame.
        public Vector2 LiftPx;
        public float PingFactor = 1f;
        // A pan mode's sizing, position and no-repeat stand over the element's own inline values (a
        // StyleOverrides.BackgroundRepeat, the gradient's stretch-to-fill, anything a refCallback wrote, or none,
        // leaving the slot to its classes); these are those values, written back when the loop lets go.
        public StyleBackgroundRepeat RepeatUnderPan = StyleKeyword.Null;
        public StyleBackgroundSize SizeUnderPan = StyleKeyword.Null;
        public StyleBackgroundPosition PositionXUnderPan = StyleKeyword.Null;
        public StyleBackgroundPosition PositionYUnderPan = StyleKeyword.Null;
    }

    // Drives the animate-* motions. The texture is baked ONCE (the static gradient path); this only writes a
    // cheap inline style per frame — a background-position offset (Gradient/Shimmer), a hue-rotate filter
    // angle (Hue), an opacity (Pulse), a rotation (Spin), an opacity and scale (Ping) or a translation (Bounce) — so
    // a continuously-animating gradient costs no per-frame texture work. The phase math is pure (PanOffsetPx /
    // HueAngleDeg / PulseOpacityOver / SpinAngleDeg / PingProgress / BounceLift / BounceOffsetPx / Phase) and
    // unit-tested directly; the scheduler wiring is exercised at runtime (the EditMode PlayerLoop
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
        // Ping reaches its end values at three quarters of the loop. Bounce lifts by a quarter of the element's height.
        private const float PingEndPhase = 0.75f;
        private const float PingEndScale = 2f;
        private const float BounceLiftFraction = 0.25f;

        // The loop each element runs, for ReassertLoop.
        private static readonly ConditionalWeakTable<VisualElement, StyleAnimateBinding> s_running = new();

        // The slots each driver on the element writes over its loop, by driver.
        private static readonly ConditionalWeakTable<VisualElement, Dictionary<object, MotionTransitionSlots>> s_held = new();

        // The slots a layoutId projection writes, whose frame the loop's share is added to instead of the loop writing
        // them. Kept apart from s_held, which stops the loop computing a frame at all.
        private static readonly ConditionalWeakTable<VisualElement, Dictionary<object, MotionTransitionSlots>> s_yielded = new();

        // Every running loop, so a change to what decides an element's rules can reach the loops it affects.
        private static readonly List<StyleAnimateBinding> s_loops = new();

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
        public static void ForgetHolds(VisualElement element)
        {
            s_held.Remove(element);
            s_yielded.Remove(element);
        }

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

        // driven names the slots the calling driver writes or has just cleared, whose inline value is its own and not
        // the element's (LoopSlot); a slot outside it that differs from the loop's last frame was written by something
        // else and is the element's own.
        public static void ReassertLoop(VisualElement element, MotionTransitionSlots driven)
        {
            if (s_running.TryGetValue(element, out var binding))
            {
                ApplyCurrentFrame(element, binding, driven);
            }
        }

        /// <summary>
        /// Records the slots a layoutId projection writes on the element until it is called again with
        /// <see cref="MotionTransitionSlots.None"/>. The loop leaves them unwritten and keeps its share current
        /// (<see cref="CurrentLiftPx"/>, <see cref="CurrentPingFactor"/>) for the projection to add to its own frame,
        /// so that neither takes the other's frame for the element's own.
        /// </summary>
        public static void YieldSlots(VisualElement element, object owner, MotionTransitionSlots slots)
        {
            var yielded = s_yielded.GetValue(element, static _ => new Dictionary<object, MotionTransitionSlots>());
            yielded.Remove(owner);
            if (slots != MotionTransitionSlots.None)
            {
                yielded[owner] = slots;
            }
        }

        // The offset in the element's pixels a running bounce lifts it by, or zero.
        internal static Vector2 CurrentLiftPx(VisualElement element)
            => s_running.TryGetValue(element, out var binding) && binding.Spec.Mode == AnimateMode.Bounce
                ? binding.LiftPx
                : Vector2.zero;

        // The factor a running ping scales the element by, or one.
        internal static float CurrentPingFactor(VisualElement element)
            => s_running.TryGetValue(element, out var binding) && binding.Spec.Mode == AnimateMode.Ping
                ? binding.PingFactor
                : 1f;

        /// <summary>
        /// Marks the loops on <paramref name="element"/> and beneath it as needing the element's class values read
        /// again: a rule can match by an ancestor's classes without anything changing on the loop's own element.
        /// </summary>
        public static void NotifySubtreeStyleChanged(VisualElement element)
        {
            foreach (var binding in s_loops)
            {
                if (binding.Element is { } loop && (loop == element || element.Contains(loop)))
                {
                    binding.Cascade.Invalidate();
                }
            }
        }

        private static void NotifyAllStyleChanged()
        {
            foreach (var binding in s_loops)
            {
                binding.Cascade.Invalidate();
            }
        }

        // The events that move an element between pseudo-states, which a rule can match on with no class change.
        private static void RegisterStyleInputs(VisualElement element, StyleAnimateBinding binding)
        {
            element.RegisterCallback<PointerOverEvent, StyleAnimateBinding>(OnStyleInput, binding);
            element.RegisterCallback<PointerOutEvent, StyleAnimateBinding>(OnStyleInput, binding);
            element.RegisterCallback<FocusInEvent, StyleAnimateBinding>(OnStyleInput, binding);
            element.RegisterCallback<FocusOutEvent, StyleAnimateBinding>(OnStyleInput, binding);
            element.RegisterCallback<PointerDownEvent, StyleAnimateBinding>(OnStyleInput, binding);
            element.RegisterCallback<PointerUpEvent, StyleAnimateBinding>(OnStyleInput, binding);
        }

        private static void UnregisterStyleInputs(VisualElement element)
        {
            element.UnregisterCallback<PointerOverEvent, StyleAnimateBinding>(OnStyleInput);
            element.UnregisterCallback<PointerOutEvent, StyleAnimateBinding>(OnStyleInput);
            element.UnregisterCallback<FocusInEvent, StyleAnimateBinding>(OnStyleInput);
            element.UnregisterCallback<FocusOutEvent, StyleAnimateBinding>(OnStyleInput);
            element.UnregisterCallback<PointerDownEvent, StyleAnimateBinding>(OnStyleInput);
            element.UnregisterCallback<PointerUpEvent, StyleAnimateBinding>(OnStyleInput);
        }

        private static void OnStyleInput(EventBase evt, StyleAnimateBinding binding) => binding.Cascade.Invalidate();

        // The element's own translate under a running bounce whose frame sits in the slot: what the bounce moves from,
        // for a layoutId projection that would otherwise take the frame for the element's own. False when the slot
        // holds anything else.
        internal static bool TryReadBounceOwn(VisualElement element, StyleTranslate inline, out StyleTranslate ownInline,
            out Translate own)
        {
            ownInline = default;
            own = default;
            if (!s_running.TryGetValue(element, out var binding) || binding.Spec.Mode != AnimateMode.Bounce
                || !binding.Translation.HoldsWrittenFrame(inline))
            {
                return false;
            }
            ownInline = binding.Translation.OwnValue;
            own = ReadTranslate(element, binding, ownInline);
            return true;
        }

        // As TryReadBounceOwn, for the scale a running ping writes.
        internal static bool TryReadPingOwn(VisualElement element, StyleScale inline, out StyleScale ownInline,
            out Vector3 own)
        {
            ownInline = default;
            own = default;
            if (!s_running.TryGetValue(element, out var binding) || binding.Spec.Mode != AnimateMode.Ping
                || !binding.Scale.HoldsWrittenFrame(inline))
            {
                return false;
            }
            ownInline = binding.Scale.OwnValue;
            own = ReadScale(element, binding, ownInline);
            return true;
        }

        private static void ApplyCurrentFrame(VisualElement element, StyleAnimateBinding binding, MotionTransitionSlots driven)
        {
            var free = GuardedSlots(binding.Spec.Mode);
            var yielded = MotionTransitionSlots.None;
            if (s_yielded.TryGetValue(element, out var yields))
            {
                foreach (var slots in yields.Values)
                {
                    yielded |= slots;
                }
            }
            if (s_held.TryGetValue(element, out var held))
            {
                foreach (var slots in held.Values)
                {
                    free &= ~slots;
                }
            }
            if (free == MotionTransitionSlots.None)
            {
                return;
            }
            var elapsed = Time.realtimeSinceStartupAsDouble - binding.StartTime;
            WriteFrame(element, binding, Phase(elapsed, binding.Spec.DurationSec), free, driven | yielded, yielded);
        }

        // Attaches a motion to an element whose gradient (the pan modes) is already applied. Sets the
        // once-per-attach background sizing for pan modes, takes the transition suspension, then schedules the
        // recurring tick on the panel root (deferred to attach when the element is off-panel). Returns the
        // binding to store.
        public static StyleAnimateBinding Attach(VisualElement element, AnimateSpec spec, bool panVertical)
        {
            var binding = new StyleAnimateBinding
            {
                Spec = spec,
                StartTime = Time.realtimeSinceStartupAsDouble,
                PanVertical = panVertical,
            };

            // Pan modes need their background sizing established once (the per-frame write only moves the
            // position). Gradient oversizes the pan axis; Shimmer keeps the stretched box so its
            // transparent-ended band can sweep fully in and out. Both disable repeat so off-box is empty.
            if (spec.Mode == AnimateMode.Gradient || spec.Mode == AnimateMode.Shimmer)
            {
                binding.RepeatUnderPan = element.style.backgroundRepeat;
                binding.SizeUnderPan = element.style.backgroundSize;
                binding.PositionXUnderPan = element.style.backgroundPositionX;
                binding.PositionYUnderPan = element.style.backgroundPositionY;
                ApplyPanSizing(element, spec.Mode, panVertical);
            }

            SeedOwnValues(element, binding);
            binding.Element = element;
            RegisterStyleInputs(element, binding);
            if (s_loops.Count == 0)
            {
                VelvetTheme.DarkModeChanged += NotifyAllStyleChanged;
            }
            s_loops.Add(binding);
            SyncTransitionSuspension(element, binding);
            ScheduleOrDefer(element, binding);
            s_running.AddOrUpdate(element, binding);
            return binding;
        }

        // The inline values the element holds when the loop attaches are its own, which a keyframe's implicit first and
        // last frame start from.
        private static void SeedOwnValues(VisualElement element, StyleAnimateBinding binding)
        {
            var style = element.style;
            binding.Opacity.Seed(style.opacity);
            binding.Rotation.Seed(style.rotate);
            binding.Scale.Seed(style.scale);
            binding.Translation.Seed(style.translate);
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
            AnimateMode.Ping => MotionTransitionSlots.Opacity | MotionTransitionSlots.Scale,
            AnimateMode.Bounce => MotionTransitionSlots.Translate,
            AnimateMode.Gradient or AnimateMode.Shimmer => MotionTransitionSlots.BackgroundPosition,
            AnimateMode.None => MotionTransitionSlots.None,
        };
#pragma warning restore CS8524

        // An inline background-repeat written from outside the loop. While a pan mode holds the slot the value
        // waits for Detach to write it, so the pan keeps its no-repeat and the element ends on the newest value.
        public static void WriteBackgroundRepeat(VisualElement element, StyleBackgroundRepeat value)
        {
            if (s_running.TryGetValue(element, out var binding)
                && (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer))
            {
                binding.RepeatUnderPan = value;
                return;
            }
            element.style.backgroundRepeat = value;
        }

        // Tears down a running motion: hands back any transition suspension, pauses the tick, removes any
        // deferred-attach callback, and restores the styles the motion drove. Pan modes write back the background
        // sizing, position and repeat they covered (StyleAnimateBinding.RepeatUnderPan and its siblings); each
        // shared-slot mode clears the slot it owned (filter for Hue, opacity for Pulse, rotate for Spin).
        public static void Detach(VisualElement element, StyleAnimateBinding binding)
        {
            MotionNativeTransitionGuard.Release(element, binding);
            s_running.Remove(element);
            UnregisterStyleInputs(element);
            if (s_loops.Remove(binding) && s_loops.Count == 0)
            {
                VelvetTheme.DarkModeChanged -= NotifyAllStyleChanged;
            }
            binding.LiftPx = Vector2.zero;
            binding.PingFactor = 1f;
            binding.Scheduled?.Pause();
            binding.Scheduled = null;
            if (binding.PendingAttach != null)
            {
                element.UnregisterCallback(binding.PendingAttach);
                binding.PendingAttach = null;
            }

            if (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer)
            {
                element.style.backgroundSize = binding.SizeUnderPan;
                element.style.backgroundPositionX = binding.PositionXUnderPan;
                element.style.backgroundPositionY = binding.PositionYUnderPan;
                element.style.backgroundRepeat = binding.RepeatUnderPan;
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
            else if (binding.Spec.Mode == AnimateMode.Ping)
            {
                // Same ownership rule as the Pulse and Spin branches, over the opacity and scale slots.
                MotionOpacity.Write(element, StyleKeyword.Null);
                element.style.scale = StyleKeyword.Null;
            }
            else if (binding.Spec.Mode == AnimateMode.Bounce)
            {
                // Same ownership rule as the Spin branch, over the translate slot.
                element.style.translate = StyleKeyword.Null;
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

        // Tailwind's pulse over an element at full opacity.
        public static float PulseOpacity(float t) => PulseOpacityOver(PulseMaxOpacity, t);

        // Tailwind's pulse: opacity at half by the loop's midpoint, each half eased with
        // cubic-bezier(0.4, 0, 0.6, 1), since a keyframe animation applies its timing function per interval. The
        // keyframes name no opacity at 0% and 100%, so those take the element's own.
        public static float PulseOpacityOver(float ownOpacity, float t)
        {
            // MUTANT_SURVIVES(equivalent): at t = 0.5 the falling half ends and the rising half starts on the same
            // half opacity, so `<=` changes nothing.
            var falling = t < 0.5f;
            var progress = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.6f, 1f, falling ? t * 2f : (t * 2f) - 1f);
            return falling
                ? Mathf.LerpUnclamped(ownOpacity, PulseMinOpacity, progress)
                : Mathf.LerpUnclamped(PulseMinOpacity, ownOpacity, progress);
        }

        // Tailwind's ping: scale(2) and opacity 0 are reached at 75% and held to 100%, the first three quarters eased
        // with cubic-bezier(0, 0, 0.2, 1). The keyframes name neither at 0%, so that frame is the element's own.
        // Returns the share of the way from the element's own values.
        public static float PingProgress(float t)
            => t >= PingEndPhase ? 1f : CubicBezierEvaluator.Evaluate(0f, 0f, 0.2f, 1f, t / PingEndPhase);

        // Tailwind's bounce: translateY(-25%) at 0% and 100%, none at 50%, the first half eased with
        // cubic-bezier(0.8, 0, 1, 1) and the second with cubic-bezier(0, 0, 0.2, 1). Returns the share of that
        // quarter-height lift the element stands at.
        public static float BounceLift(float t)
            => t < 0.5f
                ? 1f - CubicBezierEvaluator.Evaluate(0.8f, 0f, 1f, 1f, t * 2f)
                : CubicBezierEvaluator.Evaluate(0f, 0f, 0.2f, 1f, (t * 2f) - 1f);

        // Where a bounce at the given lift moves an element of the given height, in the parent's pixels. The keyframes'
        // transform applies innermost, beneath the element's own scale and rotate, so the lift is scaled and turned by
        // them.
        public static Vector2 BounceOffsetPx(float lift, float height, float ownRotateDeg, float ownScaleY)
        {
            var lifted = -BounceLiftFraction * height * lift * ownScaleY;
            var radians = ownRotateDeg * Mathf.Deg2Rad;
            return new Vector2(-lifted * Mathf.Sin(radians), lifted * Mathf.Cos(radians));
        }

        // Applies one frame at loop position t. Pan modes read the element's resolved box (so they need a
        // laid-out element); Hue is geometry-independent. Public so tests drive specific phases without the
        // runtime scheduler (which the EditMode PlayerLoop does not tick).
        public static void ApplyFrame(VisualElement element, StyleAnimateBinding binding, float t)
            => WriteFrame(element, binding, t, GuardedSlots(binding.Spec.Mode), MotionTransitionSlots.None, MotionTransitionSlots.None);

        // free names the slots no Motion driver is holding against the loop (HoldAgainstLoop), driven the slots whose
        // inline value is somebody's frame rather than the element's own, and yielded the ones a layoutId projection
        // writes.
        private static void WriteFrame(VisualElement element, StyleAnimateBinding binding, float t,
            MotionTransitionSlots free, MotionTransitionSlots driven, MotionTransitionSlots yielded)
        {
            binding.Cascade.BeginFrame();
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
                    // Geometry-free: opacity is a value-compared float, so writing it each frame dirties the
                    // element correctly (no reference-list pitfall like the filter slot above).
                    WriteOpacity(element, binding, PulseOpacityOver(OwnOpacity(element, binding, driven), t));
                    break;
                case AnimateMode.Spin:
                    // Geometry-free, and a value-compared struct like opacity rather than a list like filter.
                    WriteRotation(element, binding, driven, t);
                    break;
                case AnimateMode.Ping:
                    ApplyPing(element, binding, t, free, driven, yielded);
                    break;
                case AnimateMode.Bounce:
                    ApplyBounce(element, binding, t, driven, yielded);
                    break;
            }
        }

        // The Own* helpers return the element's own value of a slot: the inline value that is not the loop's frame or
        // a driver's, else what its classes cascade to, else the slot's initial value.
        private static bool Adopts(MotionTransitionSlots driven, MotionTransitionSlots slot)
            => (driven & slot) == MotionTransitionSlots.None;

        private static float OwnOpacity(VisualElement element, StyleAnimateBinding binding, MotionTransitionSlots driven)
        {
            // A crossfade's frame sits in the slot while one draws the element, and is not the element's own.
            var adopt = Adopts(driven, MotionTransitionSlots.Opacity) && !MotionOpacity.Draws(element);
            var own = binding.Opacity.Own(element.style.opacity, adopt);
            if (own.keyword == StyleKeyword.Undefined)
            {
                return own.value;
            }
            binding.Cascade.Refresh(element);
            return binding.Cascade.TryOpacity(out var cascaded) ? cascaded : 1f;
        }

        private static float ReadRotationDeg(VisualElement element, StyleAnimateBinding binding, StyleRotate own)
        {
            if (own.keyword == StyleKeyword.Undefined)
            {
                return own.value.angle.ToDegrees();
            }
            binding.Cascade.Refresh(element);
            return binding.Cascade.TryRotate(out var cascaded) ? cascaded.angle.ToDegrees() : 0f;
        }

        private static Vector3 ReadScale(VisualElement element, StyleAnimateBinding binding, StyleScale own)
        {
            if (own.keyword == StyleKeyword.Undefined)
            {
                return own.value.value;
            }
            binding.Cascade.Refresh(element);
            return binding.Cascade.TryScale(out var cascaded) ? cascaded.value : Vector3.one;
        }

        private static Translate ReadTranslate(VisualElement element, StyleAnimateBinding binding, StyleTranslate own)
        {
            if (own.keyword == StyleKeyword.Undefined)
            {
                return own.value;
            }
            binding.Cascade.Refresh(element);
            return binding.Cascade.TryTranslate(out var cascaded) ? cascaded : default;
        }

        // MotionOpacity.Write rather than the style: a layoutId crossfade holds the slot through it.
        private static void WriteOpacity(VisualElement element, StyleAnimateBinding binding, float opacity)
        {
            MotionOpacity.Write(element, opacity);
            binding.Opacity.Wrote(element.style.opacity);
        }

        // The turn adds to the element's own rotation, as the keyframes' transform composes with `rotate`. That is
        // the CSS result for an even scale; under an uneven one CSS turns the content beneath the scale
        // (rotate, then scale, then the keyframe turn), which a single rotate and scale cannot express, so the squash
        // axes turn with the element here.
        private static void WriteRotation(VisualElement element, StyleAnimateBinding binding,
            MotionTransitionSlots driven, float t)
        {
            var own = binding.Rotation.Own(element.style.rotate, Adopts(driven, MotionTransitionSlots.Rotate));
            var turn = ReadRotationDeg(element, binding, own) + SpinAngleDeg(t);
            element.style.rotate = new Rotate(Angle.Degrees(turn));
            binding.Rotation.Wrote(element.style.rotate);
        }

        private static void ApplyPing(VisualElement element, StyleAnimateBinding binding, float t,
            MotionTransitionSlots free, MotionTransitionSlots driven, MotionTransitionSlots yielded)
        {
            var progress = PingProgress(t);
            var factor = Mathf.LerpUnclamped(1f, PingEndScale, progress);
            binding.PingFactor = factor;
            if ((free & MotionTransitionSlots.Opacity) != MotionTransitionSlots.None)
            {
                WriteOpacity(element, binding, Mathf.LerpUnclamped(OwnOpacity(element, binding, driven), 0f, progress));
            }
            if ((free & ~yielded & MotionTransitionSlots.Scale) != MotionTransitionSlots.None)
            {
                // The keyframes' scale(2) multiplies the element's own `scale` rather than replacing it.
                var inline = binding.Scale.Own(element.style.scale, Adopts(driven, MotionTransitionSlots.Scale));
                var own = ReadScale(element, binding, inline);
                element.style.scale = new Scale(new Vector3(own.x * factor, own.y * factor, own.z));
                binding.Scale.Wrote(element.style.scale);
            }
        }

        private static void ApplyBounce(VisualElement element, StyleAnimateBinding binding, float t,
            MotionTransitionSlots driven, MotionTransitionSlots yielded)
        {
            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            // Pre-layout the resolved box is NaN / 0, and a lift of a quarter of it has nothing to measure; the pan
            // modes skip the same frame.
            if (float.IsNaN(width) || float.IsNaN(height) || height <= 0f)
            {
                return;
            }
            var turnInline = binding.Rotation.Own(element.style.rotate, Adopts(driven, MotionTransitionSlots.Rotate));
            var scaleInline = binding.Scale.Own(element.style.scale, Adopts(driven, MotionTransitionSlots.Scale));
            var turn = ReadRotationDeg(element, binding, turnInline);
            var scale = ReadScale(element, binding, scaleInline);
            var lift = BounceOffsetPx(BounceLift(t), height, turn, scale.y);
            binding.LiftPx = lift;
            if ((yielded & MotionTransitionSlots.Translate) != MotionTransitionSlots.None)
            {
                return;
            }
            var translation = binding.Translation.Own(element.style.translate, Adopts(driven, MotionTransitionSlots.Translate));
            var own = ReadTranslate(element, binding, translation);
            var x = LengthPx(own.x, width) + lift.x;
            var y = LengthPx(own.y, height) + lift.y;
            element.style.translate = new Translate(new Length(x), new Length(y), own.z);
            binding.Translation.Wrote(element.style.translate);
        }

        // A translate component in pixels; a percentage is of the element's own extent along that axis.
        private static float LengthPx(Length length, float extent)
            => length.unit == LengthUnit.Percent ? length.value / 100f * extent : length.value;

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
                binding.StartTime = Time.realtimeSinceStartupAsDouble;
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
                ApplyCurrentFrame(element, binding, MotionTransitionSlots.None);
            }).Every(TickMs);
        }
    }
}
