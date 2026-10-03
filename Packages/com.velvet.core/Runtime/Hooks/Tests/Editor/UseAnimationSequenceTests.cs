using System;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>Hooks.UseAnimationSequence</c>'s step-walk contract on the EditMode fake clock (reusing
    /// <see cref="UseFrameFakeClockHost"/>'s shared Ms/ReadFakeClock harness, since the hook is itself built on
    /// <c>UseFrame</c>): a <c>To</c> step's label/transition take effect the moment the walker arrives at it and
    /// hold until the next step's turn, a <c>Wait</c> step holds the current label with no effect of its own, a
    /// <c>Call</c> step fires synchronously on arrival, a null dependency list restarts the sequence on every render,
    /// and <c>controls</c> / <c>loop</c> behave as documented.
    /// </summary>
    internal sealed class UseAnimationSequenceTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static AnimationSequenceStep[] s_steps;
        private static bool s_autoplay;
        private static bool s_loop;
        private static object[] s_deps;
        private static AnimationSequenceState s_state;
        private static AnimationSequenceControls s_controls;
        private static int s_callCount;
        private static int s_renderCount;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_autoplay = true;
            s_loop = false;
            s_deps = System.Array.Empty<object>();
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
        private static VNode SequenceHost()
        {
            s_renderCount++;
            var (state, controls) = Hooks.UseAnimationSequence(s_steps, deps: s_deps, autoplay: s_autoplay, loop: s_loop);
            s_state = state;
            s_controls = controls;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        [Component]
        private static VNode DefaultsSequenceHost()
        {
            var (state, _) = Hooks.UseAnimationSequence(s_steps, deps: s_deps);
            s_state = state;
            return V.Div(className: "w-[10px] h-[10px]");
        }

        // Mounts on the fake clock, flushes the mount effect (Reset + the resulting re-render) and arms
        // UseFrame's own tick, mirroring UseFrameDispatcherBehaviorTests' per-frame-contract arm sequence.
        private void Mount() => Mount(SequenceHost);

        private void Mount(Func<VNode> host)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(host, key: "root"));
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

            // Assert — Mount() flushes the mount effect (which runs Reset, committing step 0) before ever
            // reading s_state. In production the effect is still post-paint like any UseEffect, so the actual
            // first painted frame shows no active label yet; this pins the state this test's own flushed
            // helper observes, not a claim about literal first-paint timing.
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

        // GREEN_ON_BASE(characterization): autoplay on and loop off by default, which the base has and
        // the reordered parameters keep.
        [Test]
        public void Given_AutoplayAndLoopLeftToTheirDefaults_When_TheOnlyStepsHoldElapses_Then_IsCompleteBecomesTrue()
        {
            // Arrange
            s_steps = new[] { AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.1f }) };
            Mount(DefaultsSequenceHost);

            // Act
            AdvancePast(0.1f);

            // Assert
            Assert.That(s_state.IsComplete, Is.True);
        }

        [Test]
        public void Given_ANullDependencyList_When_ARenderAfterTheWalkerLeftStepZeroCommitsItsEffects_Then_TheSequenceIsBackAtStepZero()
        {
            // Arrange — the advance into step 1 is itself a render, and with no list the reset effect is staged
            // on every render.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.3f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.2f }),
            };
            s_deps = null;
            Mount();
            AdvancePast(0.3f);

            // Act
            _mounted.FlushEffectsForTest();
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
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

        [Test]
        public void Given_ASingleClockJumpSpanningTwoHolds_When_Advanced_Then_TheOvershootCarriesIntoTheThirdStepInsteadOfStallingAtTheSecond()
        {
            // Arrange — three 50ms holds; a single 120ms jump should cross step 0 AND step 1 (50ms each, 100ms
            // total) with 20ms left over into step 2, landing on "c" in one tick rather than stalling on "b".
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.05f }),
                AnimationSequenceStep.To("c", new StyleTransitionConfig { DurationSec = 0.05f }),
            };
            Mount();

            // Act — one single large jump, one single scheduler drive (not the small-increment AdvancePast
            // helper, which would never exercise a multi-hold crossing within one Advance() call).
            UseFrameFakeClockHost.Ms += 120;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("c"));
        }

        [Test]
        public void Given_ASingleStepCallSequenceThatLoops_When_TimeAdvances_Then_TheComponentKeepsReRenderingOnEachRecommit()
        {
            // Arrange — a 1-step loop wraps back to the SAME index (0) on every recommit, so a re-render
            // trigger keyed on "did StepIndex change" would never fire again after the first tick.
            s_steps = new[] { AnimationSequenceStep.Call(() => s_callCount++) };
            s_loop = true;
            Mount();
            var renderCountAfterMount = s_renderCount;
            Assume.That(s_callCount, Is.GreaterThan(0), "Precondition: step 0's callback already fired once on mount");

            // Act — a zero-hold step re-arrives every tick regardless of dt.
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.GreaterThan(renderCountAfterMount));
        }

        [Test]
        public void Given_AToStepWithAPropertyOverrideLongerThanTheTopLevelDuration_When_OnlyTheTopLevelDurationHasElapsed_Then_TheStepIsStillCurrent()
        {
            // Arrange — the top-level DurationSec (50ms) is shorter than the "translate" override's own (300ms);
            // the auto-derived hold must follow the slower override, matching StyleAnimationScheduler's own
            // "completion sized off the slowest overridden property" rule for the same StyleTransitionConfig.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig
                {
                    DurationSec = 0.05f,
                    PropertyOverrides = new[] { new StylePropertyTransition("translate", durationSec: 0.3f) },
                }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.05f }),
            };
            Mount();

            // Act — past the top-level 50ms but well short of the override's 300ms.
            AdvancePast(0.1f);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_ABezierToStepWithAPropertyOverrideLongerThanTheTopLevelDuration_When_TheTopLevelDurationHasElapsed_Then_TheWalkerHasAdvancedPastIt()
        {
            // Arrange — a Bezier tween drives every channel with one fixed-duration curve and never reads
            // PropertyOverrides (the same contract as a spring's single stiffness/damping/mass), so the
            // auto-derived hold must be the fixed 50ms DurationSec, NOT the 300ms override a plain Tween would
            // follow — otherwise the sequence stalls on the step long past when the tween it describes finished.
            s_steps = new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig
                {
                    Type = TransitionType.Bezier,
                    DurationSec = 0.05f,
                    PropertyOverrides = new[] { new StylePropertyTransition("translate", durationSec: 0.3f) },
                }),
                AnimationSequenceStep.To("b", new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.5f }),
            };
            Mount();
            Assume.That(s_state.CurrentLabel, Is.EqualTo("a"), "Precondition: step 0 is current right after mount");

            // Act — past the top-level 50ms but well short of the override's 300ms.
            AdvancePast(0.1f);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
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

        private static AnimationSequenceStep[] SpringThenB(StyleTransitionConfig spring) => new[]
        {
            AnimationSequenceStep.To("a", spring),
            AnimationSequenceStep.To("b", new StyleTransitionConfig { DurationSec = 0.05f }),
        };

        private static StyleTransitionConfig Spring(float stiffness, float damping, float mass, float delaySec = 0f)
            => new() { Type = TransitionType.Spring, Stiffness = stiffness, Damping = damping, Mass = mass, DelaySec = delaySec };

        // The 16ms ticks inside a hold.
        private static int TicksIn(float seconds) => (int)(seconds * 1000f / 16f);

        // Each row's seconds are the duration Framer Motion's sequence gives that spring: its generator sampled
        // every 50ms over a 0→100 travel until within 0.5 of the target at a speed of at most 2 per second.
        [TestCase(100f, 10f, 1f, 1.05f)]
        [TestCase(170f, 26f, 1f, 0.70f)]
        [TestCase(100f, 20f, 1f, 0.85f)]
        [TestCase(100f, 125f, 1f, 4.9f)]
        public void Given_ASpringToStepWithNoHold_When_TimeStopsShortOfFramersDuration_Then_TheStepIsStillCurrent(
            float stiffness, float damping, float mass, float framerSec)
        {
            // Arrange
            s_steps = SpringThenB(Spring(stiffness, damping, mass));
            Mount();

            // Act
            AdvanceTicks(TicksIn(framerSec) - 3);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        // GREEN_ON_BASE(characterization): the base's fixed 0.5s hold has passed by then as well; `SpringRestSpeed`
        // at 0.2 reddens the first three rows, holding them to 1.45s, 0.9s and 1.1s.
        [TestCase(100f, 10f, 1f, 1.05f)]
        [TestCase(170f, 26f, 1f, 0.70f)]
        [TestCase(100f, 20f, 1f, 0.85f)]
        [TestCase(100f, 125f, 1f, 4.9f)]
        public void Given_ASpringToStepWithNoHold_When_TimePassesFramersDuration_Then_TheNextStepIsCurrent(
            float stiffness, float damping, float mass, float framerSec)
        {
            // Arrange
            s_steps = SpringThenB(Spring(stiffness, damping, mass));
            Mount();

            // Act
            AdvanceTicks(TicksIn(framerSec) + 4);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_AStiffSpringToStepWithNoHold_When_TimePassesFramersDuration_Then_TheNextStepIsCurrent()
        {
            // Arrange — Framer gives this spring 0.30s, and 22 ticks is still short of half a second.
            s_steps = SpringThenB(Spring(500f, 40f, 1f));
            Mount();

            // Act
            AdvanceTicks(TicksIn(0.30f) + 4);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        [Test]
        public void Given_ASpringToStepWithADelay_When_TimePassesTheDurationButNotTheDelayOnTop_Then_TheStepIsStillCurrent()
        {
            // Arrange — 1.05s of spring, then 0.4s of delay on top.
            s_steps = SpringThenB(Spring(100f, 10f, 1f, delaySec: 0.4f));
            Mount();

            // Act
            AdvanceTicks(TicksIn(1.45f) - 3);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("a"));
        }

        [Test]
        public void Given_ASpringToStepWithANegativeDelay_When_TimePassesTheDurationLessTheDelay_Then_TheNextStepIsCurrent()
        {
            // Arrange
            s_steps = SpringThenB(Spring(100f, 10f, 1f, delaySec: -0.4f));
            Mount();

            // Act
            AdvanceTicks(TicksIn(0.65f) + 4);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        // Each parameter at zero and at infinity: a play warns and completes at once, so the step holds nothing.
        [TestCase(0f, 10f, 1f)]
        [TestCase(float.PositiveInfinity, 10f, 1f)]
        [TestCase(100f, 0f, 1f)]
        [TestCase(100f, float.PositiveInfinity, 1f)]
        [TestCase(100f, 10f, 0f)]
        [TestCase(100f, 10f, float.PositiveInfinity)]
        public void Given_ASpringToStepAPlayRefusesToTick_When_TwoTicksPass_Then_TheNextStepIsCurrent(
            float stiffness, float damping, float mass)
        {
            // Arrange
            s_steps = SpringThenB(new StyleTransitionConfig
            {
                Type = TransitionType.Spring, Stiffness = stiffness, Damping = damping, Mass = mass,
            });
            Mount();

            // Act
            AdvanceTicks(2);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base's fixed 0.5s hold has passed by then as well; the cap
        // compared with `<=`, which samples once more past 20s, is what reddens it.
        [Test]
        public void Given_ASpringToStepThatWouldRunForADay_When_TwentySecondsPass_Then_TheNextStepIsCurrent()
        {
            // Arrange — Framer Motion times a spring over at most 20 seconds; this one takes over a day to settle.
            s_steps = SpringThenB(Spring(1f, 0.0001f, 1f));
            Mount();

            // Act — 20s is 1250 ticks, and a sample past it would hold to 20.05s.
            AdvanceTicks(1252);

            // Assert
            Assert.That(s_state.CurrentLabel, Is.EqualTo("b"));
        }
    }
}
