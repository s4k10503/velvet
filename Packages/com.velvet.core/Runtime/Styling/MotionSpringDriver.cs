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
        // The length of one pass, the travel Framer Motion's rest thresholds are chosen by, and the factor that puts
        // the channel on the scale Framer's spring measures its rest against.
        public float PassSec;
        public float RestTravel;
        public float Scale = 1f;

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
            // Framer Motion springs a color from 0 to 100 and mixes the endpoints at that percentage.
            Progress = new SpringChannel(0f, 1f) { Scale = 100f };
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
        public VisualElement? NativeLayoutOwner;

        public float Stiffness;
        public float Damping;
        public float Mass;

        // A play samples each channel's spring by the time since it started, as Framer Motion's generator does, until
        // a Retarget hands it to the integrator. A double, since an endless repeat adds to it for as long as the play
        // runs.
        public bool Sampled = true;
        public MotionRepeat Repeat;
        public double ElapsedSec;

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
        // The rest epsilon MotionLayoutIdDriver's fading layout spring settles by on an opacity's 0..1 scale.
        internal const float NormalizedRestDelta = 0.001f;

        /// <summary>
        /// Builds the running state from a resolved plan, or null when the plan animates nothing (the caller
        /// should treat this exactly like a zero-duration tween: land the classes and complete immediately).
        /// </summary>
        public static MotionSpringState? Create(MotionSpringClassParser.SpringPlan plan, float stiffness, float damping,
            float mass, MotionRepeat repeat = default)
        {
            if (plan.IsEmpty)
            {
                return null;
            }
            var state = new MotionSpringState { Stiffness = stiffness, Damping = damping, Mass = mass, Repeat = repeat };
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
            // Framer Motion runs each value as an animation of its own, so each channel's pass lasts as long as its
            // own spring takes to rest.
            ForEachActiveChannel(state, c =>
            {
                c.RestTravel = (c.Target - c.RestingTarget) * c.Scale;
                c.PassSec = PassDurationSec(c.RestTravel, stiffness, damping, mass);
            });
            return state;
        }

        // Framer Motion's calcGeneratorDuration over its spring generator: the first 50 ms sample at which a spring
        // released `delta` from its target, at rest, has come to rest, or infinity when none before 20 s does.
        internal static float PassDurationSec(float delta, float stiffness, float damping, float mass)
        {
            for (var ms = 0; ms < MaxPassMs; ms += PassSampleMs)
            {
                var (displacement, velocity) = SpringIntegrator.Solve(-delta, 0.0, ms / 1000.0,
                    (stiffness, damping, mass));
                if (Rests(delta, displacement, velocity))
                {
                    return ms / 1000f;
                }
            }
            return float.PositiveInfinity;
        }

        // The time from a play's start to the end of its slowest channel's last pass, which is when Framer Motion's
        // animation of every value has finished.
        internal static double SpanSec(MotionSpringClassParser.SpringPlan plan, float stiffness, float damping,
            float mass, MotionRepeat repeat)
        {
            var state = Create(plan, stiffness, damping, mass);
            if (state == null)
            {
                return 0.0;
            }
            var span = 0.0;
            ForEachActiveChannel(state, c => span = System.Math.Max(span, repeat.TotalSec(
                PassDurationSec((c.Target - c.RestingTarget) * c.Scale, stiffness, damping, mass))));
            return span;
        }

        private const int PassSampleMs = 50;
        private const int MaxPassMs = 20000;

        // Framer Motion's rest test, with its thresholds for a travel under 5 and for any other.
        private static bool Rests(double delta, double displacement, double velocity)
        {
            var granular = System.Math.Abs(delta) < 5.0;
            // MUTANT_SURVIVES(equivalent, boundary): `<` differs only on a sample landing exactly on a threshold, and
            // no sample of the springs the cases time lands on one.
            return System.Math.Abs(velocity) <= (granular ? 0.01 : 2.0)
                && System.Math.Abs(displacement) <= (granular ? 0.005 : 0.5);
        }

        /// <summary>
        /// Writes each channel's CURRENT integrator value as an inline style, synchronously — so the element
        /// shows the from-pose on the very first rendered frame instead of flashing at the (already-applied)
        /// resting classes' value until the first tick runs — after suspending the element's native transitions
        /// when this play drives a property one of them could intercept (see
        /// <see cref="MotionNativeTransitionGuard"/>).
        /// </summary>
        /// <remarks>
        /// The suspension goes up with this FIRST write rather than with the recurring tick, because this write
        /// is itself one a live native transition would animate into instead of landing on. A play parked behind
        /// a delay is already holding its from-pose over that whole window, so there is no earlier moment the
        /// driver does not own the slots.
        /// </remarks>
        public static void ApplyCurrentValues(VisualElement element, MotionSpringState state)
        {
            MotionNativeTransitionGuard.SuspendIfIntercepted(element, state, DrivenSlots(state),
                MotionNativeTransitionGuard.LengthLonghands(element, element, state.Lengths, static channel => channel.Property));
            StyleAnimateDriver.HoldAgainstLoop(element, state, DrivenSlots(state));
            WriteChannelValues(element, state);
        }

        private static void SyncLayoutOwner(VisualElement element, MotionSpringState state)
            => state.NativeLayoutOwner = MotionNativeTransitionGuard.SyncLayoutOwner(element, state,
                state.NativeLayoutOwner, state.Lengths, static channel => channel.Property, DrivenSlots(state));

        // The slot groups this play writes, for the guard's intersection against what the element's own
        // transition utilities declare.
        private static MotionTransitionSlots DrivenSlots(MotionSpringState state)
        {
            var slots = MotionTransitionSlots.None;
            if (state.Opacity != null) slots |= MotionTransitionSlots.Opacity;
            if (state.TranslateX != null || state.TranslateY != null) slots |= MotionTransitionSlots.Translate;
            if (state.Scale != null) slots |= MotionTransitionSlots.Scale;
            if (state.Rotate != null) slots |= MotionTransitionSlots.Rotate;
            if (state.Colors != null) slots |= MotionTransitionSlots.Color;
            if (state.Lengths != null) slots |= MotionTransitionSlots.Length;
            return slots;
        }

        private static void WriteChannelValues(VisualElement element, MotionSpringState state)
        {
            SyncLayoutOwner(element, state);
            if (state.Opacity != null)
            {
                MotionOpacity.Write(element, state.Opacity.Integrator.Value);
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
                    // The integrator already holds the spring's true position, overshoot included; the emitter
                    // is what saturates a property with no negative meaning.
                    var v = MotionPropertyInterpolation.ClampLength(l.Property, l.Value.Integrator.Value);
                    StyleArbitraryValueResolver.ApplyInline(element, new ArbitraryStyle(l.Property, v, l.Unit));
                }
            }
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
        }

        /// <summary>
        /// Steps every active channel by <paramref name="dtSec"/> and re-applies the inline styles. Returns true
        /// once EVERY channel has settled at its (possibly retargeted) target.
        /// </summary>
        public static bool Step(VisualElement element, MotionSpringState state, float dtSec)
        {
            if (state.Sampled)
            {
                state.ElapsedSec += dtSec;
            }
            var settled = true;
            if (state.Opacity != null) settled &= StepChannel(state, state.Opacity, dtSec);
            if (state.TranslateX != null) settled &= StepChannel(state, state.TranslateX, dtSec);
            if (state.TranslateY != null) settled &= StepChannel(state, state.TranslateY, dtSec);
            if (state.Scale != null) settled &= StepChannel(state, state.Scale, dtSec);
            if (state.Rotate != null) settled &= StepChannel(state, state.Rotate, dtSec);
            if (state.Colors != null)
            {
                foreach (var color in state.Colors) settled &= StepChannel(state, color.Progress, dtSec);
            }
            if (state.Lengths != null)
            {
                foreach (var length in state.Lengths) settled &= StepChannel(state, length.Value, dtSec);
            }

            // Every channel's Integrator.Value was just advanced above; re-applying them is exactly what the
            // initial (pre-tick) write already does, so the style writes live in exactly one place instead of
            // being duplicated here.
            WriteChannelValues(element, state);
            return settled;
        }

        private static bool StepChannel(MotionSpringState state, SpringChannel channel, float dtSec)
            => state.Sampled ? SampleRepeating(state, channel, dtSec) : StepIntegrated(state, channel, dtSec);

        // A channel a Retarget handed to the integrator rests where Framer Motion's spring would, by the thresholds
        // its travel from the retarget chooses.
        private static bool StepIntegrated(MotionSpringState state, SpringChannel channel, float dtSec)
        {
            channel.Integrator.Step(dtSec, channel.Target, state.Stiffness, state.Damping, state.Mass);
            return Rests(channel.RestTravel, (channel.Integrator.Value - channel.Target) * channel.Scale,
                channel.Integrator.Velocity * channel.Scale);
        }

        // Framer Motion's main-thread animation over a spring, repeating or not: each pass samples the channel's
        // spring at the time MotionRepeat gives, a mirrored pass a spring from the to-value back to the from-value,
        // and a channel past its last pass holds the value MotionRepeat.EndsAtFrom names, so a play that does not
        // repeat ends when its pass does, as its play span says. Every channel reaching that end is the play's
        // settle. The integrator takes each sample, with the velocity since the last, so a Retarget carries on from
        // it; the scheduler's tick and pre-roll step by a positive time, which that velocity divides by.
        private static bool SampleRepeating(MotionSpringState state, SpringChannel channel, float dtSec)
        {
            var previous = channel.Integrator.Value;
            var ended = state.ElapsedSec >= state.Repeat.TotalSec(channel.PassSec);
            float value;
            if (ended)
            {
                value = state.Repeat.EndsAtFrom ? channel.RestingTarget : channel.Target;
            }
            else
            {
                // A spring that does not rest within 20 s has no pass to repeat, so it plays its first pass on.
                var mirrored = false;
                var passTime = float.IsPositiveInfinity(channel.PassSec)
                    ? state.ElapsedSec
                    : state.Repeat.PassTime(state.ElapsedSec, channel.PassSec, out mirrored);
                value = mirrored
                    ? SampleSpring(channel.Target, channel.RestingTarget, channel.Scale, passTime, state)
                    : SampleSpring(channel.RestingTarget, channel.Target, channel.Scale, passTime, state);
            }
            channel.Integrator.Set(value, (value - previous) / dtSec);
            return ended;
        }

        // Framer Motion's spring generator released at `from` toward `to`, at rest: the target once it rests.
        private static float SampleSpring(float from, float to, float scale, double t, MotionSpringState state)
        {
            var delta = (to - from) * scale;
            var (displacement, velocity) = SpringIntegrator.Solve(-delta, 0.0, t,
                (state.Stiffness, state.Damping, state.Mass));
            return Rests(delta, displacement, velocity) ? to : (float)(to + displacement / scale);
        }

        // Whether the play ends on its from-values rather than its to-values (MotionRepeat.EndsAtFrom).
        public static bool EndsAtFrom(MotionSpringState state) => state.Repeat.EndsAtFrom;

        /// <summary>
        /// Releases every inline slot this state ever wrote — and the transition suspension
        /// <see cref="ApplyCurrentValues"/> put in place — letting the (already-resting) classes take back over.
        /// </summary>
        /// <remarks>
        /// The slots are nulled first and the element's surviving arbitrary-value layers re-asserted afterwards,
        /// in that order: a driven shorthand owns a whole fan-out (a `padding` channel writes all four edges)
        /// while an authored longhand (`pt-[2px]`) is registered against ONE of them, so per-property
        /// interleaving would let a later null wipe a value an earlier re-assert had just restored.
        /// </remarks>
        public static void ClearInlineOverrides(VisualElement element, MotionSpringState state)
        {
            if (state.Opacity != null) MotionOpacity.Write(element, StyleKeyword.Null);
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
            StyleArbitraryValueResolver.ReapplyLayeredValues(element);
            StyleAnimateDriver.HoldAgainstLoop(element, state, MotionTransitionSlots.None);
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
            MotionNativeTransitionGuard.Release(element, state);
            if (state.NativeLayoutOwner != null) MotionNativeTransitionGuard.Release(state.NativeLayoutOwner, state);
            state.NativeLayoutOwner = null;
        }

        /// <summary>
        /// Stops driving every channel that writes one of <paramref name="named"/> and hands its inline slot back to
        /// the element's variant layers, as <see cref="ClearInlineOverrides"/> does, leaving the rest running.
        /// </summary>
        public static void ReleaseChannels(VisualElement element, MotionSpringState state, StyleLonghandSet named)
        {
            if (state.Opacity != null && named.Contains(StyleLonghand.Opacity))
            {
                state.Opacity = null;
                MotionOpacity.Write(element, StyleKeyword.Null);
            }
            // X and Y are created together, so they are dropped together.
            if (state.TranslateX != null && named.Contains(StyleLonghand.Translate))
            {
                state.TranslateX = null;
                state.TranslateY = null;
                element.style.translate = StyleKeyword.Null;
            }
            if (state.Scale != null && named.Contains(StyleLonghand.Scale))
            {
                state.Scale = null;
                element.style.scale = StyleKeyword.Null;
            }
            if (state.Rotate != null && named.Contains(StyleLonghand.Rotate))
            {
                state.Rotate = null;
                element.style.rotate = StyleKeyword.Null;
            }
            state.Colors?.RemoveAll(c => ReleasesProperty(element, c.Property, named));
            state.Lengths?.RemoveAll(l => ReleasesProperty(element, l.Property, named));
            StyleArbitraryValueResolver.ReapplyLayeredValues(element, named);
            StyleAnimateDriver.HoldAgainstLoop(element, state, DrivenSlots(state));
            StyleAnimateDriver.ReassertLoop(element, DrivenSlots(state));
            SyncLayoutOwner(element, state);
        }

        internal static bool ReleasesProperty(VisualElement element, ArbitraryProperty property, StyleLonghandSet named)
        {
            if (!StyleArbitraryLonghands.Of(property).Overlaps(named))
            {
                return false;
            }
            StyleArbitraryValueResolver.ClearInline(element, property);
            return true;
        }

        /// <summary>
        /// Re-targets every active channel toward the value it STARTED from (see
        /// <see cref="SpringChannel.RestingTarget"/>) — the exit-cancel hand-off. Each channel's
        /// <see cref="SpringIntegrator"/> instance is untouched, so its current value/velocity carry over
        /// unbroken; only the goal it steps toward next changes. The reversal is integrated from there, whatever the
        /// play repeated.
        /// </summary>
        public static void Retarget(MotionSpringState state)
        {
            ForEachActiveChannel(state, static c =>
            {
                c.RestTravel = (c.RestingTarget - c.Integrator.Value) * c.Scale;
                c.Target = c.RestingTarget;
            });
            state.Sampled = false;
            state.Repeat = default;
        }

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
