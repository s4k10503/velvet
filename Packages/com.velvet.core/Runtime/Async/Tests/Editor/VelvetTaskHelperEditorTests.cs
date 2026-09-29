using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskHelperEditorTests
    {
        sealed class UpstreamCanceledException : OperationCanceledException
        {
            public UpstreamCanceledException(string message) : base(message)
            {
            }
        }

        static string ExceptionsLoggedDuring(Action action)
        {
            var logged = new List<string>();
            void Record(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception)
                {
                    logged.Add(condition);
                }
            }

            Application.logMessageReceived += Record;
            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= Record;
            }

            return string.Join(" | ", logged);
        }

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

        // GREEN_ON_BASE(characterization): an attached task settling with what it waits for is the base's behaviour.
        [Test]
        public void Given_AnAttachedTask_When_TheTaskItWaitsForSucceeds_Then_TheAttachedTaskSucceeds()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token);

            // Act
            source.SetResult();

            // Assert
            Assert.That(attached.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        // GREEN_ON_BASE(characterization): the base's attached result task carries the result it waited for.
        [Test]
        public void Given_AnAttachedResultTask_When_TheTaskItWaitsForSucceeds_Then_TheAttachedTaskCarriesItsResult()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource<int>();
            var attached = source.Task.AttachExternalCancellation(cts.Token);

            // Act
            source.SetResult(7);

            // Assert
            Assert.That(attached.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public void Given_AnAlreadyCancelledToken_When_AttachedToAFaultedTask_Then_TheTaskKeepsItsFault()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var faulted = VelvetTask.FromException(new InvalidOperationException("boom"));

            // Act
            var attached = faulted.AttachExternalCancellation(cts.Token);

            // Assert
            Assert.That(attached.Status, Is.EqualTo(VelvetTaskStatus.Faulted));
        }

        [Test]
        public void Given_AnAlreadyCancelledToken_When_AttachedToACompletedResultTask_Then_TheTaskKeepsItsResult()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var attached = VelvetTask.FromResult(7).AttachExternalCancellation(cts.Token);

            // Assert
            Assert.That(attached.GetAwaiter().GetResult(), Is.EqualTo(7));
        }

        [Test]
        public void Given_AnAttachedTask_When_TheTaskItWaitsForIsCancelled_Then_ItsCancellationCarriesThatTasksToken()
        {
            // Arrange
            using var attachedCts = new CancellationTokenSource();
            using var ownCts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(attachedCts.Token);

            // Act
            source.SetCanceled(ownCts.Token);

            // Assert
            Assert.That(Assert.Throws<OperationCanceledException>(() => attached.GetAwaiter().GetResult())!.CancellationToken,
                Is.EqualTo(ownCts.Token));
        }

        [Test]
        public void Given_AnAttachedResultTask_When_TheTaskItWaitsForIsCancelled_Then_ItsCancellationCarriesThatTasksToken()
        {
            // Arrange
            using var attachedCts = new CancellationTokenSource();
            using var ownCts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource<int>();
            var attached = source.Task.AttachExternalCancellation(attachedCts.Token);

            // Act
            source.SetCanceled(ownCts.Token);

            // Assert
            Assert.That(Assert.Throws<OperationCanceledException>(() => attached.GetAwaiter().GetResult())!.CancellationToken,
                Is.EqualTo(ownCts.Token));
        }

        [Test]
        public void Given_AnAttachedTask_When_TheTaskItWaitsForIsCancelledWithItsOwnException_Then_TheAttachedTaskThrowsThatException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token);
            var cancellation = new UpstreamCanceledException("upstream");

            // Act
            source.SetException(cancellation);

            // Assert
            Assert.That(Assert.Catch(() => attached.GetAwaiter().GetResult()), Is.SameAs(cancellation));
        }

        [Test]
        public void Given_AnAttachedTaskTakenAsATaskOnTheMainThread_When_AnotherThreadCompletesTheTaskItWaitsFor_Then_AWaitThereReturns()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token).AsTask();

            // Act
            ThreadPool.QueueUserWorkItem(_ => source.SetResult());
            var completed = attached.Wait(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_AnAttachedTaskItsTokenCancelled_When_TheTaskItWaitedForFailsLater_Then_TheFaultIsLogged()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            using var cts = new CancellationTokenSource();
            var source = new VelvetTaskCompletionSource();
            var attached = source.Task.AttachExternalCancellation(cts.Token);
            cts.Cancel();

            // Act
            var logged = ExceptionsLoggedDuring(() => source.SetException(new InvalidOperationException("late")));

            // Assert
            Assert.That((attached.Status, logged), Is.EqualTo((VelvetTaskStatus.Canceled, "InvalidOperationException: late")));
        }

        [Test]
        public void Given_AnAttachedCombination_When_TwoOfItsMembersFail_Then_TheAttachedTaskKeepsBothFaults()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var firstFault = new InvalidOperationException("first");
            var secondFault = new ArgumentException("second");
            var attached = VelvetTask.WhenAll(first.Task, second.Task).AttachExternalCancellation(cts.Token).AsTask();

            // Act
            first.SetException(firstFault);
            second.SetException(secondFault);

            // Assert
            Assert.That(attached.Exception?.InnerExceptions, Is.EqualTo(new Exception[] { firstFault, secondFault }));
        }

        [Test]
        public void Given_AnAttachedResultCombination_When_TwoOfItsMembersFail_Then_TheAttachedTaskKeepsBothFaults()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            var firstFault = new InvalidOperationException("first");
            var secondFault = new ArgumentException("second");
            var attached = VelvetTask.WhenAll(first.Task, second.Task).AttachExternalCancellation(cts.Token).AsTask();

            // Act
            first.SetException(firstFault);
            second.SetException(secondFault);

            // Assert
            Assert.That(attached.Exception?.InnerExceptions, Is.EqualTo(new Exception[] { firstFault, secondFault }));
        }

        [Test]
        public void Given_AForgottenPendingCombination_When_TwoOfItsMembersFail_Then_BothFaultsAreLogged()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            VelvetTask.WhenAll(first.Task, second.Task).Forget();

            // Act
            var logged = ExceptionsLoggedDuring(() =>
            {
                first.SetException(new InvalidOperationException("first"));
                second.SetException(new ArgumentException("second"));
            });

            // Assert
            Assert.That(logged, Is.EqualTo("InvalidOperationException: first | ArgumentException: second"));
        }

        [Test]
        public void Given_AFailedResultCombination_When_Forgotten_Then_BothFaultsAreLogged()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            var combination = VelvetTask.WhenAll(
                VelvetTask.FromException<int>(new InvalidOperationException("first")),
                VelvetTask.FromException<int>(new ArgumentException("second")));

            // Act
            var logged = ExceptionsLoggedDuring(() => combination.Forget());

            // Assert
            Assert.That(logged, Is.EqualTo("InvalidOperationException: first | ArgumentException: second"));
        }

        [Test]
        public void Given_ACancelledTask_When_Forgotten_Then_NothingIsLogged()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            var source = new VelvetTaskCompletionSource();
            source.SetCanceled();
            var status = source.Task.Status;

            // Act
            var logged = ExceptionsLoggedDuring(() => source.Task.Forget());

            // Assert
            Assert.That((status, logged), Is.EqualTo((VelvetTaskStatus.Canceled, "")));
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
