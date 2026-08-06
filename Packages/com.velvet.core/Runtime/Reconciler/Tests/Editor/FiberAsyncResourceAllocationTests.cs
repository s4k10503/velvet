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

        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Arrange
            Action canary = () => GC.KeepAlive(new byte[16]);
            canary();

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(canary);

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_WarmSyncFactory_When_StartingResource_Then_StartAllocatesNoHeapBlocks()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                StartWarmResource();
            }

            var resource = new FiberAsyncResource<int>(ResourceKey);

            // Act
            var blocks = GCAllocationProbe.SampleBlocksDuring(() => resource.Start(SyncFactory));

            // Assert — a synchronously completed factory must not charge heap blocks on Start after warmup.
            Assert.That(blocks, Is.EqualTo(0));
        }
    }
}
