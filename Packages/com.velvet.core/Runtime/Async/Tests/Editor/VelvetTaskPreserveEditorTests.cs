using System;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskPreserveEditorTests
    {
        static void OnAnotherThread(Action action)
        {
            var thread = new Thread(() => action());
            thread.Start();
            thread.Join();
        }

        static async VelvetTask<int> PlusWhenSettled(VelvetTask<int> task, int addend) => await task + addend;

        [Test]
        public void Given_APreservedPendingTask_When_TwoAsyncMethodsAwaitItAndItCompletes_Then_BothResumeWithItsResult()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var preserved = source.Task.Preserve();
            var first = PlusWhenSettled(preserved, 1);
            var second = PlusWhenSettled(preserved, 2);

            // Act
            source.SetResult(10);

            // Assert
            Assert.That((first.GetAwaiter().GetResult(), second.GetAwaiter().GetResult()), Is.EqualTo((11, 12)));
        }

        [Test]
        public void Given_APreservedPendingTask_When_ItsSourceCompletes_Then_ItsStatusMovesFromPendingToSucceeded()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var preserved = source.Task.Preserve();
            var statusBeforeCompletion = preserved.Status;

            // Act
            source.SetResult();

            // Assert
            Assert.That((statusBeforeCompletion, preserved.Status),
                Is.EqualTo((VelvetTaskStatus.Pending, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_APreservedCompletedTask_When_ReadTwice_Then_EachReadReturnsItsResult()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource<int>();
            var preserved = source.Task.Preserve();
            source.SetResult(7);

            // Act
            var first = preserved.GetAwaiter().GetResult();
            var second = preserved.GetAwaiter().GetResult();

            // Assert
            Assert.That((first, second), Is.EqualTo((7, 7)));
        }

        [Test]
        public void Given_APreservedFaultedTask_When_ReadTwice_Then_EachReadThrowsItsFault()
        {
            // Arrange
            var fault = new InvalidOperationException("boom");
            var preserved = VelvetTask.FromException(fault).Preserve();

            // Act
            var first = Assert.Throws<InvalidOperationException>(() => preserved.GetAwaiter().GetResult());
            var second = Assert.Throws<InvalidOperationException>(() => preserved.GetAwaiter().GetResult());

            // Assert
            Assert.That((first, second), Is.EqualTo((fault, fault)));
        }

        [Test]
        public void Given_APreservedCanceledTask_When_Read_Then_ThrowsWithItsToken()
        {
            // Arrange
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var source = new VelvetTaskCompletionSource();
            var preserved = source.Task.Preserve();
            source.SetCanceled(cancellation.Token);

            // Act
            var thrown = Assert.Throws<OperationCanceledException>(() => preserved.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Given_APreservedCombinationOfTwoFaults_When_TakenAsATask_Then_ItsExceptionHoldsBoth()
        {
            // Arrange
            var first = new InvalidOperationException("first");
            var second = new ArgumentException("second");
            var preserved = VelvetTask.WhenAll(VelvetTask.FromException(first), VelvetTask.FromException(second)).Preserve();

            // Act
            var task = preserved.AsTask();

            // Assert
            Assert.That(task.Exception!.InnerExceptions, Is.EqualTo(new Exception[] { first, second }));
        }

        [Test]
        public void Given_ATaskPreservedOnAnotherThread_When_ItsSourceCompletesOnAnotherThread_Then_AnAwaitRegisteredOnTheMainThreadRunsThereOnceTheHandoffsRun()
        {
            // Arrange
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var source = new VelvetTaskCompletionSource<int>();
            VelvetTask<int> preserved = default;
            OnAnotherThread(() => preserved = source.Task.Preserve());
            var ranOnThread = 0;
            preserved.GetAwaiter().OnCompleted(() => ranOnThread = Thread.CurrentThread.ManagedThreadId);
            OnAnotherThread(() => source.SetResult(1));
            var ranBeforeTheHandoffs = ranOnThread;

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That((ranBeforeTheHandoffs, ranOnThread == mainThreadId), Is.EqualTo((0, true)));
        }
    }
}
