using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskCancellationEditorTests
    {
        static async VelvetTask CancelAfter(VelvetTaskCompletionSource gate)
        {
            await gate.Task;
            throw new OperationCanceledException("after suspending");
        }

        static async VelvetTask<int> CancelWithResultAfter(VelvetTaskCompletionSource gate)
        {
            await gate.Task;
            throw new OperationCanceledException("after suspending");
        }

        [Test]
        public void Given_ACompletionSource_When_AnOperationCanceledExceptionIsSetAsItsException_Then_ItsTaskIsFaulted()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();

            // Act
            source.SetException(new OperationCanceledException("fault"));

            // Assert
            Assert.That(source.Task.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_AResultCompletionSource_When_AnOperationCanceledExceptionIsSetAsItsException_Then_ItsTaskIsFaulted()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();

            // Act
            source.SetException(new OperationCanceledException("fault"));

            // Assert
            Assert.That(source.Task.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_AnOperationCanceledException_When_ATaskIsMadeFromItAsAnException_Then_TheTaskIsFaulted()
        {
            // Arrange
            var exception = new OperationCanceledException("fault");

            // Act
            var task = VelvetTask.FromException(exception);

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_AnOperationCanceledException_When_AResultTaskIsMadeFromItAsAnException_Then_TheTaskIsFaulted()
        {
            // Arrange
            var exception = new OperationCanceledException("fault");

            // Act
            var task = VelvetTask.FromException<int>(exception);

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        // GREEN_ON_BASE(characterization): an async method's thrown cancellation already cancelled its task on the base.
        [Test]
        public void Given_AnAsyncMethodSuspended_When_ItThrowsAnOperationCanceledException_Then_ItsTaskIsCanceled()
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource();
            var task = CancelAfter(gate);

            // Act
            gate.SetResult();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Canceled));
        }

        // GREEN_ON_BASE(characterization): an async method's thrown cancellation already cancelled its task on the base.
        [Test]
        public void Given_AnAsyncMethodWithAResultSuspended_When_ItThrowsAnOperationCanceledException_Then_ItsTaskIsCanceled()
        {
            // Arrange
            var gate = new VelvetTaskCompletionSource();
            var task = CancelWithResultAfter(gate);

            // Act
            gate.SetResult();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Canceled));
        }

        [Test]
        public void Given_ATaskFaultedWithAnOperationCanceledException_When_ItIsPreserved_Then_ThePreservedTaskIsFaulted()
        {
            // Arrange
            var task = VelvetTask.FromException(new OperationCanceledException("fault"));

            // Act
            var preserved = task.Preserve();

            // Assert
            Assert.That(preserved.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_ACanceledMemberBeforeOneFaultedWithAnOperationCanceledException_When_CombinedByWhenAll_Then_TheCombinationIsFaulted()
        {
            // Arrange
            var canceled = new VelvetTaskCompletionSource();
            canceled.SetCanceled();
            var faulted = VelvetTask.FromException(new OperationCanceledException("fault"));

            // Act
            var all = VelvetTask.WhenAll(canceled.Task, faulted);

            // Assert
            Assert.That(all.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        // GREEN_ON_BASE(characterization): a cancelled member already cancelled the combination on the base.
        [Test]
        public void Given_ACanceledMember_When_CombinedByWhenAll_Then_TheCombinationIsCanceled()
        {
            // Arrange
            var canceled = new VelvetTaskCompletionSource();
            canceled.SetCanceled();

            // Act
            var all = VelvetTask.WhenAll(canceled.Task, VelvetTask.CompletedTask);

            // Assert
            Assert.That(all.Status, Is.EqualTo(VelvetTaskStatus.Canceled));
        }

        [Test]
        public void Given_AnAttachedTask_When_TheTaskItWaitsForFaultsWithAnOperationCanceledException_Then_TheAttachedTaskIsFaulted()
        {
            // Arrange
            using var cts = new System.Threading.CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token);

            // Act
            source.SetException(new OperationCanceledException("fault"));

            // Assert
            Assert.That(attached.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        // GREEN_ON_BASE(characterization): a cancelled awaited task already cancelled the attached one on the base.
        [Test]
        public void Given_AnAttachedTask_When_TheTaskItWaitsForIsCanceled_Then_TheAttachedTaskIsCanceled()
        {
            // Arrange
            using var cts = new System.Threading.CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token);

            // Act
            source.SetCanceled();

            // Assert
            Assert.That(attached.Status, Is.EqualTo(VelvetTaskStatus.Canceled));
        }
    }
}
