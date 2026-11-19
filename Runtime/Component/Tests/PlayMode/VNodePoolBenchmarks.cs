using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// What one <c>V.Label</c>, <c>V.Div</c> or <c>V.Button</c> call allocates.
    /// </summary>
    [TestFixture]
    public class VNodePoolBenchmarks
    {
        #region B-2-a: Allocation measurement for V.Label / V.Div / V.Button

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void VLabel_Allocation()
        {
            WarmUp(() => V.Label(text: "warmup"), 10);

            Measure.Method(() => GC.KeepAlive(V.Label(text: "hello")))
                .GC()
                .WarmupCount(BenchmarkRun.Warmup(5))
                .MeasurementCount(BenchmarkRun.Measurements(30))
                .Run();
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void VDiv_Allocation()
        {
            WarmUp(() => V.Div("warmup-class"), 10);

            Measure.Method(() => GC.KeepAlive(V.Div("my-class")))
                .GC()
                .WarmupCount(BenchmarkRun.Warmup(5))
                .MeasurementCount(BenchmarkRun.Measurements(30))
                .Run();
        }

        // GREEN_ON_BASE(characterization): a benchmark asserts nothing about its numbers, so the base runs it green.
        [Test, Performance]
        public void VButton_Allocation()
        {
            WarmUp(() => V.Button(text: "warmup"), 10);

            Measure.Method(() => GC.KeepAlive(V.Button(text: "click me")))
                .GC()
                .WarmupCount(BenchmarkRun.Warmup(5))
                .MeasurementCount(BenchmarkRun.Measurements(30))
                .Run();
        }

        #endregion

        private static void WarmUp(Func<VNode> factory, int count)
        {
            for (var i = 0; i < count; i++)
            {
                GC.KeepAlive(factory());
            }
        }
    }
}
