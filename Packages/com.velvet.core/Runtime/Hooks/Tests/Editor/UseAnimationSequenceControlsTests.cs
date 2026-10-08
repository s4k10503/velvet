using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>AnimationSequenceControls</c>' <c>Cancel</c>, <c>SetSpeed</c>, <c>Speed</c> and <c>TimeSec</c> on the
    /// fake clock <see cref="UseAnimationSequenceTests"/> walks the steps on. Every tick is 16ms.
    /// </summary>
    internal sealed class UseAnimationSequenceControlsTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static bool s_loop;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;
        private static int s_callCount;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_loop = false;
            s_callCount = 0;
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
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, deps: Array.Empty<object>(), loop: s_loop);
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        private void Mount()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(SequenceHost, key: "root"));
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

        private static AnimationSequenceStep To(string label, float durationSec)
            => AnimationSequenceStep.To(label, new StyleTransitionConfig { DurationSec = durationSec });

        [Test]
        public void Given_AMountedSequence_When_Cancelled_Then_CurrentLabelIsNull()
        {
            // Arrange
            s_steps = new[] { To("a", 0.5f), To("b", 0.5f) };
            Mount();

            // Act
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentLabel, Is.Null);
        }

        [Test]
        public void Given_AMountedSequence_When_Cancelled_Then_CurrentTransitionIsNull()
        {
            // Arrange
            s_steps = new[] { To("a", 0.5f), To("b", 0.5f) };
            Mount();

            // Act
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentTransition, Is.Null);
        }

        [Test]
        public void Given_ASequenceOnItsSecondStep_When_Cancelled_Then_StepIndexIsBackAtZero()
        {
            // Arrange — 160ms is past step 0's 100ms hold.
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            AdvanceTicks(10);
            var indexBeforeCancel = s_state.StepIndex;

            // Act
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((indexBeforeCancel, s_state.StepIndex), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ACompletedSequence_When_Cancelled_Then_IsCompleteIsFalse()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f) };
            Mount();
            AdvanceTicks(10);
            var completeBeforeCancel = s_state.IsComplete;

            // Act
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((completeBeforeCancel, s_state.IsComplete), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASequenceTenTicksIntoItsFirstHold_When_Cancelled_Then_TimeSecIsZero()
        {
            // Arrange
            s_steps = new[] { To("a", 0.5f), To("b", 0.5f) };
            Mount();
            AdvanceTicks(10);

            // Act
            s_controls.Cancel();

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ASequenceIntoItsSecondHold_When_Cancelled_Then_TimeSecIsZero()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            AdvanceTicks(10);

            // Act
            s_controls.Cancel();

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ACallStepThatCancelsTheSequence_When_TheWalkerCrossesIt_Then_ItsCallbackRunsOnce()
        {
            // Arrange
            s_steps = new[]
            {
                To("a", 0.1f),
                AnimationSequenceStep.Call(() =>
                {
                    s_callCount++;
                    s_controls.Cancel();
                }),
                To("b", 0.5f),
            };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_callCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_ACallStepThatRestartsAndThenCancels_When_TheWalkerCrossesIt_Then_CurrentLabelIsNull()
        {
            // Arrange
            s_steps = new[]
            {
                To("a", 0.1f),
                AnimationSequenceStep.Call(() =>
                {
                    s_controls.Restart();
                    s_controls.Cancel();
                }),
                To("b", 0.5f),
            };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.Null);
        }

        [Test]
        public void Given_ACancelledSequence_When_Played_Then_CurrentLabelIsTheFirstStepsAgain()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Act
            s_controls.Play();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_ACancelledSequence_When_PlayedAndTheFirstHoldElapses_Then_TheSecondStepIsCurrent()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            s_controls.Cancel();
            _mounted.FlushStateForTest();

            // Act
            s_controls.Play();
            AdvanceTicks(10);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ACancelledSequence_When_RestartedAndTimePasses_Then_TheCursorStaysOnARecommittedStepZero()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            s_controls.Cancel();

            // Act
            s_controls.Restart();
            AdvanceTicks(10);

            // Assert
            Assert.That((s_state.StepIndex, s_state.CurrentLabel), Is.EqualTo((0, "a")));
        }

        [Test]
        public void Given_ASpeedOfTwo_When_LessThanTheFirstHoldButMoreThanHalfOfItElapses_Then_TheSecondStepIsCurrent()
        {
            // Arrange — 240ms of clock against a 400ms hold.
            s_steps = new[] { To("a", 0.4f), To("b", 0.4f) };
            Mount();
            s_controls.SetSpeed(2f);

            // Act
            AdvanceTicks(15);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ASpeedOfTwo_When_TheSequenceRestarts_Then_TheFirstStepsTransitionIsHandedOutAtHalfItsDuration()
        {
            // Arrange
            s_steps = new[] { To("a", 0.4f), To("b", 0.4f) };
            Mount();
            s_controls.SetSpeed(2f);

            // Act
            s_controls.Restart();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentTransition.DurationSec, Is.EqualTo(0.2f).Within(1e-6f));
        }

        [Test]
        public void Given_ASpeedSet_When_SpeedIsRead_Then_ItIsTheRateSet()
        {
            // Arrange
            s_steps = new[] { To("a", 0.4f) };
            Mount();

            // Act
            s_controls.SetSpeed(2.5f);

            // Assert
            Assert.That(s_controls.Speed, Is.EqualTo(2.5f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(0.0009f)]
        [TestCase(1000.1f)]
        public void Given_ARateOutsideAThousandthToAThousand_When_SetAsTheSpeed_Then_ItIsRefused(float rate)
        {
            // Arrange
            s_steps = new[] { To("a", 0.4f) };
            Mount();

            // Act
            TestDelegate setSpeed = () => s_controls.SetSpeed(rate);

            // Assert
            Assert.That(setSpeed, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0.001f)]
        [TestCase(1000f)]
        public void Given_ARateAtAnEndOfAThousandthToAThousand_When_SetAsTheSpeed_Then_ItIsTaken(float rate)
        {
            // Arrange
            s_steps = new[] { To("a", 0.4f) };
            Mount();

            // Act
            TestDelegate setSpeed = () => s_controls.SetSpeed(rate);

            // Assert
            Assert.That(setSpeed, Throws.Nothing);
        }

        [Test]
        public void Given_ASequenceTenTicksIntoItsFirstHold_When_TimeSecIsRead_Then_ItIsTheTimeElapsed()
        {
            // Arrange
            s_steps = new[] { To("a", 0.5f), To("b", 0.5f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.16f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceTenTicksPastAShortFirstHold_When_TimeSecIsRead_Then_ItCountsTheFirstHold()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.16f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceTheClockHasRunPast_When_TimeSecIsRead_Then_ItIsTheSequencesLength()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f) };
            Mount();

            // Act
            AdvanceTicks(10);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.1f).Within(1e-4f));
        }

        [Test]
        public void Given_ALoopingSequenceOnItsSecondPass_When_TimeSecIsRead_Then_ItCountsTheFirstPassToo()
        {
            // Arrange — 240ms against a 200ms pass.
            s_steps = new[] { To("a", 0.1f), To("b", 0.1f) };
            s_loop = true;
            Mount();

            // Act
            AdvanceTicks(15);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.24f).Within(1e-4f));
        }

        [Test]
        public void Given_ALoopRunAtAHundredTimesItsSpeed_When_TheSpeedDropsToOne_Then_TheNextFrameCrossesNoStep()
        {
            // Arrange — each 16ms frame at 100 owes 1.6s, far more than the three steps one frame's walk crosses.
            s_steps = new[] { To("a", 0.1f), To("b", 0.1f) };
            s_loop = true;
            Mount();
            s_controls.SetSpeed(100f);
            AdvanceTicks(10);
            s_controls.SetSpeed(1f);
            var indexBefore = s_state.StepIndex;

            // Act — 16ms, short of either 100ms hold.
            AdvanceTicks(1);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(indexBefore));
        }

        [Test]
        public void Given_ALoopFrameCrossingAsManyStepsAsTheGuardAllows_When_TimeSecIsRead_Then_ItKeepsThePartialHold()
        {
            // Arrange — one 16ms frame at 21.875 owes 0.35s: three 100ms holds crossed, 50ms into the fourth.
            s_steps = new[] { To("a", 0.1f), To("b", 0.1f) };
            s_loop = true;
            Mount();
            s_controls.SetSpeed(21.875f);

            // Act
            AdvanceTicks(1);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.35f).Within(1e-3f));
        }

        [Test]
        public void Given_ASequenceIntoItsSecondHold_When_Restarted_Then_TimeSecIsZero()
        {
            // Arrange
            s_steps = new[] { To("a", 0.1f), To("b", 0.5f) };
            Mount();
            AdvanceTicks(10);

            // Act
            s_controls.Restart();

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0f));
        }
    }
}
