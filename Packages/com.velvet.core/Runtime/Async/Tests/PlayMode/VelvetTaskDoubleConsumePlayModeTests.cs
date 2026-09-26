using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskDoubleConsumePlayModeTests
    {
        [UnityTest]
        public IEnumerator Given_StoredPendingVelvetTask_When_CompletedAndAwaitedOnce_Then_ReturnsResult()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                var source = new VelvetTaskCompletionSource<int>();
                var stored = source.Task;
                Assume.That(stored.Status.IsCompleted(), Is.False);
                source.SetResult(7);

                // Act
                var result = await stored;

                // Assert
                Assert.That(result, Is.EqualTo(7));
            });

        [UnityTest]
        public IEnumerator Given_RunnerBackedCompletedVelvetTask_When_SecondAwaitAttempted_Then_ThrowsInvalidOperationException()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                var source = new VelvetTaskCompletionSource<int>();
                source.SetResult(99);
                var task = source.Task;
                await task;

                // Act
                Exception? caught = null;
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                // Assert
                Assert.That(caught, Is.TypeOf<InvalidOperationException>()
                    .And.Message.EqualTo("The VelvetTask has already been consumed."));
            });

        // GREEN_ON_BASE(characterization): the base already resumes every await in this case.
        // What the bound changes is a wedge: with `PlayerLoop.SetPlayerLoop(playerLoop);` removed from
        // the frame driver, measured, this case fails in 20 s rather than at the runner's own timeout.
        [UnityTest]
        public IEnumerator Given_AsyncMethodCompletingAsynchronously_When_SecondAwaitAttempted_Then_ThrowsInvalidOperationException()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                static async VelvetTask<int> AsyncYieldThenReturn()
                {
                    await VelvetTask.Yield();
                    return 99;
                }

                var task = AsyncYieldThenReturn();
                Assume.That(task.Status.IsCompleted(), Is.False);
                await VelvetTask.Yield();
                Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
                await task;

                // Act
                Exception? caught = null;
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                // Assert
                Assert.That(caught, Is.TypeOf<InvalidOperationException>()
                    .And.Message.EqualTo("The VelvetTask has already been consumed."));
            }).Bounded();

        // GREEN_ON_BASE(characterization): the base already resumes every await in this case.
        // Bounded for the reason the declaration above the first bounded case in this file gives.
        [UnityTest]
        public IEnumerator Given_AsyncVoidMethodCompletingAsynchronously_When_SecondAwaitAttempted_Then_ThrowsInvalidOperationException()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                static async VelvetTask AsyncYieldVoid()
                {
                    await VelvetTask.Yield();
                }

                var task = AsyncYieldVoid();
                Assume.That(task.Status.IsCompleted(), Is.False);
                await VelvetTask.Yield();
                Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
                await task;

                // Act
                Exception? caught = null;
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                // Assert
                Assert.That(caught, Is.TypeOf<InvalidOperationException>()
                    .And.Message.EqualTo("The VelvetTask has already been consumed."));
            }).Bounded();

        // GREEN_ON_BASE(characterization): the base already resumes every await in this case.
        // Bounded for the reason the declaration above the first bounded case in this file gives.
        [UnityTest]
        public IEnumerator Given_RejectedRunnerBackedConsume_When_SubsequentAsyncMethodsComplete_Then_EachUsesDistinctSource()
            => VelvetTask.ToCoroutine(async () =>
            {
                // Arrange
                static async VelvetTask<int> AsyncReturn(int value)
                {
                    await VelvetTask.Yield();
                    return value;
                }

                static async VelvetTask<int> AsyncYieldThenReturn()
                {
                    await VelvetTask.Yield();
                    return 42;
                }

                static object GetTaskSource(VelvetTask<int> task) =>
                    typeof(VelvetTask<int>)
                        .GetField("_source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .GetValue(task)!;

                var firstTask = AsyncYieldThenReturn();
                await VelvetTask.Yield();
                await firstTask;
                try
                {
                    await firstTask;
                }
                catch (InvalidOperationException)
                {
                }

                // Act
                var secondTask = AsyncReturn(2);
                var thirdTask = AsyncReturn(3);
                await VelvetTask.Yield();
                var secondResult = await secondTask;
                var thirdResult = await thirdTask;
                var sharesSource =
                    ReferenceEquals(GetTaskSource(secondTask), GetTaskSource(thirdTask));

                // Assert
                Assert.That((secondResult, thirdResult, sharesSource), Is.EqualTo((2, 3, false)));
            }).Bounded();
    }
}
