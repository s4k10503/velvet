using System;
using System.Threading;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    [Category("Performance")]
    internal sealed class FiberAsyncResourceAllocationTests
    {
        private static readonly object ResourceKey = new();

        private static VelvetTask<int> SyncFactory(CancellationToken _) => VelvetTask.FromResult(42);

        private static void StartWarmResource()
        {
            var resource = new FiberAsyncResource<int>(ResourceKey);
            resource.Start(SyncFactory);
        }

        // GREEN_ON_BASE(refactor): the probe already counts a 16-byte array allocation on the base, which is all this case asserts.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_WarmSyncFactory_When_StartingResource_Then_StartAllocatesNoHeapBlocks()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                StartWarmResource();
            }

            FiberAsyncResource<int> resource = null;

            // Act — a started resource returns from Start at once, so each window starts a fresh one.
            var blocks = GCAllocationProbe.MedianBlocksDuring(
                () => resource = new FiberAsyncResource<int>(ResourceKey),
                () => resource.Start(SyncFactory));

            // Assert — a synchronously completed factory must not charge heap blocks on Start after warmup.
            Assert.That(blocks, Is.EqualTo(0));
        }
    }
}
