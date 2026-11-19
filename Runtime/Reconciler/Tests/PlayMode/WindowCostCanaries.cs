using System;
using NUnit.Framework;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Answers for the allocation count the benchmarks take through <see cref="WindowCost"/>, never for what they
    /// measure.
    /// </summary>
    [TestFixture]
    internal sealed class WindowCostCanaries
    {
        private const int KnownAllocations = 500;
        private const int OtherThreadAllocationCount = 10000;
        private const int Slack = 100;

        // The lower bound fails for a count that is not this window's, as the last window's is; the upper bound for a
        // count that takes in another thread's allocations.
        // GREEN_ON_BASE(characterization): the recorder behaviour this pins is the engine's, which the base runs too.
        [Test]
        public void Given_AnotherThreadAllocatingInsideTheWindow_When_TheOperationAllocatesAKnownCount_Then_TheCostIsThatCount()
        {
            // Arrange
            WindowCost.Of(() => Allocate(1));
            using var other = new OtherThreadAllocations(OtherThreadAllocationCount);

            // Act
            var cost = WindowCost.Of(() =>
            {
                Allocate(KnownAllocations);
                other.AllocateAndWait();
            });

            // Assert
            Assert.That(cost.AllocationBlocks, Is.InRange(KnownAllocations, KnownAllocations + Slack));
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
