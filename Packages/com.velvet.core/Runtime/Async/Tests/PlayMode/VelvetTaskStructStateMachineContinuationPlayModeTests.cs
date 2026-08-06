using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

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
            });
    }
}
