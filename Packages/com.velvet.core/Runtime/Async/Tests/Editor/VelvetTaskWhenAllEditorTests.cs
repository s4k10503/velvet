using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskWhenAllEditorTests
    {
        static readonly FieldInfo ResultTaskSourceField =
            typeof(VelvetTask<int[]>).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static async VelvetTask<int[]> AwaitBounded(VelvetTask<int[]> task) => await task.Bounded();

        static async VelvetTask Await(VelvetTask task) => await task;

        static async VelvetTask<int> DoubledWhenSettled(VelvetTaskCompletionSource<int> source) =>
            await source.Task * 2;

        static VelvetTaskSource<int[]> PublishingSourceOf(VelvetTask<int[]> task)
        {
            var composite = ResultTaskSourceField.GetValue(task)!;
            var inner = composite.GetType().GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (VelvetTaskSource<int[]>)inner.GetValue(composite)!;
        }

        static int CompletionCountOf(VelvetTask<int[]> task)
        {
            var core = typeof(VelvetTaskSource<int[]>)
                .GetField("_core", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(PublishingSourceOf(task))!;
            return (int)core.GetType()
                .GetField("_completedCount", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(core)!;
        }

        [Test]
        public void Given_TwoPendingTasks_When_OnlyOneCompletes_Then_WhenAllStaysPending()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var all = VelvetTask.WhenAll(first.Task, second.Task);

            // Act
            first.SetResult();
            var settled = first.Task.Status;
            var afterFirst = all.Status;

            // Assert
            Assert.That((settled, afterFirst), Is.EqualTo((VelvetTaskStatus.Succeeded, VelvetTaskStatus.Pending)));
        }

        [Test]
        public void Given_TwoPendingTasks_When_BothComplete_Then_WhenAllSucceeds()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var all = VelvetTask.WhenAll(first.Task, second.Task);

            // Act
            first.SetResult();
            second.SetResult();

            // Assert
            Assert.That(all.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_TwoPendingResultTasks_When_TheyCompleteOutOfOrder_Then_ResultsFollowArgumentOrder()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            var all = VelvetTask.WhenAll(first.Task, second.Task);

            // Act
            second.SetResult(20);
            first.SetResult(10);
            var results = all.GetAwaiter().GetResult();

            // Assert
            Assert.That(results, Is.EqualTo(new[] { 10, 20 }));
        }

        [Test]
        public void Given_NoTasks_When_WhenAllCalled_Then_CompletesWithoutWaiting()
        {
            // Arrange
            var tasks = Array.Empty<VelvetTask>();

            // Act
            var all = VelvetTask.WhenAll(tasks);

            // Assert
            Assert.That(all.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_NoResultTasks_When_WhenAllCalled_Then_CompletesWithAnEmptyArray()
        {
            // Arrange
            var tasks = Array.Empty<VelvetTask<int>>();

            // Act
            var results = VelvetTask.WhenAll(tasks).GetAwaiter().GetResult();

            // Assert
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void Given_TwoPendingTasks_When_OnlyOneFaults_Then_WhenAllStaysPending()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var all = VelvetTask.WhenAll(first.Task, second.Task);

            // Act
            first.SetException(new InvalidOperationException("boom"));
            var settled = first.Task.Status;
            var afterFault = all.Status;

            // Assert
            Assert.That((settled, afterFault), Is.EqualTo((VelvetTaskStatus.Faulted, VelvetTaskStatus.Pending)));
        }

        [Test]
        public void Given_TwoAlreadyFaultedTasks_When_WhenAllIsConsumed_Then_ThrowsTheFirstArgumentsFault()
        {
            // Arrange
            var first = new InvalidOperationException("first");
            var second = new InvalidOperationException("second");
            var all = VelvetTask.WhenAll(VelvetTask.FromException(first), VelvetTask.FromException(second));

            // Act
            var thrown = Assert.Throws<InvalidOperationException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(first));
        }

        [Test]
        public void Given_TwoPendingTasks_When_TheSecondFaultsFirst_Then_ThrowsTheFaultOfTheFirstArgument()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var all = VelvetTask.WhenAll(first.Task, second.Task);
            var fromFirstArgument = new InvalidOperationException("first argument");

            // Act
            second.SetException(new InvalidOperationException("second argument"));
            first.SetException(fromFirstArgument);
            var thrown = Assert.Throws<InvalidOperationException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(fromFirstArgument));
        }

        [Test]
        public void Given_TwoPendingTasks_When_TheSecondCancelsFirst_Then_ThrowsWithTheTokenOfTheFirstArgument()
        {
            // Arrange
            using var fromFirstArgument = new CancellationTokenSource();
            using var fromSecondArgument = new CancellationTokenSource();
            fromFirstArgument.Cancel();
            fromSecondArgument.Cancel();
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            var all = VelvetTask.WhenAll(first.Task, second.Task);

            // Act
            second.SetCanceled(fromSecondArgument.Token);
            first.SetCanceled(fromFirstArgument.Token);
            var thrown = Assert.Throws<OperationCanceledException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(fromFirstArgument.Token));
        }

        [Test]
        public void Given_ACanceledTaskBeforeAFaultedOne_When_WhenAllIsConsumed_Then_ThrowsTheFault()
        {
            // Arrange
            var canceled = new VelvetTaskCompletionSource();
            canceled.SetCanceled();
            var fault = new InvalidOperationException("fault");
            var all = VelvetTask.WhenAll(canceled.Task, VelvetTask.FromException(fault));

            // Act
            var thrown = Assert.Throws<InvalidOperationException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(fault));
        }

        [Test]
        public void Given_ACanceledTask_When_WhenAllIsConsumed_Then_ThrowsWithThatMembersToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceled = new VelvetTaskCompletionSource();
            canceled.SetCanceled(cts.Token);
            var all = VelvetTask.WhenAll(canceled.Task, VelvetTask.CompletedTask);

            // Act
            var thrown = Assert.Throws<OperationCanceledException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void Given_ACanceledResultTask_When_WhenAllIsConsumed_Then_ThrowsWithThatMembersToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceled = new VelvetTaskCompletionSource<int>();
            canceled.SetCanceled(cts.Token);
            var all = VelvetTask.WhenAll(canceled.Task, VelvetTask.FromResult(7));

            // Act
            var thrown = Assert.Throws<OperationCanceledException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void Given_AFaultedResultTask_When_WhenAllIsConsumed_Then_ThrowsThatFault()
        {
            // Arrange
            var fault = new InvalidOperationException("result fault");
            var all = VelvetTask.WhenAll(VelvetTask.FromException<int>(fault), VelvetTask.FromResult(7));

            // Act
            var thrown = Assert.Throws<InvalidOperationException>(() => all.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(fault));
        }

        [Test]
        public void Given_AnAlreadyCompletedTaskBesideAPendingOne_When_ThePendingOneCompletes_Then_CarriesBothResults()
        {
            // Arrange
            var ready = new VelvetTaskCompletionSource<int>();
            ready.SetResult(10);
            var pending = new VelvetTaskCompletionSource<int>();
            var all = VelvetTask.WhenAll(ready.Task, pending.Task);

            // Act
            var beforePending = all.Status;
            pending.SetResult(20);
            var results = string.Join(",", all.GetAwaiter().GetResult());

            // Assert
            Assert.That((beforePending, results), Is.EqualTo((VelvetTaskStatus.Pending, "10,20")));
        }

        [Test]
        public void Given_AWhenAllAwaitedBeforeItsLastMember_When_ThatMemberCompletes_Then_TheAwaitResumes()
        {
            // Arrange
            var early = new VelvetTaskCompletionSource<int>();
            var late = new VelvetTaskCompletionSource<int>();
            var awaited = AwaitBounded(VelvetTask.WhenAll(early.Task, late.Task));
            early.SetResult(10);

            // Act
            var beforeLate = awaited.Status;
            late.SetResult(20);
            var results = string.Join(",", awaited.GetAwaiter().GetResult());

            // Assert
            Assert.That((beforeLate, results), Is.EqualTo((VelvetTaskStatus.Pending, "10,20")));
        }

        [Test]
        public void Given_AWhenAllCarryingNothingAwaitedBeforeItsLastMember_When_ThatMemberCompletes_Then_TheAwaitResumes()
        {
            // Arrange
            var early = new VelvetTaskCompletionSource();
            var late = new VelvetTaskCompletionSource();

            // A status read never registers a continuation on the combination, so a case built on the
            // combination's own status stays green with that registration dead. The await is plain
            // rather than Bounded, whose registration goes through AttachExternalCancellation and
            // would vanish if that call took its completed-task path.
            var awaited = Await(VelvetTask.WhenAll(early.Task, late.Task));
            early.SetResult();

            // Act
            var beforeLate = awaited.Status;
            late.SetResult();
            var afterLate = awaited.Status;

            // Assert
            Assert.That((beforeLate, afterLate),
                Is.EqualTo((VelvetTaskStatus.Pending, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_TwoFailingResultTasks_When_CombinedByWhenAll_Then_TheCombinationIsCompletedOnce()
        {
            // Arrange
            var canceled = new VelvetTaskCompletionSource<int>();
            canceled.SetCanceled();
            var faulted = VelvetTask.FromException<int>(new InvalidOperationException("boom"));

            // Act
            var all = VelvetTask.WhenAll(canceled.Task, faulted);
            var completions = CompletionCountOf(all);

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        [Test]
        public void Given_AConsumedWhenAllResult_When_ConsumedAgain_Then_ThrowsAlreadyConsumed()
        {
            // Arrange
            var all = VelvetTask.WhenAll(VelvetTask.FromResult(1), VelvetTask.FromResult(2));
            all.GetAwaiter().GetResult();

            // Act
            void SecondConsume() => all.GetAwaiter().GetResult();

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(SecondConsume)!.Message,
                Is.EqualTo("The VelvetTask has already been consumed."));
        }

        [Test]
        public void Given_OnePendingTaskPassedTwice_When_WhenAllCalled_Then_ThrowsAlreadyAwaited()
        {
            // Arrange
            var shared = new VelvetTaskCompletionSource();

            // Act
            void PassTwice() => VelvetTask.WhenAll(shared.Task, shared.Task);

            // Assert
            Assert.That(Assert.Throws<InvalidOperationException>(PassTwice)!.Message,
                Is.EqualTo("The VelvetTask has already been awaited."));
        }

        [Test]
        public void Given_ACompletedTaskPassedTwiceBehindAPendingOne_When_WhenAllCalled_Then_ThrowsAlreadyConsumedWithThePendingOneAwaited()
        {
            // Arrange
            var ahead = new VelvetTaskCompletionSource();
            var duplicated = VelvetTask.FromException(new InvalidOperationException("boom"));

            // Act
            var fromCall = Assert.Throws<InvalidOperationException>(
                () => VelvetTask.WhenAll(ahead.Task, duplicated, duplicated))!.Message;
            var fromAhead = Assert.Throws<InvalidOperationException>(
                () => ahead.Task.GetAwaiter().OnCompleted(() => { }))!.Message;

            // Assert
            Assert.That((fromCall, fromAhead), Is.EqualTo((
                "The VelvetTask has already been consumed.",
                "The VelvetTask has already been awaited.")));
        }

        [Test]
        public void Given_ACompletedResultTaskPassedTwiceBehindAPendingOne_When_WhenAllCalled_Then_ThrowsAlreadyConsumedWithThePendingOneAwaited()
        {
            // Arrange
            var ahead = new VelvetTaskCompletionSource<int>();
            var duplicated = VelvetTask.FromException<int>(new InvalidOperationException("boom"));

            // Act
            var fromCall = Assert.Throws<InvalidOperationException>(
                () => VelvetTask.WhenAll(ahead.Task, duplicated, duplicated))!.Message;
            var fromAhead = Assert.Throws<InvalidOperationException>(
                () => ahead.Task.GetAwaiter().OnCompleted(() => { }))!.Message;

            // Assert
            Assert.That((fromCall, fromAhead), Is.EqualTo((
                "The VelvetTask has already been consumed.",
                "The VelvetTask has already been awaited.")));
        }

        [Test]
        public void Given_TwoSuspendedAsyncMethods_When_TheyCompleteOutOfOrder_Then_ResultsFollowArgumentOrder()
        {
            // Arrange
            var first = new VelvetTaskCompletionSource<int>();
            var second = new VelvetTaskCompletionSource<int>();
            var all = VelvetTask.WhenAll(DoubledWhenSettled(first), DoubledWhenSettled(second));

            // Act
            second.SetResult(20);
            first.SetResult(10);
            var results = all.GetAwaiter().GetResult();

            // Assert
            Assert.That(results, Is.EqualTo(new[] { 20, 40 }));
        }

        [Test]
        public void Given_OneValueBackedTaskPassedTwice_When_WhenAllCalled_Then_CarriesItAtBothPositions()
        {
            // Arrange
            var shared = VelvetTask.FromResult(7);

            // Act
            var results = VelvetTask.WhenAll(shared, shared).GetAwaiter().GetResult();

            // Assert
            Assert.That(results, Is.EqualTo(new[] { 7, 7 }));
        }

        [Test]
        public void Given_ANullTaskArray_When_WhenAllCalled_Then_ThrowsArgumentNullException()
        {
            // Arrange
            VelvetTask[] tasks = null!;

            // Act
            void Call() => VelvetTask.WhenAll(tasks);

            // Assert
            Assert.That(Call, Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void Given_ANullResultTaskArray_When_WhenAllCalled_Then_ThrowsArgumentNullException()
        {
            // Arrange
            VelvetTask<int>[] tasks = null!;

            // Act
            void Call() => VelvetTask.WhenAll(tasks);

            // Assert
            Assert.That(Call, Throws.InstanceOf<ArgumentNullException>());
        }
    }
}
