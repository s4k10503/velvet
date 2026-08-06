using System;
using NUnit.Framework;
using Unity.PerformanceTesting;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    [TestFixture]
    internal sealed class InstrumentCanariesPlayModeBenchmarks
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;
        private const int TimingCanaryIterations = 10000;

        [Test, Performance]
        public void InstrumentCanary_AllocationBlocks()
        {
            Measure.Method(() => GC.KeepAlive(new byte[16]))
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        [Test, Performance]
        public void InstrumentCanary_AllocationProbeCountsKnownArray()
        {
            // Arrange
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test, Performance]
        public void InstrumentCanary_TimingBaseline()
        {
            Measure.Method(() =>
                {
                    var sum = 0;
                    for (var i = 0; i < TimingCanaryIterations; i++)
                    {
                        sum += i;
                    }

                    GC.KeepAlive(sum);
                })
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }
    }
}
