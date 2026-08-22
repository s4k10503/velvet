using System;
using System.Threading;
using NUnit.Framework;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    [Category("Performance")]
    internal sealed class RouteLoaderRunnerAllocationTests
    {
        private static readonly RouteLoaderRunner SharedRunner = new();

        private static VelvetTask<object> CompletedLoader(RouteLoaderContext _, CancellationToken __) =>
            VelvetTask.FromResult<object>("loaded-data");

        private static void RunAwaitLoader()
        {
            var matches = MakeMatch("test", loader: CompletedLoader, loaderMode: LoaderMode.Await);
            SharedRunner.RunLoadersSync(matches, CancellationToken.None);
        }

        private static void RunSuspendLoader()
        {
            var matches = MakeMatch("test", loader: CompletedLoader, loaderMode: LoaderMode.Suspend);
            SharedRunner.RunLoadersSync(matches, CancellationToken.None);
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
        public void Given_WarmSyncCompletedLoader_When_BothModesRun_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                RunAwaitLoader();
                RunSuspendLoader();
            }

            // Act
            var awaitBlocks = GCAllocationProbe.SampleBlocksDuring(RunAwaitLoader);
            var suspendBlocks = GCAllocationProbe.SampleBlocksDuring(RunSuspendLoader);

            // Assert — both are pinned rather than ordered, and an assertion with a slack constant would
            // move with whichever path grew.
            Assert.That((awaitBlocks, suspendBlocks), Is.EqualTo((20, 21)));
        }
    }
}
