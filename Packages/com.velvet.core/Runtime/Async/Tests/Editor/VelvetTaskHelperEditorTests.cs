using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskHelperEditorTests
    {
        [Test]
        public void Given_CompletedVelvetTask_When_ForgetCalled_Then_DoesNotThrow()
        {
            // Arrange
            var task = VelvetTask.CompletedTask;

            // Act
            void Act() => task.Forget();

            // Assert
            Assert.DoesNotThrow(Act);
        }

        [Test]
        public void Given_CancelledToken_When_AttachExternalCancellationOnPendingTask_Then_CancelsAttachedTask()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var pending = new VelvetTaskCompletionSource<int>().Task;

            // Act
            var attached = pending.AttachExternalCancellation(cts.Token);
            cts.Cancel();

            // Assert
            Assert.That(attached.Status.IsCanceled(), Is.True);
        }

        [Test]
        public void Given_SyncCompletedVelvetTask_When_ToCoroutineRuns_Then_CompletesWithoutFurtherMoves()
        {
            // Arrange
            var enumerator = VelvetTask.ToCoroutine(async () => await VelvetTask.CompletedTask);

            // Act
            var hasNext = enumerator.MoveNext();

            // Assert
            Assert.That(hasNext, Is.False);
        }

        [Test]
        public void Given_FromExceptionVelvetTask_When_StatusPeeked_Then_IsFaulted()
        {
            // Arrange
            var task = VelvetTask.FromException(new Exception("boom"));

            // Act
            var faulted = task.Status.IsFaulted();

            // Assert
            Assert.That(faulted, Is.True);
        }
    }
}
