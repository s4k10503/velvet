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
        static async VelvetTask<int> Relay(VelvetTaskCompletionSource<int> source) => await source.Task;

        static async VelvetTask AwaitReady(VelvetTask ready) => await ready;

        [Test]
        public void Given_ACompletionSourceTaskAlreadyConsumed_When_ItsStatusIsRead_Then_ItReportsTheOutcome()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var task = source.Task;
            source.SetResult();
            task.GetAwaiter().GetResult();

            // Act
            var status = task.Status;

            // Assert
            Assert.That(status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_AResultCompletionSourceTaskAlreadyConsumed_When_ItIsConsumedAgain_Then_ItReturnsTheResultAgain()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var task = source.Task;
            source.SetResult(5);
            task.GetAwaiter().GetResult();

            // Act
            var again = task.GetAwaiter().GetResult();

            // Assert
            Assert.That(again, Is.EqualTo(5));
        }

        [Test]
        public void Given_AReadyLatchOneCallerAwaited_When_ASecondCallerAwaitsItAfterItCompleted_Then_BothComplete()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var ready = source.Task;
            var first = AwaitReady(ready);
            source.SetResult();

            // Act
            var second = AwaitReady(ready);

            // Assert
            Assert.That((first.Status, second.Status), Is.EqualTo((VelvetTaskStatus.Succeeded, VelvetTaskStatus.Succeeded)));
        }

        // GREEN_ON_BASE(characterization): a completion source already completed refuses a second outcome on the base.
        [Test]
        public void Given_ACompletedCompletionSource_When_ASecondResultIsOffered_Then_ItIsRefusedAndTheFirstStands()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            source.SetResult(1);

            // Act
            var accepted = source.TrySetResult(2);

            // Assert
            Assert.That((accepted, source.Task.GetAwaiter().GetResult()), Is.EqualTo((false, 1)));
        }

        [Test]
        public void Given_TwoCallersAwaitingOneCompletionSourceTask_When_ItCompletes_Then_BothComplete()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var ready = source.Task;
            var first = AwaitReady(ready);
            var second = AwaitReady(ready);

            // Act
            source.SetResult();

            // Assert
            Assert.That((first.Status, second.Status), Is.EqualTo((VelvetTaskStatus.Succeeded, VelvetTaskStatus.Succeeded)));
        }

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

        // GREEN_ON_BASE(characterization): the single-consume rule this pins holds on the base.
        // Its task comes from an async method, since a completion source's task is not single-consume.
        [Test]
        public void Given_RunnerBackedCompletedVelvetTask_When_GetResultTwice_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var task = Relay(source);
            source.SetResult(42);
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
