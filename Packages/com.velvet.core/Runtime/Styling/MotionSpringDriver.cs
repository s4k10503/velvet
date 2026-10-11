using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One spring-animated channel: the spring Framer Motion would run for it — the value it was released at, the
    /// velocity it was released with and the value it heads toward — the value it last showed, and the value it
    /// rested at before the play (<see cref="RestingTarget"/>, which an exit-cancel hand-off heads back to).
    /// </summary>
    internal sealed class SpringChannel
    {
        // Deliberately NOT readonly: SpringIntegrator is a mutable struct embedded inline (see its own doc), and
        // every Set call mutates it in place through this field. Marking the field readonly would make the
        // compiler silently invoke Set on a defensive COPY instead — the channel would only ever report its
        // initial value.
        public SpringIntegrator Integrator;
        public float Target;
        public readonly float RestingTarget;
        // The spring's start, in the channel's own units, and its start velocity on the scale below.
        public float Origin;
        public float StartVelocity;
        // The length of one pass, and the factor that puts the channel on the scale Framer's spring measures its rest
        // against.
        public float PassSec;
        public float Scale = 1f;
        // A color's progress, which Framer Motion springs over a fixed travel of 100 with no velocity of its own,
        // an interrupted color's included, since Framer mixes from the color it was showing.
        public bool Normalized;
        // A value Framer Motion animates on the browser's own animation where the repeat allows it: an opacity or a
        // background color. Framer's x, y, scale and rotate are values of their own, which it keeps on its main
        // thread.
        public bool Acceleratable;

        public SpringChannel(float initialValue, float target)
        {
            Integrator = new SpringIntegrator(initialValue);
            Target = target;
            RestingTarget = initialValue;
            Origin = initialValue;
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
            Progress = new SpringChannel(0f, 1f)
            {
                Normalized = true, Acceleratable = property == ArbitraryProperty.BackgroundColor,
            };
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

        // A play samples each channel's spring by the time since it started, as Framer Motion's generator does. A
        // double, since an endless repeat adds to it for as long as the play runs.
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
            if (plan.Opacity is { } o) state.Opacity = new SpringChannel(o.from, o.to) { Acceleratable = true };
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
            ForEachActiveChannel(state, c => Release(state, c, c.Origin, 0f));
            return state;
        }

        // Releases the channel's spring at `origin` with `velocity`, in the channel's own units per second, toward its
        // Target, as Framer Motion starts a value's animation. Framer runs each value as an animation of its own, so
        // each channel's pass lasts as long as its own spring takes to rest. One it can hand to the browser — an
        // acceleratable value whose repeat neither mirrors nor waits — becomes an easing over a travel of 100 cut at
        // 20 s, onto which the value's velocity is carried unscaled; any other is measured over its own travel, a
        // color's progress over 100.
        private static void Release(MotionSpringState state, SpringChannel c, float origin, float velocity)
        {
            c.Origin = origin;
            var travel = c.Target - origin;
            var accelerated = c.Acceleratable && state.Repeat.Type != TransitionRepeatType.Mirror
                && state.Repeat.DelaySec == 0f;
            c.Scale = (c.Normalized || accelerated) && travel != 0f ? 100f / System.Math.Abs(travel) : 1f;
            c.StartVelocity = c.Normalized ? 0f : accelerated ? System.Math.Sign(travel) * velocity : velocity * c.Scale;
            var pass = PassDurationSec(travel * c.Scale, state.Stiffness, state.Damping, state.Mass, c.StartVelocity);
            c.PassSec = accelerated ? System.Math.Min(pass, MaxPassMs / 1000f) : pass;
        }

        // Framer Motion's calcGeneratorDuration over its spring generator: the first 50 ms sample at which a spring
        // released `delta` from its target with `velocity` has come to rest, or infinity when none before 20 s does.
        internal static float PassDurationSec(float delta, float stiffness, float damping, float mass, float velocity = 0f)
        {
            for (var ms = 0; ms < MaxPassMs; ms += PassSampleMs)
            {
                var (displacement, speed) = SpringIntegrator.Solve(-delta, velocity, ms / 1000.0,
                    (stiffness, damping, mass));
                if (Rests(delta, displacement, speed))
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
            var state = Create(plan, stiffness, damping, mass, repeat);
            if (state == null)
            {
                return 0.0;
            }
            var span = 0.0;
            ForEachActiveChannel(state, c => span = System.Math.Max(span, repeat.TotalSec(c.PassSec)));
            return span;
        }

        private const int PassSampleMs = 50;
        private const int MaxPassMs = 20000;

        // Framer Motion's rest test, with its thresholds for a travel under 5 and for any other.
        private static bool Rests(double delta, double displacement, double velocity)
        {
            var granular = System.Math.Abs(delta) < 5.0;
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
            state.ElapsedSec += dtSec;
            return SampleAll(element, state, dtSec, seekScale: null);
        }

        // dtSec is the time since the channels were last sampled, which a stepped play's velocity divides by.
        // seekScale is set for a play sampled at a time: its velocity is then the curve's slope at that time,
        // times seekScale.
        private static bool SampleAll(VisualElement element, MotionSpringState state, float dtSec, float? seekScale)
        {
            var settled = true;
            if (state.Opacity != null) settled &= SampleRepeating(state, state.Opacity, dtSec, seekScale);
            if (state.TranslateX != null) settled &= SampleRepeating(state, state.TranslateX, dtSec, seekScale);
            if (state.TranslateY != null) settled &= SampleRepeating(state, state.TranslateY, dtSec, seekScale);
            if (state.Scale != null) settled &= SampleRepeating(state, state.Scale, dtSec, seekScale);
            if (state.Rotate != null) settled &= SampleRepeating(state, state.Rotate, dtSec, seekScale);
            if (state.Colors != null)
            {
                foreach (var color in state.Colors) settled &= SampleRepeating(state, color.Progress, dtSec, seekScale);
            }
            if (state.Lengths != null)
            {
                foreach (var length in state.Lengths) settled &= SampleRepeating(state, length.Value, dtSec, seekScale);
            }

            // Every channel's Integrator.Value was just sampled above; re-applying them is exactly what the initial
            // (pre-tick) write already does, so the style writes live in exactly one place instead of being
            // duplicated here.
            WriteChannelValues(element, state);
            return settled;
        }

        // Framer Motion's main-thread animation over a spring, repeating or not: each pass samples the channel's
        // spring at the time MotionRepeat gives, a mirrored pass a spring from the to-value back to the from-value
        // released with the opposite velocity, and a channel past its last pass holds the value
        // MotionRepeat.EndsAtFrom names, so a play that does not repeat ends when its pass does, as its play span
        // says. Every channel reaching that end is the play's settle. The integrator takes each sample, with the
        // velocity on screen, which is what an interruption releases the next spring with; the scheduler's tick
        // and pre-roll step by a positive time, which that velocity divides by.
        private static bool SampleRepeating(MotionSpringState state, SpringChannel channel, float dtSec, float? seekScale)
        {
            var previous = channel.Integrator.Value;
            var value = ValueAt(state, channel, state.ElapsedSec, out var ended);
            float velocity;
            if (ended)
            {
                // A finished value has stopped, so an interruption after its end releases the next spring at rest.
                velocity = 0f;
            }
            else if (seekScale is { } scale)
            {
                // The slope at the time, in the units of the clock the play is shown on: a step across the seek
                // itself would average the whole jump, and a pause or a rate of zero leaves the value still.
                var step = SeekSlopeStepSec;
                var from = state.ElapsedSec >= step ? state.ElapsedSec - step : state.ElapsedSec;
                var to = state.ElapsedSec >= step ? state.ElapsedSec : state.ElapsedSec + step;
                velocity = (ValueAt(state, channel, to, out _) - ValueAt(state, channel, from, out _))
                    / (float)(to - from) * scale;
            }
            else
            {
                velocity = dtSec > 0f ? (value - previous) / dtSec : 0f;
            }
            channel.Integrator.Set(value, velocity);
            return ended;
        }

        private const double SeekSlopeStepSec = 1.0 / 1000.0;

        private static float ValueAt(MotionSpringState state, SpringChannel channel, double elapsedSec, out bool ended)
        {
            ended = elapsedSec >= state.Repeat.TotalSec(channel.PassSec);
            if (ended)
            {
                return state.Repeat.EndsAtFrom ? channel.Origin : channel.Target;
            }
            // A spring that does not rest within 20 s has no pass to repeat, so it plays its first pass on.
            var mirrored = false;
            var passTime = float.IsPositiveInfinity(channel.PassSec)
                ? elapsedSec
                : state.Repeat.PassTime(elapsedSec, channel.PassSec, out mirrored);
            return mirrored
                ? SampleSpring(channel.Target, channel.Origin, channel.Scale, -channel.StartVelocity, passTime, state)
                : SampleSpring(channel.Origin, channel.Target, channel.Scale, channel.StartVelocity, passTime, state);
        }

        private static float SampleSpring(float from, float to, float scale, float velocity, double t,
            MotionSpringState state)
            => SampleSpring(from, to, scale, velocity, t, (state.Stiffness, state.Damping, state.Mass));

        // Framer Motion's spring generator released at `from` toward `to` with `velocity` on the scaled travel: the
        // target once it rests.
        internal static float SampleSpring(float from, float to, float scale, float velocity, double t,
            (float Stiffness, float Damping, float Mass) spring)
        {
            var delta = (to - from) * scale;
            var (displacement, speed) = SpringIntegrator.Solve(-delta, velocity, t, spring);
            return Rests(delta, displacement, speed) ? to : (float)(to + displacement / scale);
        }

        /// <summary>
        /// Releases each channel of <paramref name="next"/> with the velocity the same channel of
        /// <paramref name="interrupted"/> was moving at, as Framer Motion starts a value's new animation with the
        /// motion value's velocity. A color carries none, as Framer's color values carry none.
        /// </summary>
        public static void InheritVelocity(MotionSpringState next, MotionSpringState interrupted)
        {
            Inherit(next, next.Opacity, interrupted.Opacity);
            Inherit(next, next.TranslateX, interrupted.TranslateX);
            Inherit(next, next.TranslateY, interrupted.TranslateY);
            Inherit(next, next.Scale, interrupted.Scale);
            Inherit(next, next.Rotate, interrupted.Rotate);
            if (next.Lengths != null && interrupted.Lengths != null)
            {
                foreach (var length in next.Lengths)
                {
                    var match = interrupted.Lengths.Find(l => l.Property == length.Property);
                    Inherit(next, length.Value, match?.Value);
                }
            }
        }

        private static void Inherit(MotionSpringState next, SpringChannel? channel, SpringChannel? interrupted)
        {
            if (channel != null && interrupted != null)
            {
                Release(next, channel, channel.Origin, interrupted.Integrator.Velocity);
            }
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

        // Holds translate at (x, y) from its next write on, the start values of a play CancelPlay holds.
        public static void HoldTranslate(MotionSpringState state, float x, float y)
        {
            state.TranslateX = new SpringChannel(x, x);
            state.TranslateY = new SpringChannel(y, y);
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
        /// <see cref="SpringChannel.RestingTarget"/>) — the exit-cancel hand-off — releasing a new spring from the
        /// value and velocity it was last sampled at, as Framer Motion starts an interrupted value's new animation.
        /// The reversal plays once, whatever the play repeated.
        /// </summary>
        public static void Retarget(MotionSpringState state)
        {
            state.Repeat = default;
            state.ElapsedSec = 0.0;
            ForEachActiveChannel(state, c =>
            {
                c.Target = c.RestingTarget;
                Release(state, c, c.Integrator.Value, c.Integrator.Velocity);
            });
        }

        /// <summary>
        /// Puts the play at <paramref name="timeSec"/> into its run, earlier or later than where it stands, writes
        /// every channel there, and reports whether that is settled — Framer Motion's spring generator sampled at a
        /// time. A play on a <c>MotionPlayback</c> is driven by this. The velocity the channels keep is the curve's
        /// slope there times <paramref name="timeScale"/>, the playback's seconds per second on screen.
        /// </summary>
        public static bool SeekTo(VisualElement element, MotionSpringState state, float timeSec, float timeScale = 1f)
        {
            state.ElapsedSec = Mathf.Max(0f, timeSec);
            return SampleAll(element, state, 0f, timeScale);
        }

        /// <summary>
        /// Leaves every channel still: what a play on a paused or stopped <c>MotionPlayback</c> shows, so an
        /// interruption releases the next spring at rest.
        /// </summary>
        public static void Halt(MotionSpringState state)
        {
            HaltChannel(state.Opacity);
            HaltChannel(state.TranslateX);
            HaltChannel(state.TranslateY);
            HaltChannel(state.Scale);
            HaltChannel(state.Rotate);
            if (state.Colors != null)
            {
                foreach (var c in state.Colors) HaltChannel(c.Progress);
            }
            if (state.Lengths != null)
            {
                foreach (var l in state.Lengths) HaltChannel(l.Value);
            }
        }

        private static void HaltChannel(SpringChannel? channel)
        {
            if (channel != null)
            {
                channel.Integrator.Set(channel.Integrator.Value, 0f);
            }
        }

        /// <summary>
        /// The values a play started from on each channel, as a plan of from-to pairs that both hold them: what
        /// <c>StyleAnimationScheduler</c> starts the next play on a held element from.
        /// </summary>
        public static MotionSpringClassParser.SpringPlan StartValues(MotionSpringState state)
            => MotionSpringClassParser.Holding(
                (state.Opacity?.RestingTarget, state.TranslateX?.RestingTarget, state.TranslateY?.RestingTarget,
                    state.Scale?.RestingTarget, state.Rotate?.RestingTarget),
                state.Colors, static c => new MotionSpringClassParser.ColorChannelPlan(c.Property, c.From, c.From),
                state.Lengths, static l => new MotionSpringClassParser.LengthChannelPlan(l.Property,
                    l.Value.RestingTarget, l.Value.RestingTarget, l.Unit));

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
