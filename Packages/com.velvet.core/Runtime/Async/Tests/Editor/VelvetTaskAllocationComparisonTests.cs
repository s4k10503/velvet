using System;
using NUnit.Framework;
using Velvet.TestUtilities;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    [Category("Performance")]
    internal sealed class VelvetTaskAllocationComparisonTests
    {
        static async VelvetTask<int> VelvetTaskSyncAwait() => await VelvetTask.FromResult(1);

#if UNITY_EDITOR
        static async VelvetTask VelvetTaskYieldAwait() => await VelvetTask.Yield();
#endif

        // GREEN_ON_BASE(characterization): the probe already counts this canary's allocation.
        // This change reads it over three windows.
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

        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_WarmSyncAwaitSteadyState_When_Awaited_Then_SteadyPathAllocatesNoMoreThanAsyncStateMachine()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                VelvetTaskSyncAwait().GetAwaiter().GetResult();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(
                () => VelvetTaskSyncAwait().GetAwaiter().GetResult());

            // Assert — the async lambda delegate may retain one GC.Alloc block from its state machine.
            Assert.That(blocks, Is.LessThanOrEqualTo(1));
        }

#if UNITY_EDITOR
        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_WarmYieldAwaitSteadyState_When_YieldDrainedAndAwaited_Then_SteadyPathAllocatesNoMoreThanMeasuredBlocks()
        {
            // Arrange
            for (var i = 0; i < 64; i++)
            {
                var task = VelvetTaskYieldAwait();
                DrainEditorUpdateForTest();
                task.GetAwaiter().GetResult();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(() =>
            {
                var task = VelvetTaskYieldAwait();
                DrainEditorUpdateForTest();
                task.GetAwaiter().GetResult();
            });

            // Assert — yield scheduling and the async state machine retain five steady-state blocks.
            Assert.That(blocks, Is.EqualTo(5));
        }
#endif
    }
}
