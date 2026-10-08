using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the <c>iterations</c> overload of <c>Hooks.UseAnimationSequence</c> on the EditMode fake clock, with
    /// <see cref="UseAnimationSequenceTests"/>' mount and advance harness: the count of passes a sequence plays,
    /// where it stops, what zero plays, and what a restart plays again.
    /// </summary>
    internal sealed class UseAnimationSequenceIterationsTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static int s_iterations;
        private static float s_repeatDelaySec;
        private static Action s_rerender;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;
        private static int s_callCount;
        private static int s_renderCount;
        private static AnimationSequenceState s_firstRenderState;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_callCount = 0;
            s_renderCount = 0;
            s_repeatDelaySec = 0f;
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
        private static VNode IteratingSequenceHost()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_rerender = () => setTick.Invoke(n => n + 1);
            var (state, controls) = Hooks.UseAnimationSequence(
                s_steps, Array.Empty<object>(), iterations: s_iterations, repeatDelaySec: s_repeatDelaySec);
            if (s_renderCount++ == 0)
            {
                s_firstRenderState = state;
            }
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        private void Mount()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(IteratingSequenceHost, key: "root"));
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private void AdvancePast(float seconds)
        {
            var ticks = (int)(seconds * 1000f / 16f) + 2;
            for (var i = 0; i < ticks; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            _mounted.FlushStateForTest();
        }

        // Two 0.1s holds, so one pass takes 0.2s.
        private static AnimationSequenceStep[] TwoLabels() => new[]
        {
            AnimationSequenceStep.To("a", holdSec: 0.1f),
            AnimationSequenceStep.To("b", holdSec: 0.1f),
        };

        // The Call sits after step 0, where neither a reseed nor a StrictMode re-run of the mount effect reaches it,
        // so it fires once per pass and nowhere else.
        private static AnimationSequenceStep[] LabelThenCall() => new[]
        {
            AnimationSequenceStep.To("a", holdSec: 0.05f),
            AnimationSequenceStep.Call(() => s_callCount++),
        };

        [Test]
        public void Given_TwoIterations_When_OnlyTheFirstPassHasElapsed_Then_TheCursorIsBackAtStepZeroAndNotComplete()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            Mount();

            // Act — 0.272s of ticks lands inside the second pass's first hold.
            AdvancePast(0.25f);

            // Assert
            Assert.That((s_state.StepIndex, s_state.CurrentLabel, s_state.IsComplete), Is.EqualTo((0, "a", false)));
        }

        [Test]
        public void Given_TwoIterations_When_TimePassesBothPassesButNotAThird_Then_TheSequenceIsCompleteOnTheLastStep()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            Mount();

            // Act — 0.432s of ticks, which a third pass would be inside its first hold at.
            AdvancePast(0.4f);

            // Assert
            Assert.That((s_state.StepIndex, s_state.CurrentLabel, s_state.IsComplete), Is.EqualTo((1, "b", true)));
        }

        [Test]
        public void Given_ThreeIterationsEndingInACallStep_When_TimeRunsFarPastThreePasses_Then_TheCallFiredThreeTimes()
        {
            // Arrange
            s_steps = LabelThenCall();
            s_iterations = 3;
            Mount();

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That(s_callCount, Is.EqualTo(3));
        }

        [Test]
        public void Given_ThreeIterationsOfOnlyZeroHoldSteps_When_ManyFramesPass_Then_TheCallFiredThreeTimes()
        {
            // Arrange — a Wait(0) first keeps the Call off step 0, for the reason LabelThenCall gives.
            s_steps = new[]
            {
                AnimationSequenceStep.Wait(0f),
                AnimationSequenceStep.Call(() => s_callCount++),
            };
            s_iterations = 3;
            Mount();

            // Act
            AdvancePast(0.5f);

            // Assert
            Assert.That(s_callCount, Is.EqualTo(3));
        }

        [Test]
        public void Given_ZeroIterations_When_TimePasses_Then_NoStepCommittedAndTheSequenceIsComplete()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.Call(() => s_callCount++),
                AnimationSequenceStep.To("a", holdSec: 0.05f),
            };
            s_iterations = 0;
            Mount();

            // Act
            AdvancePast(0.2f);

            // Assert
            Assert.That((s_callCount, s_state.CurrentLabel, s_state.IsComplete), Is.EqualTo((0, (string)null, true)));
        }

        [Test]
        public void Given_ZeroIterations_When_TheMountRenderRuns_Then_ItAlreadyReadsComplete()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 0;

            // Act
            Mount();

            // Assert
            Assert.That(s_firstRenderState.IsComplete, Is.True);
        }

        [Test]
        public void Given_TwoIterations_When_TheMountRenderRuns_Then_ItReadsNotComplete()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;

            // Act
            Mount();

            // Assert
            Assert.That(s_firstRenderState.IsComplete, Is.False);
        }

        [Test]
        public void Given_AnEmptyStepList_When_TheMountRenderRuns_Then_ItAlreadyReadsComplete()
        {
            // Arrange
            s_steps = Array.Empty<AnimationSequenceStep>();
            s_iterations = 2;

            // Act
            Mount();

            // Assert
            Assert.That(s_firstRenderState.IsComplete, Is.True);
        }

        // Three passes of TwoLabels, run to the second pass's first hold (0.272s of ticks, the pass spanning 0.2s
        // to 0.4s), then the count set and a render forced so the walker reads it.
        private void LowerDuringSecondPass(int lowered, AnimationSequenceStep[] steps = null)
        {
            s_steps = steps ?? TwoLabels();
            s_iterations = 3;
            Mount();
            AdvancePast(0.25f);
            s_iterations = lowered;
            s_rerender();
            _mounted.FlushStateForTest();
        }

        [Test]
        public void Given_ThreeIterationsLoweredToOneDuringTheSecondPass_When_TheNextFramesTick_Then_TheSequenceIsCompleteBeforeThePassEnds()
        {
            // Arrange
            LowerDuringSecondPass(1);

            // Act — 0.032s more, where the second pass runs to 0.4s and the clock is at 0.304s.
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_ThreeIterationsLoweredToOneDuringTheSecondPass_When_TheNextFramesTick_Then_TheEndStateIsShownAndTheSkippedCallDidNotFire()
        {
            // Arrange — the Call sits between the labels, so the second pass is lowered before it is reached.
            LowerDuringSecondPass(1, new[]
            {
                AnimationSequenceStep.To("a", holdSec: 0.1f),
                AnimationSequenceStep.Call(() => s_callCount++),
                AnimationSequenceStep.To("b", holdSec: 0.1f),
            });

            // Act
            AdvancePast(0f);

            // Assert — one Call from the first pass, and the label a normal completion would hold.
            Assert.That((s_state.CurrentLabel, s_state.StepIndex, s_callCount, s_state.IsComplete), Is.EqualTo(("b", 2, 1, true)));
        }

        [Test]
        public void Given_ThreeIterationsLoweredToTwoDuringTheSecondPass_When_TheNextFramesTick_Then_TheSecondPassStillPlays()
        {
            // Arrange
            LowerDuringSecondPass(2);

            // Act — as the case lowering to one, which this count's second pass outlasts.
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.IsComplete, Is.False);
        }

        [Test]
        public void Given_TwoIterationsThatCompleted_When_TheCountIsRaisedToThree_Then_ThePassesPlayOnAndTheCallFiresThreeTimes()
        {
            // Arrange
            s_steps = LabelThenCall();
            s_iterations = 2;
            Mount();
            AdvancePast(0.5f);
            s_iterations = 3;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act
            AdvancePast(0.5f);

            // Assert
            Assert.That(s_callCount, Is.EqualTo(3));
        }

        [Test]
        public void Given_TwoIterationsThatCompleted_When_TheCountIsRaisedToThree_Then_TheSequenceNoLongerReadsComplete()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            Mount();
            AdvancePast(0.4f);
            s_iterations = 3;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.IsComplete, Is.False);
        }

        [Test]
        public void Given_TwoIterationsWithAGapOfTwoTenths_When_TheFirstPassHasEnded_Then_TheCursorWaitsOnTheLastStep()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();

            // Act — 0.24s of ticks, inside the gap that spans 0.2s to 0.4s.
            AdvancePast(0.22f);

            // Assert
            Assert.That((s_state.StepIndex, s_state.IsComplete), Is.EqualTo((1, false)));
        }

        [Test]
        public void Given_TwoIterationsWithAGapOfTwoTenths_When_TimePassesPastTheSecondPass_Then_TheSequenceIsCompleteWithoutATrailingGap()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();

            // Act — 0.672s of ticks: the second pass ends at 0.6s, and a gap after it would hold to 0.8s.
            AdvancePast(0.65f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_ThreeIterationsWithAGapLoweredToOneInsideTheGap_When_TheNextFramesTick_Then_TheSequenceIsComplete()
        {
            // Arrange — the clock is at 0.24s, inside the gap that spans 0.2s to 0.4s.
            s_steps = TwoLabels();
            s_iterations = 3;
            s_repeatDelaySec = 0.2f;
            Mount();
            AdvancePast(0.22f);
            s_iterations = 1;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_TwoIterationsThatCompleted_When_Restarted_Then_BothPassesPlayAgain()
        {
            // Arrange
            s_steps = LabelThenCall();
            s_iterations = 2;
            Mount();
            AdvancePast(0.5f);

            // Act
            s_controls.Restart();
            _mounted.FlushStateForTest();
            AdvancePast(0.5f);

            // Assert — two passes before the restart and two after it.
            Assert.That(s_callCount, Is.EqualTo(4));
        }

        [Test]
        public void Given_ANegativeIterationCount_When_TheHookIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Arrange
            var steps = TwoLabels();

            // Act
            TestDelegate call = () => Hooks.UseAnimationSequence(steps, Array.Empty<object>(), iterations: -1);

            // Assert — outside a render, so a count that went unchecked would throw InvalidOperationException
            // from the render guard instead.
            Assert.Throws<ArgumentOutOfRangeException>(call);
        }

        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Given_AGapThatIsNegativeOrNotFinite_When_TheHookIsCalled_Then_ItThrowsArgumentOutOfRange(float gap)
        {
            // Arrange
            var steps = TwoLabels();

            // Act
            TestDelegate call = () => Hooks.UseAnimationSequence(steps, Array.Empty<object>(), iterations: 2, repeatDelaySec: gap);

            // Assert — outside a render, as the negative count's case.
            Assert.Throws<ArgumentOutOfRangeException>(call);
        }
    }
}
