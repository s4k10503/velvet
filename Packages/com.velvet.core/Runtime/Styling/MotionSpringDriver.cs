using System.Collections.Generic;
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
    /// One spring-driven color channel. A single scalar 0→1 PROGRESS spring drives a
    /// <see cref="Color.LerpUnclamped"/> between the endpoints rather than four independent per-component
    /// springs: four springs sharing one stiffness/damping/mass but different distances settle at different
    /// times, so the color would drift off the straight line between the two endpoints and arrive channel by
    /// channel. Interpolation stays in straight RGBA — the space UI Toolkit's own color transition uses — so
    /// switching a config between Tween and Spring cannot change which colors the element passes through.
    /// </summary>
    internal sealed class SpringColorChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly Color From;
        public readonly Color To;
        public readonly SpringChannel Progress;

        public SpringColorChannel(ArbitraryProperty property, Color from, Color to)
        {
            Property = property;
            From = from;
            To = to;
            Progress = new SpringChannel(0f, 1f);
        }
    }

    /// <summary>
    /// One spring-driven length channel: the property to write, the unit both endpoints share (see
    /// <see cref="MotionSpringClassParser.LengthChannelPlan"/>), and the magnitude spring itself.
    /// </summary>
    internal sealed class SpringLengthChannel
    {
        public readonly ArbitraryProperty Property;
        public readonly LengthUnit Unit;
        public readonly SpringChannel Value;

        public SpringLengthChannel(ArbitraryProperty property, float from, float to, LengthUnit unit)
        {
            Property = property;
            Unit = unit;
            Value = new SpringChannel(from, to);
        }
    }

    /// <summary>
    /// Running state for one spring-driven Motion variant enter/exit: the five fixed axes (see
    /// <see cref="SpringAxis"/>; translate x/y are always present together, never alone — see
    /// <see cref="MotionSpringClassParser.Resolve"/>) plus any color/length property channels the delta
    /// resolved, the shared stiffness/damping/mass, the recurring tick handle, and the completion callback.
    /// Owned by <c>StyleAnimationScheduler</c>'s <c>PendingAnimation</c>.
    /// </summary>
    internal sealed class MotionSpringState
    {
        public SpringChannel? Opacity;
        public SpringChannel? TranslateX;
        public SpringChannel? TranslateY;
        public SpringChannel? Scale;
        public SpringChannel? Rotate;
        public List<SpringColorChannel>? Colors;
        public List<SpringLengthChannel>? Lengths;

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
        // Rest epsilons, chosen per channel's natural scale (a 0.01 threshold is tuned for a roughly 0..1 range;
        // a channel in pixels or degrees needs a proportionally larger pair or it would spend many extra
        // (imperceptible) ticks converging on a threshold far tighter than the value's scale).
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
            if (plan.Colors != null)
            {
                var colors = new List<SpringColorChannel>(plan.Colors.Count);
                foreach (var c in plan.Colors)
                {
                    colors.Add(new SpringColorChannel(c.Property, c.From, c.To));
                }
                state.Colors = colors;
            }
            if (plan.Lengths != null)
            {
                var lengths = new List<SpringLengthChannel>(plan.Lengths.Count);
                foreach (var l in plan.Lengths)
                {
                    lengths.Add(new SpringLengthChannel(l.Property, l.From, l.To, l.Unit));
                }
                state.Lengths = lengths;
            }
            return state;
        }

        /// <summary>
        /// Suspends the element's native transitions for the rest of this play (see
        /// <see cref="MotionNativeTransitionGuard"/>) and writes each channel's CURRENT integrator value as an
        /// inline style, synchronously — so the element shows the from-pose on the very first rendered frame
        /// instead of flashing at the (already-applied) resting classes' value until the first tick runs.
        /// </summary>
        public static void ApplyCurrentValues(VisualElement element, MotionSpringState state)
        {
            MotionNativeTransitionGuard.Suspend(element);
            WriteChannelValues(element, state);
        }

        // The style writes themselves, without the once-per-play transition suspension above: re-asserting the
        // same inline transition-property on every tick would only re-dirty the element's computed transitions
        // for a value it already holds.
        private static void WriteChannelValues(VisualElement element, MotionSpringState state)
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
            if (state.Colors != null)
            {
                foreach (var c in state.Colors)
                {
                    StyleArbitraryValueResolver.ApplyInline(element,
                        new ArbitraryStyle(c.Property, MotionPropertyInterpolation.LerpColor(c.From, c.To, c.Progress.Integrator.Value)));
                }
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths)
                {
                    StyleArbitraryValueResolver.ApplyInline(element,
                        new ArbitraryStyle(l.Property, l.Value.Integrator.Value, l.Unit));
                }
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
            if (state.Colors != null)
            {
                foreach (var color in state.Colors)
                {
                    // The progress spring runs 0→1, so it settles on the same normalized scale opacity does.
                    var c = color.Progress;
                    c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                    settled &= c.Integrator.IsSettled(c.Target, NormalizedRestDelta, NormalizedRestSpeed);
                }
            }
            if (state.Lengths != null)
            {
                foreach (var length in state.Lengths)
                {
                    // A magnitude channel converges on the pixel scale whichever unit it carries: a percentage
                    // spans the same 0..100-ish range a pixel length does, so the same epsilon reads as the
                    // same fraction of the travel.
                    var c = length.Value;
                    c.Integrator.Step(dtSec, c.Target, state.Stiffness, state.Damping, state.Mass);
                    settled &= c.Integrator.IsSettled(c.Target, PixelRestDelta, PixelRestSpeed);
                }
            }

            // Every channel's Integrator.Value was just advanced above; re-applying them is exactly what the
            // initial (pre-tick) write already does, so the style writes live in exactly one place instead of
            // being duplicated here.
            WriteChannelValues(element, state);
            return settled;
        }

        /// <summary>
        /// Clears every inline override this state ever wrote — including the transition suspension
        /// <see cref="ApplyCurrentValues"/> put in place — letting the (already-resting) classes take back over.
        /// </summary>
        public static void ClearInlineOverrides(VisualElement element, MotionSpringState state)
        {
            if (state.Opacity != null) element.style.opacity = StyleKeyword.Null;
            if (state.TranslateX != null || state.TranslateY != null) element.style.translate = StyleKeyword.Null;
            if (state.Scale != null) element.style.scale = StyleKeyword.Null;
            if (state.Rotate != null) element.style.rotate = StyleKeyword.Null;
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) StyleArbitraryValueResolver.ClearInline(element, c.Property);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) StyleArbitraryValueResolver.ClearInline(element, l.Property);
            }
            MotionNativeTransitionGuard.Restore(element);
        }

        /// <summary>
        /// Re-targets every active channel toward the value it STARTED from (see
        /// <see cref="SpringChannel.RestingTarget"/>) — the exit-cancel hand-off. Each channel's
        /// <see cref="SpringIntegrator"/> instance is untouched, so its current value/velocity carry over
        /// unbroken; only the goal it steps toward next changes.
        /// </summary>
        public static void Retarget(MotionSpringState state) => ForEachActiveChannel(state, static c => c.Target = c.RestingTarget);

        /// <summary>
        /// Runs <paramref name="action"/> against every active <see cref="SpringChannel"/> on <paramref
        /// name="state"/> — the five optional axes plus each property channel's own integrator — the single
        /// place that walks the channel set for the callers (like <see cref="Retarget"/>) whose per-channel
        /// action does not otherwise depend on WHICH channel it is. Not used by the write/step/clear paths,
        /// where each element.style write is channel-specific (and translate x/y compose onto one shared
        /// inline style), which stay hand-written.
        /// </summary>
        private static void ForEachActiveChannel(MotionSpringState state, System.Action<SpringChannel> action)
        {
            if (state.Opacity != null) action(state.Opacity);
            if (state.TranslateX != null) action(state.TranslateX);
            if (state.TranslateY != null) action(state.TranslateY);
            if (state.Scale != null) action(state.Scale);
            if (state.Rotate != null) action(state.Rotate);
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) action(c.Progress);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) action(l.Value);
            }
        }
    }
}
