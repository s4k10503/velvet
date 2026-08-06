using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskMainThreadPlayModeTests
    {
        static int s_mainThreadId;

        [UnityTest]
        public IEnumerator Given_AsyncVelvetTaskOnMainThread_When_AwaitedAfterYield_Then_ResumesOnMainThread()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
                await VelvetTask.Yield();

                // Act
                var resumedOnMainThread = Thread.CurrentThread.ManagedThreadId == s_mainThreadId;

                // Assert
                Assert.That(resumedOnMainThread, Is.True);
            });
    }
}
