using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskStructStateMachineContinuationPlayModeTests
    {
        static async VelvetTask<int> AccumulateAcrossTwoYields()
        {
            var token = 17;
            await VelvetTask.Yield();
            token += 5;
            await VelvetTask.Yield();
            return token;
        }

        // GREEN_ON_BASE(characterization): the base already resumes every await in this case.
        // What the bound changes is a wedge: with `PlayerLoop.SetPlayerLoop(playerLoop);` removed from
        // the frame driver, measured, this case fails in 20 s rather than at the runner's own timeout.
        [UnityTest]
        public IEnumerator Given_AsyncMethodWithTwoYields_When_AwaitedAfterYields_Then_PreservesLocalsAcrossSuspensions()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                var task = AccumulateAcrossTwoYields();
                Assume.That(task.Status.IsCompleted(), Is.False);

                // Act
                await VelvetTask.Yield();
                await VelvetTask.Yield();
                var result = await task;

                // Assert
                Assert.That(result, Is.EqualTo(22));
            }).Bounded();
    }
}
