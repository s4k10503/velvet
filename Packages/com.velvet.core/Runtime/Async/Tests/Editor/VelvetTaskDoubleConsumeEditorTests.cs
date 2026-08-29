using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    internal sealed class VelvetTaskDoubleConsumeEditorTests
    {
        static readonly FieldInfo VelvetTaskSourceField =
            typeof(VelvetTask).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static readonly FieldInfo VelvetTaskGenericSourceField =
            typeof(VelvetTask<int>).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static object GetTaskSource(VelvetTask task) =>
            VelvetTaskSourceField.GetValue(task)!;

        static object GetTaskSource(VelvetTask<int> task) =>
            VelvetTaskGenericSourceField.GetValue(task)!;

        static async VelvetTask<int> AsyncYieldThenReturn()
        {
            await VelvetTask.Yield();
            return 42;
        }

        static async VelvetTask AsyncYieldVoid()
        {
            await VelvetTask.Yield();
        }

        static async VelvetTask<int> AsyncFromResult() => await VelvetTask.FromResult(42);
        [Test]
        public void Given_SyncCompletedVelvetTask_When_PeekStatusThenGetResultOnce_Then_ReturnsValue()
        {
            // Arrange
            var task = VelvetTask.FromResult(42);

            // Act
            Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
            var result = task.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void Given_SyncCompletedVelvetTaskFromResult_When_GetResultTwice_Then_ReturnsSameValue()
        {
            // Arrange
            var task = VelvetTask.FromResult(42);
            var first = task.GetAwaiter().GetResult();

            // Act
            var second = task.GetAwaiter().GetResult();

            // Assert
            Assert.That((first, second), Is.EqualTo((42, 42)));
        }

        [Test]
        public void Given_CollectedAwaitModeTasks_When_EachPeekedAndConsumedOnce_Then_ReturnsStoredValues()
        {
            // Arrange
            var tasks = new List<VelvetTask<int>>
            {
                VelvetTask.FromResult(10),
                VelvetTask.FromResult(20),
            };

            // Act
            Assume.That(tasks[0].Status.IsCompleted(), Is.True);
            var first = tasks[0].GetAwaiter().GetResult();
            Assume.That(tasks[1].Status.IsCompleted(), Is.True);
            var second = tasks[1].GetAwaiter().GetResult();

            // Assert
            Assert.That(first + second, Is.EqualTo(30));
        }

        [Test]
        public void Given_FaultedVelvetTask_When_PeekStatusThenConsumeOnce_Then_ThrowsStoredException()
        {
            // Arrange
            var expected = new InvalidOperationException("fault");
            var task = VelvetTask.FromException<int>(expected);

            // Act
            Assume.That(task.Status.IsFaulted(), Is.True);
            var thrown = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(expected));
        }

        [Test]
        public void Given_RunnerBackedCompletedVelvetTask_When_GetResultTwice_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            source.SetResult(42);
            var task = source.Task;
            task.GetAwaiter().GetResult();

            // Act
            void SecondConsume() => task.GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(SecondConsume)!.Message,
                Is.EqualTo("The VelvetTask has already been consumed."));
        }

        [Test]
        public void Given_AsyncMethodCompletingSynchronously_When_GetResultTwice_Then_ReturnsSameValue()
        {
            // Arrange
            var task = AsyncFromResult();
            Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
            var first = task.GetAwaiter().GetResult();

            // Act
            var second = task.GetAwaiter().GetResult();

            // Assert
            Assert.That((first, second), Is.EqualTo((42, 42)));
        }

        [Test]
        public void Given_AsyncMethodCompletingAsynchronously_When_GetResultTwice_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var task = AsyncYieldThenReturn();
            Assume.That(task.Status.IsCompleted(), Is.False);
            DrainEditorUpdateForTest();
            Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
            task.GetAwaiter().GetResult();

            // Act
            void SecondConsume() => task.GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(SecondConsume)!.Message,
                Is.EqualTo("The VelvetTask has already been consumed."));
        }

        [Test]
        public void Given_AsyncVoidMethodCompletingAsynchronously_When_GetResultTwice_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var task = AsyncYieldVoid();
            Assume.That(task.Status.IsCompleted(), Is.False);
            DrainEditorUpdateForTest();
            Assume.That(task.Status.IsCompletedSuccessfully(), Is.True);
            task.GetAwaiter().GetResult();

            // Act
            void SecondConsume() => task.GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(SecondConsume)!.Message,
                Is.EqualTo("The VelvetTask has already been consumed."));
        }

        [Test]
        public void Given_RejectedRunnerBackedConsume_When_SubsequentAsyncMethodsComplete_Then_EachUsesDistinctSource()
        {
            // Arrange
            static async VelvetTask<int> AsyncReturn(int value)
            {
                await VelvetTask.Yield();
                return value;
            }

            var firstTask = AsyncYieldThenReturn();
            DrainEditorUpdateForTest();
            Assume.That(firstTask.Status.IsCompletedSuccessfully(), Is.True);
            var firstSource = GetTaskSource(firstTask);
            firstTask.GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => firstTask.GetAwaiter().GetResult());

            // Act
            var secondTask = AsyncReturn(2);
            var thirdTask = AsyncReturn(3);
            DrainEditorUpdateForTest();
            var secondResult = secondTask.GetAwaiter().GetResult();
            var thirdResult = thirdTask.GetAwaiter().GetResult();

            // Assert
            var sharesSourceWithFirst =
                ReferenceEquals(GetTaskSource(secondTask), firstSource)
                || ReferenceEquals(GetTaskSource(thirdTask), firstSource)
                || ReferenceEquals(GetTaskSource(secondTask), GetTaskSource(thirdTask));
            Assert.That((secondResult, thirdResult, sharesSourceWithFirst), Is.EqualTo((2, 3, false)));
        }

        [Test]
        public void Given_RejectedFromExceptionConsume_When_TwoMoreFromExceptionTasksCreated_Then_EachUsesDistinctSource()
        {
            // Arrange
            var expected = new InvalidOperationException("fault");
            var firstTask = VelvetTask.FromException(expected);
            Assert.Throws<InvalidOperationException>(() => firstTask.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => firstTask.GetAwaiter().GetResult());

            // Act
            var secondTask = VelvetTask.FromException(expected);
            var thirdTask = VelvetTask.FromException(expected);
            var sharesSource =
                ReferenceEquals(GetTaskSource(secondTask), GetTaskSource(thirdTask));

            // Assert
            Assert.That(sharesSource, Is.False);
        }
    }
}
