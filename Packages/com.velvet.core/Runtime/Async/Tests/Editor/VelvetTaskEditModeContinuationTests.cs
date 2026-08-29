using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskEditModeContinuationTests
    {
        static async VelvetTask<int> AwaitFromResult() => await VelvetTask.FromResult(42);

        static async VelvetTask<int> AwaitSameStackCompletionSource()
        {
            var tcs = new VelvetTaskCompletionSource<int>();
            tcs.TrySetResult(42);
            return await tcs.Task;
        }

        static async VelvetTask AwaitYield() => await VelvetTask.Yield();

        static async VelvetTask<int> AwaitYieldThenFromResult()
        {
            await VelvetTask.Yield();
            return await VelvetTask.FromResult(42);
        }

        static async VelvetTask<int> AwaitYieldThenSameStackCompletionSource()
        {
            await VelvetTask.Yield();
            var tcs = new VelvetTaskCompletionSource<int>();
            tcs.TrySetResult(42);
            return await tcs.Task;
        }

        static async VelvetTask<int> ThrowingAsyncMethod()
        {
            await VelvetTask.CompletedTask;
            throw new InvalidOperationException("async fault");
        }

        static async VelvetTask<int> CancelingAsyncMethod()
        {
            await VelvetTask.CompletedTask;
            throw new OperationCanceledException();
        }

        [Test]
        public void Given_SynchronouslyCompletedFromResult_When_AwaitedInEditMode_Then_Completes()
        {
            // Arrange
            var task = AwaitFromResult();

            // Act
            var completedSynchronously = task.Status.IsCompletedSuccessfully();

            // Assert
            Assert.That(completedSynchronously, Is.True);
        }

        [Test]
        public void Given_CompletionSourceSetOnSameCallStack_When_AwaitedInEditMode_Then_Completes()
        {
            // Arrange
            var task = AwaitSameStackCompletionSource();

            // Act
            var completedSynchronously = task.Status.IsCompletedSuccessfully();

            // Assert
            Assert.That(completedSynchronously, Is.True);
        }

        [Test]
        public void Given_Yield_When_AwaitedInEditMode_Then_DoesNotCompleteSynchronously()
        {
            // Arrange
            var task = VelvetTask.Yield();

            // Act
            var completedSynchronously = task.Status.IsCompleted();

            // Assert
            Assert.That(completedSynchronously, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_Yield_When_EditorUpdateTicks_Then_Completes()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var task = AwaitYield();
            Assume.That(task.Status.IsCompleted(), Is.False, "Precondition: yield is pending on the call stack");

            // Act
            await VelvetTask.Yield();

            // Assert
            Assert.That(task.Status.IsCompletedSuccessfully(), Is.True);
        });

        [Test]
        public void Given_AsyncMethodAwaitingFromResult_When_InvokedInEditMode_Then_Completes()
        {
            // Arrange
            var task = AwaitFromResult();

            // Act
            var completedSynchronously = task.Status.IsCompletedSuccessfully();

            // Assert
            Assert.That(completedSynchronously, Is.True);
        }

        [Test]
        public void Given_AsyncMethodAwaitingSameStackCompletionSource_When_InvokedInEditMode_Then_Completes()
        {
            // Arrange
            var task = AwaitSameStackCompletionSource();

            // Act
            var completedSynchronously = task.Status.IsCompletedSuccessfully();

            // Assert
            Assert.That(completedSynchronously, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_AsyncMethodAwaitingYield_When_EditorUpdateTicks_Then_Completes()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var task = AwaitYield();
            Assume.That(task.Status.IsCompleted(), Is.False, "Precondition: yield is pending on the call stack");

            // Act
            await VelvetTask.Yield();

            // Assert
            Assert.That(task.Status.IsCompletedSuccessfully(), Is.True);
        });

        [UnityTest]
        public IEnumerator Given_AsyncMethodAwaitingYieldThenFromResult_When_EditorUpdateTicks_Then_CompletesWithValue()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var task = AwaitYieldThenFromResult();
            Assume.That(task.Status.IsCompleted(), Is.False, "Precondition: first yield is pending");

            // Act
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(42));
        });

        [Test]
        public void Given_AsyncMethodAwaitingYieldThenSameStackCompletionSource_When_EditorUpdateDrained_Then_CompletesWithValue()
        {
            // Arrange
            var task = AwaitYieldThenSameStackCompletionSource();
            Assume.That(task.Status.IsCompleted(), Is.False, "Precondition: yield is pending");

            // Act
            DrainEditorUpdateForTest();
            DrainEditorUpdateForTest();
            var result = task.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(42));
        }

        [UnityTest]
        public IEnumerator Given_AsyncMethodAwaitingYieldThenSameStackCompletionSource_When_EditorUpdateTicks_Then_CompletesWithValue()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var task = AwaitYieldThenSameStackCompletionSource();
            Assume.That(task.Status.IsCompleted(), Is.False, "Precondition: yield is pending");

            // Act
            await VelvetTask.Yield();
            await VelvetTask.Yield();

            // Assert
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(42));
        });

        [Test]
        public void Given_AsyncMethodThatThrows_When_StatusPeeked_Then_IsFaulted()
        {
            // Arrange
            var task = ThrowingAsyncMethod();

            // Act
            var faulted = task.Status.IsFaulted();

            // Assert
            Assert.That(faulted, Is.True);
        }

        [Test]
        public void Given_AsyncMethodThatThrowsOperationCanceledException_When_StatusPeeked_Then_IsCanceled()
        {
            // Arrange
            var task = CancelingAsyncMethod();

            // Act
            var canceled = task.Status.IsCanceled();

            // Assert
            Assert.That(canceled, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_Yield_When_ToCoroutineRunsInEditMode_Then_CompletesAfterEditorUpdate()
        {
            // Arrange
            var enumerator = VelvetTask.ToCoroutine(AwaitYield);
            Assume.That(enumerator.MoveNext(), Is.True, "Precondition: the coroutine starts pending");

            // Act
            var stillRunning = true;
            for (var i = 0; i < 8 && stillRunning; i++)
            {
                yield return null;
                stillRunning = enumerator.MoveNext();
            }

            // Assert
            Assert.That(stillRunning, Is.False, "Editor updates complete the yield and the coroutine wrapper");
        }
    }
}
