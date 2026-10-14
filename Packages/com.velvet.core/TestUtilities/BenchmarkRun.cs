using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// How many iterations a <c>Unity.PerformanceTesting</c> benchmark takes. A benchmark asserts nothing
    /// about its numbers, so the default is one measured iteration with no warmup, which still throws
    /// wherever a full run would. Passing <see cref="FullFlag"/> to the editor restores the counts each
    /// benchmark declares; <c>CONTRIBUTING.md</c> says which workflow passes it.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public static class BenchmarkRun
    {
        public const string FullFlag = "-velvetBenchmarkFull";

        private static readonly bool s_full = IsFull(Environment.GetCommandLineArgs());

        public static bool IsFull(string[] commandLineArgs) =>
            Array.IndexOf(commandLineArgs, FullFlag) >= 0;

        public static int Warmup(int full) => Warmup(full, s_full);

        public static int Measurements(int full) => Measurements(full, s_full);

        public static int Warmup(int full, bool isFull) => isFull ? full : 0;

        public static int Measurements(int full, bool isFull) => isFull ? full : 1;
    }
}
