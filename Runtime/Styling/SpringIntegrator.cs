using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// A single damped-harmonic-oscillator spring, integrated one step at a time via semi-implicit Euler
    /// (velocity is updated from the current acceleration FIRST, then position is advanced using the
    /// already-updated velocity — unconditionally stable for the stiffness/damping ranges a UI transition
    /// uses, unlike explicit Euler). Pure math: holds only <see cref="Value"/> / <see cref="Velocity"/>, takes
    /// every other input (target, stiffness, damping, mass) per <see cref="Step"/> call, and has no dependency
    /// on a <c>VisualElement</c> or panel — see <see cref="MotionSpringDriver"/> for the piece that applies a
    /// per-channel instance of this to a Motion's animated style properties.
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
        // spring "jump": integrating a huge dt in one step can overshoot wildly or numerically blow up. Capping
        // dt means a hitch just looks like a few frames of slightly slower motion instead.
        private const float MaxDtSec = 1f / 30f;

        // A non-positive mass would divide by zero (or flip the sign of the restoring force); clamp to a tiny
        // positive epsilon so a misconfigured Mass degrades to "very heavy" instead of producing NaN/Infinity.
        private const float MinMass = 1e-4f;

        /// <summary>The spring's current value.</summary>
        public float Value { get; private set; }

        /// <summary>The spring's current velocity (units of <see cref="Value"/> per second).</summary>
        public float Velocity { get; private set; }

        public SpringIntegrator(float initialValue, float initialVelocity = 0f)
        {
            Value = initialValue;
            Velocity = initialVelocity;
        }

        /// <summary>
        /// Advances the spring by one tick toward <paramref name="target"/>. <paramref name="dtSec"/> is clamped
        /// to <see cref="MaxDtSec"/> (a no-op or negative dt does nothing). Semi-implicit Euler:
        /// <c>velocity += ((-stiffness*(value-target) - damping*velocity) / mass) * dt; value += velocity * dt;</c>
        /// </summary>
        public void Step(float dtSec, float target, float stiffness, float damping, float mass)
        {
            if (dtSec <= 0f)
            {
                return;
            }
            var dt = Mathf.Min(dtSec, MaxDtSec);
            var safeMass = Mathf.Max(mass, MinMass);
            var acceleration = (-stiffness * (Value - target) - (damping * Velocity)) / safeMass;
            Velocity += acceleration * dt;
            Value += Velocity * dt;
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
