using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Answers for the reading the benchmarks whose window waits out a frame take through
    /// <see cref="GCAllocationAcrossOneFrame"/>, never for what they measure.
    /// </summary>
    [TestFixture]
    internal sealed class GCAllocationAcrossOneFrameCanaries
    {
        private const int AllocationsBeforeTheBoundary = 500;
        private const int AllocationsAfterTheBoundary = 300;
        private const int OtherThreadAllocationCount = 10000;

        // Room for what else the running thread allocates across a frame, well short of the other thread's count.
        private const int Slack = 1000;

        // The lower bound fails for a reading that loses either side of the boundary; the upper bound for one that
        // takes in another thread's allocations.
        // GREEN_ON_BASE(characterization): the recorder behaviour this pins is the engine's, which the base runs too.
        [UnityTest]
        public IEnumerator Given_AllocationsOnBothSidesOfAFrameBoundary_When_TheWindowStops_Then_ItCountsBoth()
        {
            // Arrange
            const int expected = AllocationsBeforeTheBoundary + AllocationsAfterTheBoundary;
            using var other = new OtherThreadAllocations(OtherThreadAllocationCount);
            var window = new GCAllocationAcrossOneFrame();
            window.Start();
            Allocate(AllocationsBeforeTheBoundary);
            other.AllocateAndWait();
            yield return null;
            Allocate(AllocationsAfterTheBoundary);

            // Act
            var blocks = window.Stop();

            // Assert
            Assert.That(blocks, Is.InRange(expected, expected + Slack));
        }

        private static void Allocate(int count)
        {
            for (var i = 0; i < count; i++)
            {
                GC.KeepAlive(new byte[16]);
            }
        }
    }
}
