using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskAsTaskEditorTests
    {
        [Test]
        public void Given_APendingResultTask_When_TakenAsATaskAndItCompletes_Then_TheTaskCarriesItsResult()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var task = source.Task.AsTask();
            var completedBefore = task.IsCompleted;

            // Act
            source.SetResult(5);

            // Assert
            var result = task.Status == TaskStatus.RanToCompletion ? task.Result : 0;
            Assert.That((completedBefore, result), Is.EqualTo((false, 5)));
        }

        [Test]
        public void Given_AFaultedTask_When_TakenAsATask_Then_TheTaskHoldsThatFault()
        {
            // Arrange
            var fault = new InvalidOperationException("boom");

            // Act
            var task = VelvetTask.FromException(fault).AsTask();

            // Assert
            Assert.That(task.Exception!.InnerExceptions, Is.EqualTo(new Exception[] { fault }));
        }

        [Test]
        public void Given_ACanceledResultTask_When_TakenAsATask_Then_TheTaskIsCanceledWithItsToken()
        {
            // Arrange
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var source = new VelvetTaskCompletionSource<int>();
            source.SetCanceled(cancellation.Token);

            // Act
            var task = source.Task.AsTask();
            OperationCanceledException? thrown = null;
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                thrown = exception;
            }

            // Assert
            Assert.That((task.IsCanceled, thrown?.CancellationToken == cancellation.Token), Is.EqualTo((true, true)));
        }
    }
}
