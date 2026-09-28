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

        private static void OpenEmptyRound()
        {
            SharedRunner.EmptyRound();
        }

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
        public void Given_WarmSyncCompletedLoader_When_BothModesRun_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                RunAwaitLoader();
                RunSuspendLoader();
            }

            // Act
            var awaitBlocks = GCAllocationProbe.MedianBlocksDuring(RunAwaitLoader);
            var suspendBlocks = GCAllocationProbe.MedianBlocksDuring(RunSuspendLoader);

            // Assert — both are pinned rather than ordered, and an assertion with a slack constant would
            // move with whichever path grew.
            Assert.That((awaitBlocks, suspendBlocks), Is.EqualTo((18, 19)));
        }

        // GREEN_ON_BASE(characterization): the base already allocates what this case pins.
        // This change reads it over three windows.
        [Test]
        public void Given_AWarmRunner_When_ARoundRunningNoLoadersIsOpened_Then_AllocationMatchesPinnedExpectation()
        {
            // Arrange
            for (var i = 0; i < 16; i++)
            {
                OpenEmptyRound();
            }

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(OpenEmptyRound);

            // Assert
            Assert.That(blocks, Is.EqualTo(3));
        }
    }
}
