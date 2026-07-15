using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>Hooks.UseAnimationSequence</c>'s step-walk contract on the EditMode fake clock (reusing
    /// <see cref="UseFrameFakeClockHost"/>'s shared Ms/ReadFakeClock harness, since the hook is itself built on
    /// <c>UseFrame</c>): a <c>To</c> step's label/transition take effect the moment the walker arrives at it and
    /// hold until the next step's turn, a <c>Wait</c> step holds the current label with no effect of its own, a
    /// <c>Call</c> step fires synchronously on arrival, and <c>controls</c> / <c>loop</c> behave as documented.
    /// </summary>
    internal sealed class UseAnimationSequenceTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static bool s_autoplay;
        private static bool s_loop;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;
        private static int s_callCount;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_autoplay = true;
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
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, autoplay: s_autoplay, loop: s_loop);
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        // Mounts on the fake clock, flushes the mount effect (Reset + the resulting re-render) and arms
        // UseFrame's own tick, mirroring UseFramePerFrameContractTests' own arm sequence.
        private void Mount()
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(SequenceHost, key: "root"));
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        // Steps the fake clock forward in small increments well past `seconds`, then flushes whatever state
        // update UseFrame's tick queued along the way — mirrors MotionSimulatedPanelTestsBase.AdvancePast.
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

        [Test]
        public void Given_ATwoStepSequence_When_Mounted_Then_CurrentLabelIsAlreadyTheFirstSteps()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.5f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.2f }),
            };

            // Act
            Mount();

            // Assert — the walker's Reset (run by the mount effect) commits step 0 before the first paint,
            // so the very first observable render already reflects it rather than a null "not started yet".
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_TwoToStepsWithATweenTransition_When_TheFirstStepsHoldElapses_Then_CurrentLabelSwitchesToTheSecondStep()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.3f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.2f }),
            };
            Mount();
            Assume.That(s_state.CurrentLabel, Is.EqualTo("a"), "Precondition: step 0 is current right after mount");

            // Act
            AdvancePast(0.3f);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_AWaitStepBetweenTwoToSteps_When_OnlyThePrecedingStepsHoldHasElapsed_Then_CurrentLabelStaysAtThePriorStepThroughTheWait()
        {
            // Arrange — step 0's hold (0.2s) elapses, landing inside the 0.5s Wait; step 1 must not have
            // become current yet.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.2f }),
                AnimationSequenceStep.Wait(0.5f),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.1f }),
            };
            Mount();

            // Act
            AdvancePast(0.3f);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_ACallStepBetweenTwoToSteps_When_TheWalkerCrossesIt_Then_TheCallbackHasFiredByTheTimeTheNextToStepIsCurrent()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.2f }),
                AnimationSequenceStep.Call(() => s_callCount++),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 1f }),
            };
            Mount();

            // Act
            AdvancePast(0.2f);

            // Assert — a zero-hold Call step is crossed in the same Advance() as the To step right after it,
            // so both are already true together once the clock has passed step 0's own hold.
            Assert.That((s_callCount, s_state.CurrentLabel), Is.EqualTo((1, "b")));
        }

        [Test]
        public void Given_ANonLoopingSequence_When_TheLastStepsHoldElapses_Then_IsCompleteBecomesTrue()
        {
            // Arrange
            s_steps = new[] { AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.1f }) };
            s_loop = false;
            Mount();

            // Act
            AdvancePast(0.1f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_ControlsPauseCalledRightAfterMount_When_TimeAdvancesPastTheFirstStepsHold_Then_StepIndexDoesNotAdvance()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.1f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.1f }),
            };
            Mount();
            s_controls.Pause();

            // Act
            AdvancePast(0.5f);

            // Assert
            Assert.That(s_state.StepIndex, Is.EqualTo(0));
        }

        [Test]
        public void Given_ALoopingTwoStepSequence_When_TimeAdvancesPastBothHolds_Then_TheCursorWrapsBackToStepZero()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.1f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.1f }),
            };
            s_loop = true;
            Mount();

            // Act — past both holds (0.2s total) and back around into step 0's own hold again.
            AdvancePast(0.25f);

            // Assert
            Assert.That((s_state.StepIndex, s_state.CurrentLabel), Is.EqualTo((0, "a")));
        }

        [Test]
        public void Given_TheHostUnmountsMidSequence_When_TheSchedulerContinuesTicking_Then_NoExceptionIsThrown()
        {
            // Arrange
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.5f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.2f }),
            };
            Mount();
            _mounted.Dispose();
            _mounted = null;

            // Act & Assert — UseFrame's own unmount contract stops the tick; nothing here should throw even
            // though the fake clock keeps advancing well past where step 1 would otherwise have become current.
            Assert.DoesNotThrow(() =>
            {
                for (var i = 0; i < 40; i++)
                {
                    UseFrameFakeClockHost.Ms += 16;
                    EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
                }
            });
        }
    }
}
