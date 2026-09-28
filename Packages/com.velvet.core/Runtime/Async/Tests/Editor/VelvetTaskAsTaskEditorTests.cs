using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskAsTaskEditorTests
    {
        static void OnAnotherThread(Action action)
        {
            var thread = new Thread(() => action());
            thread.Start();
            thread.Join();
        }

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
            var status = task.Status;
            var carriesTheToken = false;
            if (status == TaskStatus.Canceled)
            {
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException exception)
                {
                    carriesTheToken = exception.CancellationToken == cancellation.Token;
                }
            }

            // Assert
            Assert.That((status, carriesTheToken), Is.EqualTo((TaskStatus.Canceled, true)));
        }

        [Test]
        public void Given_AResultTaskTakenAsATaskOnTheMainThread_When_ItsSourceCompletesOnAnotherThread_Then_TheTaskCompletesWithoutTheMainThread()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var task = source.Task.AsTask();
            OnAnotherThread(() => source.SetResult(5));

            // Act
            var completed = task.Wait(TimeSpan.FromSeconds(1));

            // Assert
            Assert.That((completed, completed ? task.Result : 0), Is.EqualTo((true, 5)));
        }
    }
}
