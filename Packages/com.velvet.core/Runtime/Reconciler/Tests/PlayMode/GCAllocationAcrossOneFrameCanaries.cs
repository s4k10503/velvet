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

        // GREEN_ON_BASE(characterization): the recorder behaviour this pins is the engine's, which the base runs too.
        [UnityTest]
        public IEnumerator Given_AllocationsOnBothSidesOfAFrameBoundary_When_TheWindowStops_Then_ItCountsBoth()
        {
            // Arrange
            var window = new GCAllocationAcrossOneFrame();
            window.Start();
            Allocate(AllocationsBeforeTheBoundary);
            yield return null;
            Allocate(AllocationsAfterTheBoundary);

            // Act
            var blocks = window.Stop();

            // Assert
            Assert.That(blocks, Is.GreaterThanOrEqualTo(AllocationsBeforeTheBoundary + AllocationsAfterTheBoundary));
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
