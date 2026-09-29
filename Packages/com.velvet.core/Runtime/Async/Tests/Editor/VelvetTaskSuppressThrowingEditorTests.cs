using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskSuppressThrowingEditorTests
    {
        static async VelvetTask<VelvetTaskStatus> AwaitSuppressingThrows(VelvetTask task) => await task.SuppressThrowing();

        static async VelvetTask<VelvetTaskStatus> AwaitSuppressingThrows(VelvetTask<int> task) => await task.SuppressThrowing();

        static async VelvetTask Relay(VelvetTaskCompletionSource source) => await source.Task;

        [Test]
        public void Given_ATaskCanceledWhileAwaited_When_AwaitedWithSuppressThrowing_Then_TheAwaitReturnsCanceled()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var awaiting = AwaitSuppressingThrows(source.Task);

            // Act
            source.SetCanceled();

            // Assert
            Assert.That(awaiting.GetAwaiter().GetResult(), Is.EqualTo(VelvetTaskStatus.Canceled));
        }

        [Test]
        public void Given_AFaultedTask_When_AwaitedWithSuppressThrowing_Then_TheAwaitReturnsFaulted()
        {
            // Arrange
            var faulted = VelvetTask.FromException(new InvalidOperationException("boom"));

            // Act
            var status = AwaitSuppressingThrows(faulted).GetAwaiter().GetResult();

            // Assert
            Assert.That(status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_AFaultedResultTask_When_AwaitedWithSuppressThrowing_Then_TheAwaitReturnsFaulted()
        {
            // Arrange
            var faulted = VelvetTask.FromException<int>(new InvalidOperationException("boom"));

            // Act
            var status = AwaitSuppressingThrows(faulted).GetAwaiter().GetResult();

            // Assert
            Assert.That(status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        // GREEN_ON_BASE(characterization): the single-consume rule this pins holds on the base.
        // Its task comes from an async method, since a completion source's task is not single-consume.
        [Test]
        public void Given_AFaultedTaskAlreadyConsumed_When_ReadWithSuppressThrowing_Then_ItStillThrowsAlreadyConsumed()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var task = Relay(source);
            source.SetException(new InvalidOperationException("boom"));
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            void ReadAgain() => task.SuppressThrowing().GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(ReadAgain)!.Message,
                Is.EqualTo("The VelvetTask has already been consumed."));
        }

        [Test]
        public void Given_APendingTask_When_ReadWithSuppressThrowing_Then_ItStillThrowsNotCompleted()
        {
            // Arrange
            var pending = new VelvetTaskCompletionSource().Task;

            // Act
            void Read() => pending.SuppressThrowing().GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(Read)!.Message,
                Is.EqualTo("The VelvetTask is not completed."));
        }
    }
}
