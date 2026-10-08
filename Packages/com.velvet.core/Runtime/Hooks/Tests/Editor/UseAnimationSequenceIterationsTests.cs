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
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, Array.Empty<object>(), iterations: s_iterations);
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

        [Test]
        public void Given_ThreeIterationsLoweredToZeroDuringTheFirstPass_When_TheFirstPassEnds_Then_TheSequenceIsCompleteOnTheLastStep()
        {
            // Arrange — 0.144s of ticks lands on "b", and that step change's re-render reads the lowered count.
            s_steps = TwoLabels();
            s_iterations = 3;
            Mount();
            s_iterations = 0;
            AdvancePast(0.12f);

            // Act — 0.32s in all, inside the second pass were it still playing.
            AdvancePast(0.15f);

            // Assert
            Assert.That((s_state.StepIndex, s_state.CurrentLabel, s_state.IsComplete), Is.EqualTo((1, "b", true)));
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
    }
}
