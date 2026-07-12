using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One spring-animated channel: an integrator plus the value it is currently heading toward, and the value
    /// it started from (its resting/pre-animation value, used only by <see cref="MotionSpringDriver.Retarget"/>
    /// on an exit-cancel hand-off — see <see cref="StyleAnimationScheduler"/>).
    /// </summary>
    internal sealed class SpringChannel
    {
        // Deliberately NOT readonly: SpringIntegrator is a mutable struct embedded inline (see its own doc), and
        // every Step call mutates it in place through this field. Marking the field readonly would make the
        // compiler silently invoke Step on a defensive COPY instead — the spring would never advance, only
        // ever reporting its initial value forever.
        public SpringIntegrator Integrator;
        public float Target;
        public readonly float RestingTarget;

        public SpringChannel(float initialValue, float target)
        {
            Integrator = new SpringIntegrator(initialValue);
            Target = target;
            RestingTarget = initialValue;
        }
    }

    /// <summary>
    /// Running state for one spring-driven Motion variant enter/exit: up to five channels (see
    /// <see cref="SpringAxis"/>; translate x/y are always present together, never alone — see
    /// <see cref="MotionSpringClassParser.Resolve"/>), the shared stiffness/damping/mass, the recurring tick
    /// handle, and the completion callback. Owned by <c>StyleAnimationScheduler</c>'s <c>PendingAnimation</c>.
    /// </summary>
    internal sealed class MotionSpringState
    {
        public SpringChannel? Opacity;
        public SpringChannel? TranslateX;
        public SpringChannel? TranslateY;
        public SpringChannel? Scale;
        public SpringChannel? Rotate;

        public float Stiffness;
        public float Damping;
        public float Mass;

        // The recurring tick, scheduled on the panel root. Paused (and nulled) once every channel has settled,
        // or on cancel.
        public IVisualElementScheduledItem? Tick;

        // Runs once every channel has settled (after the driver has already cleared the inline overrides).
        // The natural-completion caller sets this to its onComplete; an exit-cancel hand-off (see
        // StyleAnimationScheduler.CancelPending) clears it to null — a reversal settling is not "finishing"
        // anything the original caller asked for.
        public System.Action? OnSettled;
    }

    /// <summary>
    /// Pure(ish) mechanics for a spring-driven Motion variant enter/exit: builds the per-channel state from a
    /// resolved <see cref="MotionSpringClassParser.SpringPlan"/>, applies/steps/clears the inline styles a
    /// channel owns, and retargets on a cancel hand-off. Takes no dependency on scheduling, panels, or the
    /// enter/exit bookkeeping maps — see <see cref="StyleAnimationScheduler"/> for the piece that decides WHEN to
    /// start/stop/retarget one of these and owns the actual recurring <c>schedule.Execute</c> tick.
    /// </summary>
    internal static class MotionSpringDriver
    {
        // Rest epsilons, chosen per channel's natural scale (Framer Motion's own defaults — 0.01 — are tuned for
        // a roughly 0..1 range; a channel in pixels or degrees needs a proportionally larger pair or it would
        // spend many extra (imperceptible) ticks converging on a threshold far tighter than the value's scale).
        private const float NormalizedRestDelta = 0.001f; // opacity / uniform scale (~0..1 / ~1 range)
        private const float NormalizedRestSpeed = 0.001f;
        private const float PixelRestDelta = 0.1f; // translate x/y (pixels)
        private const float PixelRestSpeed = 0.1f;
        private const float DegreeRestDelta = 0.1f; // rotate (degrees)
        private const float DegreeRestSpeed = 0.1f;

        /// <summary>
        /// Builds the running state from a resolved plan, or null when the plan animates nothing (the caller
        /// should treat this exactly like a zero-duration tween: land the classes and complete immediately).
        /// </summary>
        public static MotionSpringState? Create(MotionSpringClassParser.SpringPlan plan, float stiffness, float damping, float mass)
        {
            if (plan.IsEmpty)
            {
                return null;
            }
            var state = new MotionSpringState { Stiffness = stiffness, Damping = damping, Mass = mass };
            if (plan.Opacity is { } o) state.Opacity = new SpringChannel(o.from, o.to);
            if (plan.TranslateX is { } tx) state.TranslateX = new SpringChannel(tx.from, tx.to);
            if (plan.TranslateY is { } ty) state.TranslateY = new SpringChannel(ty.from, ty.to);
            if (plan.Scale is { } s) state.Scale = new SpringChannel(s.from, s.to);
            if (plan.Rotate is { } r) state.Rotate = new SpringChannel(r.from, r.to);
            return state;
        }

        /// <summary>
        /// Writes each channel's CURRENT integrator value as an inline style, synchronously — so the element
        /// shows the from-pose on the very first rendered frame instead of flashing at the (already-applied)
        /// resting classes' value until the first tick runs.
        /// </summary>
        public static void ApplyCurrentValues(VisualElement element, MotionSpringState state)
        {
            if (state.Opacity != null)
            {
                element.style.opacity = state.Opacity.Integrator.Value;
            }
            if (state.TranslateX != null || state.TranslateY != null)
            {
                element.style.translate = new Translate(
                    new Length(state.TranslateX?.Integrator.Value ?? 0f),
                    new Length(state.TranslateY?.Integrator.Value ?? 0f));
            }
            if (state.Scale != null)
            {
                var v = state.Scale.Integrator.Value;
                element.style.scale = new Scale(new Vector2(v, v));
            }
            if (state.Rotate != null)
            {
                element.style.rotate = new Rotate(Angle.Degrees(state.Rotate.Integrator.Value));
            }
        }

        /// <summary>
        /// Steps every active channel by <paramref name="dtSec"/> and re-applies the inline styles. Returns true
        /// once EVERY channel has settled at its (possibly retargeted) target.
        /// </summary>
        public static bool Step(VisualElement element, MotionSpringState state, float dtSec)
        {
            var settled = true;

            if (state.Opacity != null)
            {
                var c = state.Opacity;
                c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                settled &= c.Integrator.IsSettled(c.Target, NormalizedRestDelta, NormalizedRestSpeed);
            }
            if (state.TranslateX != null)
            {
                var c = state.TranslateX;
                c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                settled &= c.Integrator.IsSettled(c.Target, PixelRestDelta, PixelRestSpeed);
            }
            if (state.TranslateY != null)
            {
                var c = state.TranslateY;
                c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                settled &= c.Integrator.IsSettled(c.Target, PixelRestDelta, PixelRestSpeed);
            }
            if (state.Scale != null)
            {
                var c = state.Scale;
                c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                settled &= c.Integrator.IsSettled(c.Target, NormalizedRestDelta, NormalizedRestSpeed);
            }
            if (state.Rotate != null)
            {
                var c = state.Rotate;
                c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                settled &= c.Integrator.IsSettled(c.Target, DegreeRestDelta, DegreeRestSpeed);
            }

            // Every channel's Integrator.Value was just advanced above; re-applying them is exactly what
            // ApplyCurrentValues already does for the initial (pre-tick) write, so the four style writes live
            // in exactly one place instead of being duplicated here.
            ApplyCurrentValues(element, state);
            return settled;
        }

        /// <summary>Clears every inline override this state ever wrote, letting the (already-resting) classes take back over.</summary>
        public static void ClearInlineOverrides(VisualElement element, MotionSpringState state)
        {
            if (state.Opacity != null) element.style.opacity = StyleKeyword.Null;
            if (state.TranslateX != null || state.TranslateY != null) element.style.translate = StyleKeyword.Null;
            if (state.Scale != null) element.style.scale = StyleKeyword.Null;
            if (state.Rotate != null) element.style.rotate = StyleKeyword.Null;
        }

        /// <summary>
        /// Re-targets every active channel toward the value it STARTED from (see
        /// <see cref="SpringChannel.RestingTarget"/>) — the exit-cancel hand-off. Each channel's
        /// <see cref="SpringIntegrator"/> instance is untouched, so its current value/velocity carry over
        /// unbroken; only the goal it steps toward next changes.
        /// </summary>
        public static void Retarget(MotionSpringState state) => ForEachActiveChannel(state, static c => c.Target = c.RestingTarget);

        /// <summary>
        /// Runs <paramref name="action"/> against whichever of the five optional channels on <paramref
        /// name="state"/> are active (non-null) — the single place that walks the fixed channel set for the
        /// callers (like <see cref="Retarget"/>) whose per-channel action does not otherwise depend on WHICH
        /// channel it is. Not used by <see cref="ApplyCurrentValues"/> / <see cref="Step"/> (each element.style
        /// write is channel-specific, and translate x/y compose onto one shared inline style) or
        /// <see cref="ClearInlineOverrides"/> (same translate composition), which stay hand-written.
        /// </summary>
        private static void ForEachActiveChannel(MotionSpringState state, System.Action<SpringChannel> action)
        {
            if (state.Opacity != null) action(state.Opacity);
            if (state.TranslateX != null) action(state.TranslateX);
            if (state.TranslateY != null) action(state.TranslateY);
            if (state.Scale != null) action(state.Scale);
            if (state.Rotate != null) action(state.Rotate);
        }
    }
}
