using System;
using System.Collections;
using System.Collections.Generic;
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

        static readonly MethodInfo CaptureMethod =
            typeof(VelvetMainThread).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!;

        static readonly FieldInfo ThreadIdField =
            typeof(VelvetMainThread).GetField("_threadId", BindingFlags.Static | BindingFlags.NonPublic)!;

        static readonly FieldInfo ContextField =
            typeof(VelvetMainThread).GetField("_context", BindingFlags.Static | BindingFlags.NonPublic)!;

        static readonly FieldInfo CapturedField =
            typeof(VelvetMainThread).GetField("_captured", BindingFlags.Static | BindingFlags.NonPublic)!;

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
            CaptureMethod.Invoke(null, null);
#if UNITY_EDITOR
            DrainEditorUpdateForTest();
#endif
        }

        sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            readonly List<(SendOrPostCallback Callback, object State)> _posted = new();

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (_posted)
                {
                    _posted.Add((callback, state));
                }
            }

            internal int RunPosted()
            {
                (SendOrPostCallback Callback, object State)[] posted;
                lock (_posted)
                {
                    posted = _posted.ToArray();
                    _posted.Clear();
                }

                foreach (var (callback, state) in posted)
                {
                    callback(state);
                }

                return posted.Length;
            }
        }

        sealed class RecordingTaskScheduler : TaskScheduler
        {
            readonly List<Task> _queued = new();

            protected override void QueueTask(Task task)
            {
                lock (_queued)
                {
                    _queued.Add(task);
                }
            }

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

            protected override IEnumerable<Task> GetScheduledTasks()
            {
                lock (_queued)
                {
                    return _queued.ToArray();
                }
            }

            internal int RunQueued()
            {
                Task[] queued;
                lock (_queued)
                {
                    queued = _queued.ToArray();
                    _queued.Clear();
                }

                foreach (var task in queued)
                {
                    TryExecuteTask(task);
                }

                return queued.Length;
            }
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

        static async VelvetTask<bool> YieldThenReadWhetherOnAPoolThread()
        {
            await VelvetTask.Yield();
            return Thread.CurrentThread.IsThreadPoolThread;
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
        public void Given_AnAsyncMethodOnAnotherThread_When_ItAwaitsYield_Then_ItResumesOnAPoolThread()
        {
            // Arrange
            VelvetTask<bool> task = default;
            OnAnotherThread(() => task = YieldThenReadWhetherOnAPoolThread());
            SpinWait.SpinUntil(() => task.Status != VelvetTaskStatus.Pending, (int)SettleTimeoutMilliseconds);

            // Act
            var resumedOnAPoolThread = task.GetAwaiter().GetResult();

            // Assert
            Assert.That(resumedOnAPoolThread, Is.True);
        }

        [Test]
        public void Given_AnotherThreadHoldingASynchronizationContext_When_AnAsyncMethodThereAwaitsYield_Then_ItResumesThroughThatContext()
        {
            // Arrange
            var context = new RecordingSynchronizationContext();
            VelvetTask<bool> task = default;
            OnAnotherThread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(context);
                task = YieldThenReadWhetherOnAPoolThread();
            });
            var statusBeforeThePost = task.Status;

            // Act
            var posted = context.RunPosted();

            // Assert
            Assert.That((statusBeforeThePost, posted, task.Status),
                Is.EqualTo((VelvetTaskStatus.Pending, 1, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_AnAsyncMethodRunByATaskSchedulerOnAnotherThread_When_ItAwaitsYield_Then_ItResumesThroughThatScheduler()
        {
            // Arrange
            var scheduler = new RecordingTaskScheduler();
            VelvetTask<bool> task = default;
            OnAnotherThread(() =>
            {
                Task.Factory.StartNew(
                    () => task = YieldThenReadWhetherOnAPoolThread(),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    scheduler);
                scheduler.RunQueued();
            });
            var statusBeforeTheSchedulerRuns = task.Status;

            // Act
            var queued = scheduler.RunQueued();

            // Assert
            Assert.That((statusBeforeTheSchedulerRuns, queued, task.Status),
                Is.EqualTo((VelvetTaskStatus.Pending, 1, VelvetTaskStatus.Succeeded)));
        }

        [Test]
        public void Given_AThreadHoldingTheBaseSynchronizationContext_When_ATaskSchedulerRunsAnAsyncMethodThereThatAwaitsYield_Then_ItResumesThroughThatScheduler()
        {
            // Arrange
            var scheduler = new RecordingTaskScheduler();
            VelvetTask<bool> task = default;
            OnAnotherThread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                Task.Factory.StartNew(
                    () => task = YieldThenReadWhetherOnAPoolThread(),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    scheduler);
                scheduler.RunQueued();
            });

            // Act
            var queued = scheduler.RunQueued();

            // Assert
            Assert.That((queued, task.Status), Is.EqualTo((1, VelvetTaskStatus.Succeeded)));
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
        public void Given_AHandoffThatReentersTheDrain_When_TheHandoffsRun_Then_EachHandoffRunsOnce()
        {
            // Arrange
            var reenteringRuns = 0;
            var postedRuns = 0;
            OnAnotherThread(() => VelvetMainThread.Post(_ =>
            {
                reenteringRuns++;
                if (reenteringRuns > 1)
                {
                    return;
                }

                VelvetMainThread.Post(_ => postedRuns++, null);
                VelvetMainThread.Post(_ => postedRuns++, null);
                VelvetMainThread.RunHandoffs();
                VelvetMainThread.RunHandoffs();
            }, null));

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That((reenteringRuns, postedRuns), Is.EqualTo((1, 2)));
        }

        [Test]
        public void Given_TwoBatchesOfHandoffs_When_EachIsRun_Then_TheFirstDoesNotRunAgain()
        {
            // Arrange
            var firstRuns = 0;
            VelvetMainThread.Post(_ => firstRuns++, null);
            VelvetMainThread.RunHandoffs();
            VelvetMainThread.Post(_ => { }, null);

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(firstRuns, Is.EqualTo(1));
        }

        [Test]
        public void Given_NothingCapturedYet_When_TheLoadTimeCaptureRuns_Then_AnotherThreadCanHandOffBeforeTheMainThreadAsks()
        {
            // Arrange
            ThreadIdField.SetValue(null, 0);
            ContextField.SetValue(null, null);
            CaptureMethod.Invoke(null, null);
            Exception? thrown = null;
            var ran = false;

            // Act
            OnAnotherThread(() =>
            {
                try
                {
                    VelvetMainThread.Post(_ => ran = true, null);
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }
            });
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That((thrown, ran), Is.EqualTo(((Exception?)null, true)));
        }

        [Test]
        public void Given_TheMainThreadNotYetCaptured_When_AThreadWithoutUnitysContextAsksFirst_Then_TheMainThreadIsStillTakenForIt()
        {
            // Arrange
            ThreadIdField.SetValue(null, 0);
            var elsewhere = true;
            OnAnotherThread(() => elsewhere = VelvetMainThread.IsCurrent);

            // Act
            var here = VelvetMainThread.IsCurrent;

            // Assert
            Assert.That((elsewhere, here), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_TheMainThreadNotYetCaptured_When_APoolThreadHoldingUnitysContextAsksFirst_Then_TheMainThreadIsStillTakenForIt()
        {
            // Arrange
            var unityContext = SynchronizationContext.Current;
            ThreadIdField.SetValue(null, 0);
            var poolThreadTaken = true;
            using var asked = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(unityContext);
                poolThreadTaken = VelvetMainThread.IsCurrent;
                SynchronizationContext.SetSynchronizationContext(null);
                asked.Set();
            });
            asked.Wait();

            // Act
            var mainThreadTaken = VelvetMainThread.IsCurrent;

            // Assert
            Assert.That((poolThreadTaken, mainThreadTaken), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AHandoffPostedAfterAnEarlyCapture_When_TheLoadTimeCaptureRuns_Then_TheHandoffStillRuns()
        {
            // Arrange
            ThreadIdField.SetValue(null, 0);
            CapturedField.SetValue(null, false);
            var earlyCapture = VelvetMainThread.IsCurrent;
            var ran = false;
            OnAnotherThread(() => VelvetMainThread.Post(_ => ran = true, null));

            // Act
            CaptureMethod.Invoke(null, null);
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That((earlyCapture, ran), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APendingHandoff_When_TheMainThreadIsCapturedAgain_Then_TheHandoffIsDropped()
        {
            // Arrange
            var ran = false;
            OnAnotherThread(() => VelvetMainThread.Post(_ => ran = true, null));

            // Act
            CaptureMethod.Invoke(null, null);
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
