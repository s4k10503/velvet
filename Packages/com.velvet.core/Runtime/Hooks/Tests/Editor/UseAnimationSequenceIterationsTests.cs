using System;
using System.Collections.Generic;
using System.Threading;
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
        private static readonly List<CancellationToken> s_tokens = new();
        private static readonly List<VelvetTaskCompletionSource> s_sources = new();

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_callCount = 0;
            s_renderCount = 0;
            s_repeatDelaySec = 0f;
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

        // Each arrival at the Await step hands out a fresh pending task and records its token.
        private static VelvetTask NextPending(CancellationToken cancellationToken)
        {
            s_tokens.Add(cancellationToken);
            var source = new VelvetTaskCompletionSource();
            s_sources.Add(source);
            return source.Task;
        }

        // A 0.05s label then an Await, so the cursor parks on the Await 0.05s into each pass.
        private static AnimationSequenceStep[] LabelThenAwait() => new[]
        {
            AnimationSequenceStep.To("a", holdSec: 0.05f),
            AnimationSequenceStep.Await(NextPending),
        };

        // Resolves the wait the cursor is parked on and lets the next frames cross it.
        private void SettleAndAdvance(int arrival, float seconds)
        {
            s_sources[arrival].SetResult();
            AdvancePast(seconds);
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
        public void Given_ThreeIterationsLoweredToZeroDuringTheSecondPass_When_TheNextFramesTick_Then_TheCursorAndLabelStayAndTheSequenceIsComplete()
        {
            // Arrange
            LowerDuringSecondPass(0);

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That((s_state.CurrentLabel, s_state.StepIndex, s_state.IsComplete), Is.EqualTo(("a", 0, true)));
        }

        [Test]
        public void Given_LoweredFinishOverStepsWithNoTransition_When_TheNextFramesTick_Then_TheTransitionIsFade()
        {
            // Arrange
            LowerDuringSecondPass(1);

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.CurrentTransition?.PlaybackOrigin, Is.SameAs(StyleTransition.Fade));
        }

        [Test]
        public void Given_LoweredFinishWhereOnlyAnEarlierStepNamesATransition_When_TheNextFramesTick_Then_TheLastStepInheritsIt()
        {
            // Arrange
            LowerDuringSecondPass(1, new[]
            {
                AnimationSequenceStep.To("a", StyleTransition.SlideUp, holdSec: 0.1f),
                AnimationSequenceStep.To("b", holdSec: 0.1f),
            });

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.CurrentTransition?.PlaybackOrigin, Is.SameAs(StyleTransition.SlideUp));
        }

        [Test]
        public void Given_LoweredFinishWhereTheLastStepNamesATransition_When_TheNextFramesTick_Then_ItIsTheLastStepsOwn()
        {
            // Arrange
            LowerDuringSecondPass(1, new[]
            {
                AnimationSequenceStep.To("a", StyleTransition.SlideUp, holdSec: 0.1f),
                AnimationSequenceStep.To("b", StyleTransition.SlideDown, holdSec: 0.1f),
            });

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_state.CurrentTransition?.PlaybackOrigin, Is.SameAs(StyleTransition.SlideDown));
        }

        [Test]
        public void Given_ALoweredFinishInsideAGapThenARaisedCount_When_TheGapAndPartOfTheNextPassHaveElapsed_Then_TheCursorIsBackAtStepZero()
        {
            // Arrange — lowered at 0.24s inside the gap spanning 0.2s to 0.4s, then raised again.
            s_steps = TwoLabels();
            s_iterations = 3;
            s_repeatDelaySec = 0.2f;
            Mount();
            AdvancePast(0.22f);
            s_iterations = 1;
            s_rerender();
            _mounted.FlushStateForTest();
            AdvancePast(0f);
            s_iterations = 3;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act — the first tick resumes into a 0.2s gap and 0.256s more is 0.056s into the pass after it; with
            // no gap the cursor would be in the gap that follows the resumed pass, on step 1.
            AdvancePast(0.24f);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(0));
        }

        [Test]
        public void Given_TwoIterationsWithAGapThatCompleted_When_TheCountIsRaisedToThree_Then_TimeSecIsThreePassesAndTwoGaps()
        {
            // Arrange — the two passes end at 0.6s.
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();
            AdvancePast(1f);
            s_iterations = 3;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act — long enough to finish the third pass at 1.0s, a gap after the second pass included.
            AdvancePast(2f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeIterationsWhoseSecondPassInheritsALongerTransition_When_TheCountIsLoweredToTwoInTheThirdPass_Then_TimeSecIsTheTwoPassesPlayed()
        {
            // Arrange — the first To step names no transition: the first pass holds it for Fade's 0.2s and the
            // second for the 0.3s the first pass left, so the passes take 0.5s and 0.6s and the third starts at 1.1s.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a"),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.3f }),
            };
            s_iterations = 3;
            Mount();
            AdvancePast(1.1f);
            s_iterations = 2;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act — the count was lowered at 1.12s, 0.02s into the third pass.
            AdvancePast(0f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(1.1f).Within(1e-3f));
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
        public void Given_TwoIterations_When_TheClockIsInTheSecondPass_Then_TimeSecCountsTheFirstPassToo()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            Mount();

            // Act — 0.272s of ticks against a 0.2s pass.
            AdvancePast(0.25f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.272f).Within(1e-3f));
        }

        [Test]
        public void Given_TwoIterationsWithAGapOfTwoTenths_When_TheClockIsInsideTheGap_Then_TimeSecCountsTheGapAsItPasses()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();

            // Act — 0.24s of ticks, 0.04s into the gap spanning 0.2s to 0.4s.
            AdvancePast(0.22f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.24f).Within(1e-3f));
        }

        [Test]
        public void Given_TwoIterationsWithAGapOfTwoTenths_When_TheSecondPassHasBegun_Then_TimeSecCountsTheWholeGap()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();

            // Act — 0.48s of ticks, 0.08s into the second pass that begins at 0.4s.
            AdvancePast(0.45f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.48f).Within(1e-3f));
        }

        [Test]
        public void Given_TwoIterationsWithAGapOfTwoTenths_When_TimePassesPastTheSecondPass_Then_TimeSecIsTwoPassesAndOneGap()
        {
            // Arrange
            s_steps = TwoLabels();
            s_iterations = 2;
            s_repeatDelaySec = 0.2f;
            Mount();

            // Act — 1.0s of ticks: past the 0.6s end, with no trailing gap and none of the overshoot counted.
            AdvancePast(1f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.6f).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeIterationsWithAGapLoweredToTwoDuringTheThirdPass_When_TheNextFramesTick_Then_TimeSecIsTwoPassesAndOneGap()
        {
            // Arrange — the passes span 0-0.2s, 0.3-0.5s and 0.6-0.8s; the clock is at 0.672s.
            s_steps = TwoLabels();
            s_iterations = 3;
            s_repeatDelaySec = 0.1f;
            Mount();
            AdvancePast(0.65f);
            s_iterations = 2;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeIterationsLoweredToZeroDuringTheSecondPass_When_TheNextFramesTick_Then_TimeSecIsZero()
        {
            // Arrange
            LowerDuringSecondPass(0);

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_controls.TimeSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_TwoIterationsEndingInAnAwaitStep_When_TheFirstWaitSettles_Then_TheSecondPassArrivesAtItsOwnWait()
        {
            // Arrange
            s_steps = LabelThenAwait();
            s_iterations = 2;
            Mount();
            AdvancePast(0.2f);

            // Act
            SettleAndAdvance(0, 0.2f);

            // Assert
            Assert.That(s_sources.Count, Is.EqualTo(2));
        }

        [Test]
        public void Given_TwoIterationsEndingInAnAwaitStep_When_BothWaitsSettle_Then_TheSequenceIsComplete()
        {
            // Arrange
            s_steps = LabelThenAwait();
            s_iterations = 2;
            Mount();
            AdvancePast(0.2f);
            SettleAndAdvance(0, 0.2f);

            // Act
            SettleAndAdvance(1, 0.1f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_TwoIterationsEndingInAnAwaitStep_When_TheSecondWaitStaysPendingForSeconds_Then_TimeSecIsTheTwoLabelHolds()
        {
            // Arrange
            s_steps = LabelThenAwait();
            s_iterations = 2;
            Mount();
            AdvancePast(0.2f);
            SettleAndAdvance(0, 0.2f);

            // Act — the clock runs a second beyond the pending wait, which holds no time of its own.
            AdvancePast(1f);

            // Assert — the 0.02s tolerance is the frame's overshoot past a hold, which the wait inherits.
            Assert.That(s_controls.TimeSec, Is.EqualTo(0.1f).Within(0.02f));
        }

        [Test]
        public void Given_ThreeIterationsParkedOnTheSecondPassesWait_When_TheCountIsLoweredToOne_Then_ThatWaitIsCancelled()
        {
            // Arrange
            s_steps = LabelThenAwait();
            s_iterations = 3;
            Mount();
            AdvancePast(0.2f);
            SettleAndAdvance(0, 0.2f);
            s_iterations = 1;
            s_rerender();
            _mounted.FlushStateForTest();

            // Act
            AdvancePast(0f);

            // Assert
            Assert.That(s_tokens[1].IsCancellationRequested, Is.True);
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
