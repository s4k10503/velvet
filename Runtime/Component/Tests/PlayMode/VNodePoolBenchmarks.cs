using System;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Allocation benchmarks for VNodePool.
    /// Measures the GC-allocation reduction when the pool is enabled.
    /// </summary>
    [TestFixture]
    public class VNodePoolBenchmarks
    {
        #region B-2-a: Allocation measurement for V.Label / V.Div / V.Button

        [Test, Performance]
        public void VLabel_Allocation()
        {
            WarmUpPool(() => V.Label(text: "warmup"), 10);

            Measure.Method(() =>
            {
                var node = V.Label(text: "hello");
                ReturnNodeProps(node);
            })
            .GC()
            .WarmupCount(5)
            .MeasurementCount(30)
            .Run();
        }

        [Test, Performance]
        public void VDiv_Allocation()
        {
            WarmUpPool(() => V.Div("warmup-class"), 10);

            Measure.Method(() =>
            {
                var node = V.Div("my-class");
                ReturnNodeProps(node);
            })
            .GC()
            .WarmupCount(5)
            .MeasurementCount(30)
            .Run();
        }

        [Test, Performance]
        public void VButton_Allocation()
        {
            WarmUpPool(() => V.Button(text: "warmup"), 10);

            Measure.Method(() =>
            {
                var node = V.Button(text: "click me");
                ReturnNodeProps(node);
            })
            .GC()
            .WarmupCount(5)
            .MeasurementCount(30)
            .Run();
        }

        #endregion

        #region B-2-b: RentProps / ReturnProps cycle cost

        [Test, Performance]
        public void RentReturnProps_Cycle()
        {
            // Fill the pool
            for (int i = 0; i < 5; i++)
            {
                VNodePool.ReturnProps(new FiberElementProps());
            }

            Measure.Method(() =>
            {
                var props = VNodePool.RentProps();
                VNodePool.ReturnProps(props);
            })
            .GC()
            .WarmupCount(5)
            .MeasurementCount(30)
            .Run();
        }

        #endregion

        #region Helpers

        private static void WarmUpPool(Func<VNode> factory, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ReturnNodeProps(factory());
            }
        }

        private static void ReturnNodeProps(VNode node)
        {
            if (node is ElementNode en && en.Props != null)
            {
                VNodePool.ReturnProps(en.Props);
            }
        }

        #endregion
    }
}
