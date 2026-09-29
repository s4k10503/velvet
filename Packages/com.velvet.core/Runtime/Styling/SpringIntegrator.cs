using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// A single damped-harmonic-oscillator spring, advanced one step at a time by the oscillator's closed-form
    /// solution — the one Framer Motion's spring evaluates — so the step size never decides whether it
    /// converges. Pure math: holds
    /// only <see cref="Value"/> / <see cref="Velocity"/>, takes every other input (target, stiffness, damping,
    /// mass) per <see cref="Step"/> call, and has no dependency on a <c>VisualElement</c> or panel — see
    /// <see cref="MotionSpringDriver"/> for the piece that applies a per-channel instance of this to a Motion's
    /// animated style properties.
    /// </summary>
    /// <remarks>
    /// Retargeting mid-flight (calling <see cref="Step"/> with a different <c>target</c> than the previous
    /// call) is intentionally just a normal call: <see cref="Value"/> / <see cref="Velocity"/> are never reset
    /// between calls, so the SAME instance keeps its current value and velocity — the physical continuity an
    /// interrupted spring needs (e.g. a cancelled exit transition hands off to a reversal spring built from
    /// wherever the exit currently was, not from a fresh rest state).
    /// <para>
    /// A mutable struct, not a class: <see cref="SpringChannel"/> embeds one inline (as a plain, non-readonly
    /// field — see its own doc) instead of holding a separate heap reference, so a spring channel costs one
    /// allocation instead of two. <see cref="Step"/> mutates <see cref="Value"/>/<see cref="Velocity"/> in
    /// place, so every caller must reach an instance through an addressable field/variable (never through a
    /// property or a <c>readonly</c> field of this type) or the mutation lands on a silent defensive copy
    /// instead of the real one.
    /// </para>
    /// </remarks>
    internal struct SpringIntegrator
    {
        // A single large hitch (a dropped frame, a GC pause, the editor regaining focus) must not make the
        // spring "jump" most of its way at once: capping dt means a hitch looks like a few frames of slightly
        // slower motion instead.
        private const float MaxDtSec = 1f / 30f;

        /// <summary>The spring's current value.</summary>
        public float Value { get; private set; }

        /// <summary>The spring's current velocity (units of <see cref="Value"/> per second).</summary>
        public float Velocity { get; private set; }

        /// <summary>
        /// True when all three parameters are finite and positive — the springs a play drives. Anything else
        /// completes immediately instead of ticking (see <c>StyleAnimationScheduler</c>'s validation).
        /// </summary>
        public static bool AreValidParameters(float stiffness, float damping, float mass)
            => float.IsFinite(stiffness) && stiffness > 0f
                && float.IsFinite(damping) && damping > 0f
                && float.IsFinite(mass) && mass > 0f;

        public SpringIntegrator(float initialValue, float initialVelocity = 0f)
        {
            Value = initialValue;
            Velocity = initialVelocity;
        }

        /// <summary>
        /// Advances the spring by one tick toward <paramref name="target"/>. <paramref name="dtSec"/> is clamped
        /// to <see cref="MaxDtSec"/> (a no-op or negative dt does nothing).
        /// </summary>
        public void Step(float dtSec, float target, float stiffness, float damping, float mass)
        {
            if (dtSec <= 0f)
            {
                return;
            }
            var (displacement, velocity) = Solve(Value - target, Velocity, Mathf.Min(dtSec, MaxDtSec),
                (stiffness, damping, mass));
            Value = (float)(target + displacement);
            Velocity = (float)velocity;
        }

        /// <summary>
        /// Where a spring released at <paramref name="displacement0"/> from its target with
        /// <paramref name="velocity0"/> is <paramref name="t"/> seconds later. The three regimes and the cap on
        /// the hyperbolic argument are Framer Motion's <c>spring</c> generator's, so a caller sampling it
        /// reproduces Framer's numbers.
        /// </summary>
        public static (double Displacement, double Velocity) Solve(double displacement0, double velocity0, double t,
            (double Stiffness, double Damping, double Mass) spring)
        {
            var (stiffness, damping, mass) = spring;
            var undamped = Math.Sqrt(stiffness / mass);
            var ratio = damping / (2.0 * Math.Sqrt(stiffness * mass));
            var decay = ratio * undamped;
            var envelope = Math.Exp(-decay * t);
            // Both non-critical regimes share one form, with sin/cos for an underdamped spring and sinh/cosh
            // for an overdamped one.
            var coefficient = velocity0 + (decay * displacement0);
            var pull = (decay * velocity0) + (undamped * undamped * displacement0);
            if (ratio < 1.0)
            {
                var frequency = undamped * Math.Sqrt(1.0 - (ratio * ratio));
                var cos = Math.Cos(frequency * t);
                var sin = Math.Sin(frequency * t);
                return (envelope * ((displacement0 * cos) + (coefficient / frequency * sin)),
                    envelope * ((velocity0 * cos) - (pull / frequency * sin)));
            }
            if (ratio == 1.0)
            {
                return (envelope * (displacement0 + (coefficient * t)),
                    envelope * (velocity0 - (undamped * coefficient * t)));
            }
            var hyperbolic = undamped * Math.Sqrt((ratio * ratio) - 1.0);
            // Framer's cap. Past it the envelope keeps falling while cosh stops growing, so a heavily overdamped
            // spring collapses onto its target there — released 100 away, (100, 125, 1) is 2.0 off at 4.85s and
            // 0.19 at 4.9s — and a duration sampled from this matches Framer's only while the cap is kept.
            var angle = Math.Min(hyperbolic * t, 300.0);
            var cosh = Math.Cosh(angle);
            var sinh = Math.Sinh(angle);
            return (envelope * ((displacement0 * cosh) + (coefficient / hyperbolic * sinh)),
                envelope * ((velocity0 * cosh) - (pull / hyperbolic * sinh)));
        }

        /// <summary>
        /// True once the spring is close enough to <paramref name="target"/>, in both position and speed, to
        /// treat as settled. The default epsilons (0.01) suit a roughly 0..1-scale value (opacity, a uniform
        /// scale factor); a caller animating a pixel or degree-scale channel should pass a larger,
        /// scale-appropriate pair (see <see cref="MotionSpringDriver"/>'s per-channel epsilons).
        /// </summary>
        public bool IsSettled(float target, float restDelta = 0.01f, float restSpeed = 0.01f)
        {
            return Mathf.Abs(Value - target) <= restDelta && Mathf.Abs(Velocity) <= restSpeed;
        }
    }
}
