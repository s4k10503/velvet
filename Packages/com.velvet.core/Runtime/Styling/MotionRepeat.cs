using System;

namespace Velvet
{
    /// <summary>
    /// The repeat timing of a play whose single pass runs a generator — a bezier curve, or a spring — from its
    /// from-values to its to-values: Framer Motion's <c>repeat</c> / <c>repeatType</c> / <c>repeatDelay</c>,
    /// ported from the tick of its main-thread animation and from its <c>getFinalKeyframe</c>. See
    /// <see cref="StyleTransitionConfig.Repeat"/> for the public contract.
    /// </summary>
    internal readonly struct MotionRepeat
    {
        /// <summary>The passes after the first; <c>float.PositiveInfinity</c> repeats without end.</summary>
        public readonly float Count;

        public readonly TransitionRepeatType Type;

        /// <summary>The wait after each pass but the last, holding the value the pass ended on.</summary>
        public readonly float DelaySec;

        public MotionRepeat(float count, TransitionRepeatType type, float delaySec)
        {
            Count = count;
            Type = type;
            DelaySec = delaySec;
        }

        public static MotionRepeat Of(StyleTransitionConfig config)
            => new(config.Repeat, config.RepeatType, config.RepeatDelaySec);

        public bool IsEndless => float.IsPositiveInfinity(Count);

        /// <summary>
        /// Framer's <c>getFinalKeyframe</c>: an odd number of <see cref="TransitionRepeatType.Reverse"/> or
        /// <see cref="TransitionRepeatType.Mirror"/> repeats ends on the from-values.
        /// </summary>
        public bool EndsAtFrom => Type != TransitionRepeatType.Loop && Count % 2f == 1f;

        /// <summary>
        /// Framer's <c>totalDuration</c>: every pass, and a delay between each two of them. Infinite for an endless
        /// repeat whose pass or delay takes any time; a pass and a delay of no time end at once.
        /// </summary>
        public double TotalSec(float passSec)
        {
            var resolved = (double)passSec + DelaySec;
            return resolved == 0.0 ? 0.0 : resolved * ((double)Count + 1.0) - DelaySec;
        }

        /// <summary>
        /// The time into its generator a pass shows <paramref name="elapsedSec"/> after the play started, for a time
        /// short of <see cref="TotalSec"/>: Framer's <c>elapsed</c>, which runs past <paramref name="passSec"/>
        /// through a delay between passes. <paramref name="mirrored"/> says the pass runs its generator from the
        /// to-values back to the from-values.
        /// </summary>
        public double PassTime(double elapsedSec, float passSec, out bool mirrored)
        {
            mirrored = false;
            var resolved = (double)passSec + DelaySec;
            var progress = elapsedSec / resolved;
            var iteration = Math.Floor(progress);
            var iterationProgress = progress % 1.0;
            // A time on a pass boundary belongs to the pass it ends, not to the one it starts.
            if (iterationProgress == 0.0 && progress >= 1.0)
            {
                iterationProgress = 1.0;
                iteration--;
            }
            if (iteration % 2.0 == 1.0)
            {
                if (Type == TransitionRepeatType.Reverse)
                {
                    iterationProgress = 1.0 - iterationProgress - DelaySec / resolved;
                }
                else if (Type == TransitionRepeatType.Mirror)
                {
                    mirrored = true;
                }
            }
            // Framer clamps the iteration progress to [0, 1]; it never exceeds 1, so only the floor can bind.
            return Math.Max(0.0, iterationProgress) * resolved;
        }

        /// <summary>
        /// Takes whole cycles of two passes and their delays off an endless repeat's elapsed time, which shows the
        /// same frame, so a float clock under a play that never ends stays within one cycle however long it runs. A
        /// time on a whole number of cycles keeps one cycle, the end of a second pass, so the time has to be past the
        /// start.
        /// </summary>
        public float Fold(float elapsedSec, float passSec)
        {
            if (!IsEndless)
            {
                return elapsedSec;
            }
            var cycle = 2.0 * ((double)passSec + DelaySec);
            var folded = elapsedSec % cycle;
            return (float)(folded == 0.0 ? cycle : folded);
        }
    }
}
