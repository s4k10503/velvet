using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskMainThreadPlayModeTests
    {
        const int ResumeFrameBudget = 30;

        static int s_mainThreadId;

        int _resumedThreadId;

        // GREEN_ON_BASE(characterization): the base already resumes every await in this case.
        // What the bound changes is a wedge: with `PlayerLoop.SetPlayerLoop(playerLoop);` removed from
        // the frame driver, measured, this case fails in 20 s rather than at the runner's own timeout.
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
            }).Bounded();

        [UnityTest]
        public IEnumerator Given_AnAsyncVelvetTaskAwaitedOnTheMainThread_When_ItFinishesOffTheMainThread_Then_TheAwaitResumesOnTheMainThread()
        {
            // Arrange
            var gate = new TaskCompletionSource<int>();
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _resumedThreadId = 0;
            AwaitOneThatFinishesOffTheMainThread(gate.Task).Forget();

            // Act
            Task.Run(() => gate.SetResult(1));
            for (var frame = 0; frame < ResumeFrameBudget && _resumedThreadId == 0; frame++)
            {
                yield return null;
            }

            // Assert
            Assert.That(_resumedThreadId, Is.EqualTo(mainThreadId));
        }

        static async VelvetTask<int> FinishOffTheMainThread(Task<int> gate)
        {
            var value = await gate.ConfigureAwait(false);
            return value + 1;
        }

        async VelvetTask AwaitOneThatFinishesOffTheMainThread(Task<int> gate)
        {
            await FinishOffTheMainThread(gate);
            _resumedThreadId = Thread.CurrentThread.ManagedThreadId;
        }
    }
}
