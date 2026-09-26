using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskMainThreadEditorTests
    {
        const int ConcurrentChains = 20000;
        const long SettleTimeoutMilliseconds = 30000;

        static readonly FieldInfo VoidTaskSourceField =
            typeof(VelvetTask).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static readonly FieldInfo IntTaskSourceField =
            typeof(VelvetTask<int>).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static int s_mainThreadId;
        static int s_resumedOffTheMainThread;
        static int s_settled;
        static int s_faulted;

        [SetUp]
        public void SetUp() => s_mainThreadId = Thread.CurrentThread.ManagedThreadId;

        [TearDown]
        public void TearDown()
        {
            VelvetMainThread.RunHandoffs();
#if UNITY_EDITOR
            DrainEditorUpdateForTest();
#endif
        }

        static void OnAnotherThread(Action action)
        {
            var thread = new Thread(() => action());
            thread.Start();
            thread.Join();
        }

        static async VelvetTask<int> SwitchToMainThreadThenReadThread()
        {
            await VelvetTask.SwitchToMainThread();
            return Thread.CurrentThread.ManagedThreadId;
        }

        static async VelvetTask<int> YieldThenReturn()
        {
            await VelvetTask.Yield();
            return 1;
        }

        static async VelvetTask<int> ResumeOffTheMainThread(Task<int> gate)
        {
            var value = await gate.ConfigureAwait(false);
            return value + 1;
        }

        static async VelvetTask AwaitOneThatResumesOffTheMainThread(Task<int> gate)
        {
            try
            {
                await ResumeOffTheMainThread(gate);
                if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
                {
                    Interlocked.Increment(ref s_resumedOffTheMainThread);
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref s_faulted);
            }
            finally
            {
                Interlocked.Increment(ref s_settled);
            }
        }

        [Test]
        public void Given_AContinuationRegisteredOnTheMainThread_When_ItsSourceCompletesOnAnotherThread_Then_ItRunsOnTheMainThreadOnceTheHandoffsRun()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var ranOnThread = 0;
            source.Task.GetAwaiter().OnCompleted(() => ranOnThread = Thread.CurrentThread.ManagedThreadId);
            OnAnotherThread(() => source.SetResult());
            var ranBeforeTheHandoffs = ranOnThread;

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That((ranBeforeTheHandoffs, ranOnThread == s_mainThreadId), Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_AContinuationRegisteredOnAnotherThread_When_ItsSourceCompletesOnAnotherThread_Then_ItRunsWithoutWaitingForTheMainThread()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var ran = false;
            OnAnotherThread(() => source.Task.GetAwaiter().OnCompleted(() => ran = true));

            // Act
            OnAnotherThread(() => source.SetResult());

            // Assert
            Assert.That(ran, Is.True);
        }

        [Test]
        public void Given_AnAsyncMethodAwaitingOneThatResumesOffTheMainThread_When_TwentyThousandRunAtOnce_Then_EachResumesOnTheMainThreadAndNoneFaults()
        {
            // Arrange
            s_resumedOffTheMainThread = 0;
            s_settled = 0;
            s_faulted = 0;

            // Act
            for (var i = 0; i < ConcurrentChains; i++)
            {
                var gate = new TaskCompletionSource<int>();
                AwaitOneThatResumesOffTheMainThread(gate.Task).Forget();
                ThreadPool.QueueUserWorkItem(static state => ((TaskCompletionSource<int>)state!).SetResult(1), gate);
                VelvetMainThread.RunHandoffs();
            }

            var elapsed = Stopwatch.StartNew();
            while (Volatile.Read(ref s_settled) < ConcurrentChains && elapsed.ElapsedMilliseconds < SettleTimeoutMilliseconds)
            {
                VelvetMainThread.RunHandoffs();
            }

            // Assert
            Assert.That((s_settled, s_faulted, s_resumedOffTheMainThread), Is.EqualTo((ConcurrentChains, 0, 0)));
        }

        [Test]
        public void Given_AMainThreadPoolHoldingAnItem_When_AnotherThreadRents_Then_TheItemStaysForTheMainThread()
        {
            // Arrange
            var pool = new MainThreadPool<object>();
            var item = new object();
            pool.Return(item);
            var rentedElsewhere = true;

            // Act
            OnAnotherThread(() => rentedElsewhere = pool.Rent() != null);

            // Assert
            Assert.That((rentedElsewhere, ReferenceEquals(pool.Rent(), item)), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AMainThreadPoolAtItsCap_When_OneMoreIsReturned_Then_ItIsDropped()
        {
            // Arrange
            var pool = new MainThreadPool<object>();
            var cap = (int)typeof(MainThreadPool<object>)
                .GetField("MaxPoolSize", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetRawConstantValue();
            for (var i = 0; i < cap; i++)
            {
                pool.Return(new object());
            }

            // Act
            pool.Return(new object());

            // Assert
            var rented = 0;
            while (pool.Rent() != null)
            {
                rented++;
            }

            Assert.That(rented, Is.EqualTo(cap));
        }

        // GREEN_ON_BASE(characterization): the base pools a consumed source on the main thread too, and
        // moving the pool behind a thread check must not stop that.
        [Test]
        public void Given_AConsumedFaultedTask_When_TheMainThreadFaultsAnother_Then_ItReusesTheSource()
        {
            // Arrange
            var first = VelvetTask.FromException(new InvalidOperationException("first"));
            var firstSource = VoidTaskSourceField.GetValue(first);
            try
            {
                first.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            var second = VelvetTask.FromException(new InvalidOperationException("second"));

            // Assert
            Assert.That(VoidTaskSourceField.GetValue(second), Is.SameAs(firstSource));
        }

        // GREEN_ON_BASE(characterization): the base pools a consumed value-carrying source on the main
        // thread too, and moving the pool behind a thread check must not stop that.
        [Test]
        public void Given_AConsumedFaultedValueTask_When_TheMainThreadFaultsAnother_Then_ItReusesTheSource()
        {
            // Arrange
            var first = VelvetTask.FromException<int>(new InvalidOperationException("first"));
            var firstSource = IntTaskSourceField.GetValue(first);
            try
            {
                first.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            var second = VelvetTask.FromException<int>(new InvalidOperationException("second"));

            // Assert
            Assert.That(IntTaskSourceField.GetValue(second), Is.SameAs(firstSource));
        }

        // GREEN_ON_BASE(characterization): the base pools a consumed state-machine runner on the main
        // thread too, and moving the pool behind a thread check must not stop that.
        [Test]
        public void Given_AConsumedSuspendedAsyncMethod_When_TheMainThreadSuspendsItAgain_Then_ItReusesTheRunner()
        {
            // Arrange
            var first = YieldThenReturn();
            var firstRunner = IntTaskSourceField.GetValue(first);
#if UNITY_EDITOR
            DrainEditorUpdateForTest();
#endif
            first.GetAwaiter().GetResult();

            // Act
            var second = YieldThenReturn();

            // Assert
            Assert.That(IntTaskSourceField.GetValue(second), Is.SameAs(firstRunner));
        }

        [Test]
        public void Given_AMainThreadPool_When_AnotherThreadReturnsAnItem_Then_TheMainThreadDoesNotRentIt()
        {
            // Arrange
            var pool = new MainThreadPool<object>();

            // Act
            OnAnotherThread(() => pool.Return(new object()));

            // Assert
            Assert.That(pool.Rent(), Is.Null);
        }

        [Test]
        public void Given_AnotherThread_When_ItCallsYield_Then_ItIsRefusedWithTheWayBack()
        {
            // Arrange
            Exception? thrown = null;

            // Act
            OnAnotherThread(() =>
            {
                try
                {
                    VelvetTask.Yield();
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            });

            // Assert
            Assert.That(thrown, Is.TypeOf<InvalidOperationException>().And.Message.Contains("VelvetTask.SwitchToMainThread()"));
        }

        [Test]
        public void Given_TheMainThread_When_AnAsyncMethodAwaitsSwitchToMainThread_Then_ItCompletesWithoutSuspending()
        {
            // Arrange
            Func<VelvetTask<int>> start = SwitchToMainThreadThenReadThread;

            // Act
            var task = start();

            // Assert
            Assert.That(task.Status, Is.EqualTo(VelvetTaskStatus.Succeeded));
        }

        [Test]
        public void Given_AnAsyncMethodStartedOnAnotherThread_When_ItAwaitsSwitchToMainThread_Then_ItResumesOnTheMainThreadOnceTheHandoffsRun()
        {
            // Arrange
            VelvetTask<int> task = default;
            OnAnotherThread(() => task = SwitchToMainThreadThenReadThread());
            var statusBeforeTheHandoffs = task.Status;

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            var resumedOnTheMainThread = task.GetAwaiter().GetResult() == s_mainThreadId;
            Assert.That((statusBeforeTheHandoffs, resumedOnTheMainThread), Is.EqualTo((VelvetTaskStatus.Pending, true)));
        }

        [Test]
        public void Given_TwoHandoffsWhereTheFirstThrows_When_TheHandoffsRun_Then_TheSecondStillRuns()
        {
            // Arrange
            LogAssert.ignoreFailingMessages = true;
            var secondRan = false;
            OnAnotherThread(() =>
            {
                VelvetMainThread.Post(static _ => throw new InvalidOperationException("first handoff"), null);
                VelvetMainThread.Post(_ => secondRan = true, null);
            });

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(secondRan, Is.True);
        }

        [Test]
        public void Given_AHandoffThatThrows_When_TheHandoffsRun_Then_TheExceptionIsLogged()
        {
            // Arrange
            OnAnotherThread(() => VelvetMainThread.Post(static _ => throw new InvalidOperationException("thrown handoff"), null));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: thrown handoff"));

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert — LogAssert.Expect verifies the exception reached the console rather than vanishing
        }

        [Test]
        public void Given_APendingHandoff_When_TheMainThreadIsCapturedAgain_Then_TheHandoffIsDropped()
        {
            // Arrange
            var ran = false;
            OnAnotherThread(() => VelvetMainThread.Post(_ => ran = true, null));
            var capture = typeof(VelvetMainThread).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!;

            // Act
            capture.Invoke(null, null);
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(ran, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_AHandoffPostedFromAnotherThread_When_TheEditorUpdates_Then_ItRunsOnTheMainThreadWithoutBeingDrainedByHand()
        {
            // Arrange
            var ranOnThread = 0;
            OnAnotherThread(() => VelvetMainThread.Post(_ => ranOnThread = Thread.CurrentThread.ManagedThreadId, null));

            // Act
            var elapsed = Stopwatch.StartNew();
            while (ranOnThread == 0 && elapsed.ElapsedMilliseconds < SettleTimeoutMilliseconds)
            {
                yield return null;
            }

            // Assert
            Assert.That(ranOnThread, Is.EqualTo(s_mainThreadId));
        }
    }
}
