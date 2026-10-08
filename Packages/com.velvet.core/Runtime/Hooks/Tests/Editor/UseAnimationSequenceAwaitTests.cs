using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>AnimationSequenceStep.Await</c> on the EditMode fake clock, with the harness
    /// <see cref="UseAnimationSequenceTests"/> uses: the cursor holds until the task settles and not past it,
    /// time left over from before a pending await counts toward no hold after it, a pause holds a settled wait, a fault reaches the error
    /// boundary, and a restart or unmount cancels the wait it leaves and keeps that wait's late settle out of the
    /// sequence it starts.
    /// </summary>
    internal sealed class UseAnimationSequenceAwaitTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;
        private static int s_callCount;
        private static Exception s_caught;
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<VelvetTaskCompletionSource> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_callCount = 0;
            s_caught = null;
            s_tokens.Clear();
            s_sources.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode SequenceHost()
        {
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, deps: Array.Empty<object>());
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode Boundary()
        {
            Hooks.UseFallback(ex =>
            {
                s_caught = ex;
                return V.Div(name: "fallback");
            });
            return V.Component(SequenceHost, key: "sequence");
        }

        // Each call hands out a fresh pending task and records the token it was given, so a case can settle or
        // inspect any one arrival's wait.
        private static VelvetTask NextPending(CancellationToken cancellationToken)
        {
            s_tokens.Add(cancellationToken);
            var source = new VelvetTaskCompletionSource();
            s_sources.Add(source);
            return source.Task;
        }

        private void Mount(Func<VNode> host) => Mount(host, null);

        private void Mount(Func<VNode> host, MountOptions options)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            var tree = V.Component(host, key: "root");
            _mounted = options == null ? V.Mount(_host.Root, tree) : V.Mount(_host.Root, tree, options);
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private void AdvanceTicks(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            _mounted.FlushStateForTest();
        }

        private void AdvanceOneFrame(int ms)
        {
            UseFrameFakeClockHost.Ms += ms;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();
        }

        private static AnimationSequenceStep[] AwaitThenB() => new[]
        {
            AnimationSequenceStep.Await(NextPending),
            AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
        };

        [Test]
        public void Given_ANullTaskFactory_When_AnAwaitStepIsBuilt_Then_ItThrowsArgumentNullException()
        {
            // Arrange
            Func<CancellationToken, VelvetTask> taskFactory = null;

            // Act
            TestDelegate build = () => AnimationSequenceStep.Await(taskFactory);

            // Assert
            Assert.That(build, Throws.ArgumentNullException);
        }

        [Test]
        public void Given_AnAwaitStepWhoseTaskIsPending_When_TimePassesWellBeyondAnyHold_Then_TheCursorStaysOnIt()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);

            // Act
            AdvanceTicks(60);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnAwaitStepWhoseTaskIsPending_When_TheTaskCompletesAndAFrameTicks_Then_TheNextStepIsCurrent()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            AdvanceTicks(5);

            // Act
            s_sources[0].SetResult();
            AdvanceTicks(1);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_AnAwaitStepWhoseTaskHasAlreadyCompleted_When_OneFrameCrossesTheHoldBeforeIt_Then_TheStepAfterItIsCurrentInThatFrame()
        {
            // Arrange — the 60ms frame crosses "a"'s 50ms hold and arrives at the await inside one Advance.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.Await(_ => VelvetTask.CompletedTask),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount(SequenceHost);

            // Act
            AdvanceOneFrame(60);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_APendingAwaitStepAfterAShortHold_When_OneFrameCrossesThatHold_Then_TheCursorStopsOnTheAwait()
        {
            // Arrange — the 100ms frame would also reach "b" if the await held nothing.
            s_steps = new[]
            {
                AnimationSequenceStep.To("x", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.Await(NextPending),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount(SequenceHost);

            // Act
            AdvanceOneFrame(100);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnAwaitReachedWithTimeLeftOverFromTheFrameThatCrossedIntoIt_When_ItSettlesAndAShortFrameTicks_Then_TheNextHoldHasNotElapsed()
        {
            // Arrange — the 300ms frame leaves 250ms over past "x"'s 50ms hold, more than "a"'s 200ms.
            s_steps = new[]
            {
                AnimationSequenceStep.To("x", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.Await(NextPending),
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.2f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount(SequenceHost);
            AdvanceOneFrame(300);
            s_sources[0].SetResult();

            // Act
            AdvanceOneFrame(16);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_APausedSequenceWhoseAwaitedTaskCompletes_When_ItResumes_Then_TheCallAfterTheAwaitFiresOnlyThen()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.Await(NextPending),
                AnimationSequenceStep.Call(() => s_callCount++),
            };
            Mount(SequenceHost);
            s_controls.Pause();
            s_sources[0].SetResult();
            AdvanceTicks(5);
            var callsWhilePaused = s_callCount;

            // Act
            s_controls.Play();
            AdvanceTicks(1);

            // Assert
            Assert.That((callsWhilePaused, s_callCount), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AnAwaitStepWhoseTaskIsPending_When_ControlsRestart_Then_TheTokenTheLeftWaitWasGivenIsCancelled()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            var leftToken = s_tokens[s_tokens.Count - 1];

            // Act
            s_controls.Restart();

            // Assert
            Assert.That(leftToken.IsCancellationRequested, Is.True);
        }

        // The first two arrivals restart from inside their factories, so the walker's drain reseeds twice: the
        // second restart lands during an arrival the drain itself makes.
        private static AnimationSequenceStep[] TwiceRestartingAwaitThenB() => new[]
        {
            AnimationSequenceStep.Await(token =>
            {
                var task = NextPending(token);
                if (s_tokens.Count <= 2)
                {
                    s_controls.Restart();
                }
                return task;
            }),
            AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
        };

        [Test]
        public void Given_AnAwaitFactoryThatRestartsTheSequenceFromInsideItselfTwice_When_Mounted_Then_EveryWaitButTheLastIsCancelled()
        {
            // Arrange
            s_steps = TwiceRestartingAwaitThenB();

            // Act
            Mount(SequenceHost);

            // Assert
            Assert.That(
                (s_tokens.Count, s_tokens[0].IsCancellationRequested, s_tokens[1].IsCancellationRequested,
                    s_tokens[2].IsCancellationRequested),
                Is.EqualTo((3, true, true, false)));
        }

        [Test]
        public void Given_AMountWhoseAwaitFactoriesRestartedFromInsideThemselves_When_ControlsRestart_Then_TheWaitTheMountLeftIsCancelled()
        {
            // Arrange
            s_steps = TwiceRestartingAwaitThenB();
            Mount(SequenceHost);
            var leftToken = s_tokens[s_tokens.Count - 1];

            // Act
            s_controls.Restart();

            // Assert
            Assert.That(leftToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Given_AnAwaitStepWhoseTaskIsPending_When_TheHostUnmounts_Then_TheTokenItWasGivenIsCancelled()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            var token = s_tokens[s_tokens.Count - 1];

            // Act
            _mounted.Dispose();
            _mounted = null;

            // Assert
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Given_ALeftWaitWhoseTokenCallbackThrows_When_ControlsRestartAndTwoFramesTick_Then_TheStepAfterTheRestartedAwaitIsCurrent()
        {
            // Arrange — only the first arrival's wait is pending and carries the throwing callback; the
            // restarted one has already completed.
            s_steps = new[]
            {
                AnimationSequenceStep.Await(token =>
                {
                    s_tokens.Add(token);
                    if (s_tokens.Count > 1)
                    {
                        return VelvetTask.CompletedTask;
                    }
                    token.Register(() => throw new InvalidOperationException("token probe"));
                    return new VelvetTaskCompletionSource().Task;
                }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount(SequenceHost);

            // Act — the callback's exception leaves the restart; what is pinned is the sequence it leaves behind.
            try
            {
                s_controls.Restart();
            }
            catch (AggregateException)
            {
            }
            AdvanceTicks(2);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ALeftWaitWhoseTokenCallbackRestartsTheSequence_When_ControlsRestart_Then_EveryWaitButTheLastIsCancelled()
        {
            // Arrange — the callback's restart lands after the outer restart has installed the second wait.
            s_steps = new[]
            {
                AnimationSequenceStep.Await(token =>
                {
                    var task = NextPending(token);
                    if (s_tokens.Count == 1)
                    {
                        token.Register(() => s_controls.Restart());
                    }
                    return task;
                }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount(SequenceHost);

            // Act
            s_controls.Restart();

            // Assert
            Assert.That(
                (s_tokens.Count, s_tokens[0].IsCancellationRequested, s_tokens[1].IsCancellationRequested,
                    s_tokens[2].IsCancellationRequested),
                Is.EqualTo((3, true, true, false)));
        }

        [Test]
        public void Given_ARestartWhileAnAwaitIsPending_When_TheLeftWaitsTaskCompletes_Then_TheRestartedSequenceStaysOnItsOwnWait()
        {
            // Arrange — step 0 is the await again after the restart, so only the restarted arrival's task may
            // release it.
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            s_controls.Restart();
            _mounted.FlushStateForTest();

            // Act
            s_sources[0].SetResult();
            AdvanceTicks(3);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnAwaitStepUnderAnErrorBoundary_When_ItsTaskFaults_Then_TheBoundaryCatchesTheTasksException()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(Boundary, CaughtErrors.Unlogged);

            // Act
            s_sources[0].SetException(new InvalidOperationException("await probe"));
            AdvanceTicks(1);

            // Assert
            Assert.That(s_caught?.Message, Is.EqualTo("await probe"));
        }

        [Test]
        public void Given_ALastAwaitStepWhoseTaskHasAlreadyFaulted_When_TheFrameThatReachesItTicks_Then_TheBoundaryCatchesTheTasksException()
        {
            // Arrange — a step left unreleased at arrival would be crossed into completion in that frame, after
            // which no frame reads it again.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.Await(_ => VelvetTask.FromException(new InvalidOperationException("sync probe"))),
            };
            Mount(Boundary, CaughtErrors.Unlogged);

            // Act
            AdvanceOneFrame(60);

            // Assert
            Assert.That(s_caught?.Message, Is.EqualTo("sync probe"));
        }

        [Test]
        public void Given_AnAwaitStepUnderAnErrorBoundary_When_ItsTaskCancelsOnItsOwn_Then_TheBoundaryCatchesTheCancellation()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(Boundary, CaughtErrors.Unlogged);

            // Act
            s_sources[0].SetCanceled();
            AdvanceTicks(1);

            // Assert
            Assert.That(s_caught, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void Given_AnAwaitStepWithNoErrorBoundary_When_ItsTaskFaultsAndTwoFramesTick_Then_TheNextStepIsCurrent()
        {
            // Arrange — with no boundary the fault is logged, once.
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: await probe"));
            s_sources[0].SetException(new InvalidOperationException("await probe"));

            // Act
            AdvanceTicks(2);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ARestartWhileAnAwaitIsPending_When_TheLeftWaitsTaskFaults_Then_TheFaultIsLogged()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            s_controls.Restart();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: left behind"));

            // Act
            s_sources[0].SetException(new InvalidOperationException("left behind"));

            // Assert — LogAssert.Expect fails the case when the fault never reaches the console.
        }

        [Test]
        public void Given_AnAwaitedTaskThatFaultedBeforeAnyFrameReadIt_When_ControlsRestart_Then_TheFaultIsLogged()
        {
            // Arrange
            s_steps = AwaitThenB();
            Mount(SequenceHost);
            s_sources[0].SetException(new InvalidOperationException("left behind"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: left behind"));

            // Act
            s_controls.Restart();

            // Assert — LogAssert.Expect fails the case when the fault never reaches the console.
        }
    }
}
