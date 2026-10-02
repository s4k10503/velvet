using System;

namespace Velvet
{
    /// <summary>
    /// The spring a duration and a bounce describe: the stiffness and damping Framer Motion's <c>findSpring</c>
    /// solves for, and the duration it settles at.
    /// </summary>
    internal readonly struct DurationSpring
    {
        private const double DefaultDurationSec = 0.8;
        private const double DefaultBounce = 0.3;
        private const double MinDurationSec = 0.01;
        private const double MaxDurationSec = 10.0;
        private const double MinDampingRatio = 0.05;
        private const double MaxDampingRatio = 1.0;
        private const double SafeMin = 0.001;
        private const int RootIterations = 12;
        private const float DefaultStiffness = 100f;
        private const float DefaultDamping = 10f;

        public float Stiffness { get; }
        public float Damping { get; }
        // When a play on this spring ends, measured from the moment it starts moving: Framer ends one at its
        // duration whatever its physics, landing it on the target. Null for a NaN duration, which leaves the
        // default physics to settle the play.
        public double? SettleSec { get; }

        private DurationSpring(float stiffness, float damping, double? settleSec)
        {
            Stiffness = stiffness;
            Damping = damping;
            SettleSec = settleSec;
        }

        public static DurationSpring Derive(float? durationSec, float? bounce)
        {
            var dampingRatio = Clamp(1.0 - (bounce.HasValue ? Widen(bounce.Value) : DefaultBounce),
                MinDampingRatio, MaxDampingRatio);
            var duration = Clamp(durationSec.HasValue ? Widen(durationSec.Value) : DefaultDurationSec,
                MinDurationSec, MaxDurationSec);
            var undampedFreq = ApproximateRoot(dampingRatio, duration, 5.0 / duration);
            if (double.IsNaN(undampedFreq))
            {
                return new DurationSpring(DefaultStiffness, DefaultDamping, double.IsNaN(duration) ? null : duration);
            }
            var stiffness = undampedFreq * undampedFreq;
            return new DurationSpring((float)stiffness, (float)(dampingRatio * 2.0 * Math.Sqrt(stiffness)), duration);
        }

        // A float written as 0.3f carries 0.30000001192…; the decimal conversion rounds it back to the 0.3 the
        // caller wrote, so the double arithmetic below starts from the number Framer's would.
        private static double Widen(float value)
            => float.IsFinite(value) && Math.Abs(value) < 1e9f ? (double)(decimal)value : value;

        // Framer's clamp(min, max, v), through which NaN passes.
        private static double Clamp(double value, double min, double max)
            => value > max ? max : value < min ? min : value;

        // Newton's method from the same initial guess and for the same fixed iteration count as Framer, so a root
        // the iteration does not converge on is the same root.
        private static double ApproximateRoot(double dampingRatio, double duration, double initialGuess)
        {
            var result = initialGuess;
            for (var i = 1; i < RootIterations; i++)
            {
                result -= Envelope(result, dampingRatio, duration) / Derivative(result, dampingRatio, duration);
            }
            return result;
        }

        private static double Envelope(double undampedFreq, double dampingRatio, double duration)
        {
            if (dampingRatio < 1.0)
            {
                var exponentialDecay = undampedFreq * dampingRatio;
                var delta = exponentialDecay * duration;
                return SafeMin - (exponentialDecay / AngularFreq(undampedFreq, dampingRatio) * Math.Exp(-delta));
            }
            return -SafeMin + (Math.Exp(-undampedFreq * duration) * ((undampedFreq * duration) + 1.0));
        }

        private static double Derivative(double undampedFreq, double dampingRatio, double duration)
        {
            if (dampingRatio < 1.0)
            {
                var exponentialDecay = undampedFreq * dampingRatio;
                var delta = exponentialDecay * duration;
                var e = dampingRatio * dampingRatio * undampedFreq * undampedFreq * duration;
                var f = Math.Exp(-delta);
                var g = AngularFreq(undampedFreq * undampedFreq, dampingRatio);
                var factor = -Envelope(undampedFreq, dampingRatio, duration) + SafeMin > 0.0 ? -1.0 : 1.0;
                return factor * (-e * f) / g;
            }
            return Math.Exp(-undampedFreq * duration) * (-undampedFreq * duration * duration);
        }

        private static double AngularFreq(double undampedFreq, double dampingRatio)
            => undampedFreq * Math.Sqrt(1.0 - (dampingRatio * dampingRatio));
    }
}
