using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that sampling a spring play at a time, which a play on a <c>MotionPlayback</c> does every frame,
    /// allocates nothing, against a canary that shows the probe counting.
    /// </summary>
    internal sealed class MotionPlaybackAllocationTests
    {
        // A probe stuck at zero satisfies the guard below without measuring anything.
        // GREEN_ON_BASE(characterization): the probe already counts this canary's allocation.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Arrange
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AWarmSpringPlay_When_SampledAtATime_Then_NothingIsAllocated()
        {
            // Arrange
            var element = new VisualElement();
            var state = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(
                new[] { "opacity-0", "translate-x-[0px]" }, new[] { "opacity-100", "translate-x-[10px]" }),
                100f, 10f, 1f);
            Action sample = () => MotionSpringDriver.SeekTo(element, state, 0.1f);
            sample();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(sample);

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
