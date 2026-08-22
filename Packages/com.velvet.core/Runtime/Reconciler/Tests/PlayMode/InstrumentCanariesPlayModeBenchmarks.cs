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

        // GREEN_ON_BASE(characterization): a canary that moved with this change would not be a canary.
        // It charges Unity.PerformanceTesting's GC measurement with a known 16-byte array, answering for the
        // recorder the async allocation benchmarks beside it read, never for the async paths themselves.
        [Test, Performance]
        public void InstrumentCanary_AllocationBlocks()
        {
            Measure.Method(() => GC.KeepAlive(new byte[16]))
                .GC()
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .Run();
        }

        // GREEN_ON_BASE(characterization): a canary that moved with this change would not be a canary.
        // It charges GCAllocationProbe with a known 16-byte array, so it answers for the probe the async
        // allocation benchmarks beside it read, never for the async paths themselves.
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

        // GREEN_ON_BASE(characterization): a canary that moved with this change would not be a canary.
        // It times an arithmetic loop that calls nothing this branch touched, so it answers for the timing
        // harness the async benchmarks beside it read, never for the async paths themselves.
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
