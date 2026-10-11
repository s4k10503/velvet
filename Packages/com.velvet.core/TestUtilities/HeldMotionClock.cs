namespace Velvet.TestUtilities
{
    /// <summary>
    /// A <see cref="MotionClock"/> that reads only what a test writes into <see cref="Now"/>, so it holds still
    /// while the panel ticks and moves exactly as far as the test steps it.
    /// </summary>
    public sealed class HeldMotionClock : MotionClock
    {
        /// <summary>A power of two, so a sum of steps and the float a driver steps by agree exactly.</summary>
        public const double FrameSec = 1.0 / 64.0;

        /// <summary>The reading, in seconds.</summary>
        public double Now;

        /// <summary>An integral start keeps every sum of <see cref="FrameSec"/> steps exact.</summary>
        public HeldMotionClock(double start = 100.0) => Now = start;

        /// <inheritdoc />
        public override double NowSec => Now;
    }
}
