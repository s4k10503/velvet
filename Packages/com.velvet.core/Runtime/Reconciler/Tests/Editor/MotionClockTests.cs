using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="MotionClock"/>: a mount's motion advances by the clock it chooses — the plays of every
    /// transition type, a Tween's included, and <c>layoutId</c> moves — so a clock that holds still holds it while
    /// the panel keeps ticking, and a clock stepped by one frame moves it by one frame.
    /// </summary>
    [TestFixture]
    internal sealed class MotionClockTests : MotionSimulatedPanelTestsBase
    {
        private const double FrameSec = HeldMotionClock.FrameSec;

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private static readonly StyleTransitionConfig s_linear = new()
        {
            Type = TransitionType.Bezier, DurationSec = 1f,
            BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
        };

        private static readonly StyleTransitionConfig s_linearTween = new() { DurationSec = 1f, Easing = EasingMode.Linear };

        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            _scheduler = null;
            base.TearDown();
            ResetMountCount();
        }

        // A case that retains a mount itself, or fails before its mount is disposed, would otherwise leave every later
        // fixture paying for and reading the element record.
        private static void ResetMountCount()
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            typeof(MotionClock).GetField("s_liveMounts", flags)!.SetValue(null, 0);
            var recorded = typeof(MotionClock).GetField("s_recorded", flags)!.GetValue(null);
            recorded.GetType().GetMethod("Clear")!.Invoke(recorded, null);
        }

        private VisualElement OnPanel(string name)
        {
            var element = new VisualElement { name = name };
            Root.Add(element);
            return element;
        }

        private void HeldTicks(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        private void Step(HeldMotionClock clock, double seconds)
        {
            clock.Now += seconds;
            Tick();
        }

        [Test]
        public void Given_ABezierPlayOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("fade");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, s_linear);

            // Act
            HeldTicks(10);
            var held = element.style.opacity.value;
            Step(clock, FrameSec);

            // Assert
            Assert.That(new[] { held, element.style.opacity.value },
                Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        [Test]
        public void Given_ASpringPlayOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("spring");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig { Type = TransitionType.Spring };
            var reference = new VisualElement();
            var referenceState = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(
                new[] { "opacity-0" }, new[] { "opacity-100" }), config.Stiffness, config.Damping, config.Mass);
            MotionSpringDriver.Step(reference, referenceState, (float)FrameSec);
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Act
            HeldTicks(10);
            var held = element.style.opacity.value;
            Step(clock, FrameSec);

            // Assert
            Assert.That(new[] { held, element.style.opacity.value },
                Is.EqualTo(new[] { 0f, reference.style.opacity.value }).Within(1e-5f));
        }

        // The clock reaches the delay exactly on the release step, so a play still waiting there would start a
        // frame late. The last step matches the panel's own 16 ms frame, so a play whose tick stepped by the
        // panel rather than by the clock lands on the same value, and the case reads where the delay was counted.
        [Test]
        public void Given_ADelayedBezierPlayOnAClockHeldPartWayThroughItsDelay_When_ThePanelTicksPastTheDelay_Then_ItStartsOnceTheClockHasCoveredIt()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("delayed");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = (float)(8 * FrameSec),
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);
            for (var i = 0; i < 3; i++) Step(clock, FrameSec);
            HeldTicks(20);

            // Act
            Step(clock, 5 * FrameSec);
            Step(clock, 0.016);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.016f).Within(1e-5f));
        }

        [Test]
        public void Given_AnAnimateLoopOnAHeldClock_When_TheClockMovesAQuarterLoop_Then_TheLoopShowsAQuarterTurn()
        {
            // Arrange — a start whose double is not integral, so a phase read from the sum of the two readings
            // lands elsewhere than one read from their difference.
            var clock = new HeldMotionClock(100.3);
            var element = new VisualElement();
            var loop = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Spin, 1f), panVertical: false, clock);
            try
            {
                // Act
                clock.Now += 0.25;
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.None);

                // Assert
                Assert.That(element.style.rotate.value.angle.value, Is.EqualTo(90f).Within(1e-3f));
            }
            finally
            {
                StyleAnimateDriver.Detach(element, loop);
            }
        }

        [Test]
        public void Given_AnAnimateLoopAttachedOffThePanelOnAHeldClock_When_ItJoinsThePanelAfterTheClockMoved_Then_ItsPhaseCountsFromTheJoin()
        {
            // Arrange — a quarter loop passes on the clock before the element joins the panel.
            var clock = new HeldMotionClock();
            var element = new VisualElement();
            var loop = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Spin, 1f), panVertical: false, clock);
            try
            {
                clock.Now += 0.25;
                Root.Add(element);

                // Act
                clock.Now += 0.25;
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.None);

                // Assert — a quarter turn from the join, where counting from the attach would show half.
                Assert.That(element.style.rotate.value.angle.value, Is.EqualTo(90f).Within(1e-3f));
            }
            finally
            {
                StyleAnimateDriver.Detach(element, loop);
            }
        }

        [Component]
        private static VNode FadeIn() => V.Motion(name: "fade", variants: s_fade, initial: "hidden", animate: "visible",
            transition: s_linear);

        [Test]
        public void Given_AMountNamingAHeldClock_When_ItsMountEnterRunsWhileThePanelTicks_Then_TheEnterHoldsUntilTheClockSteps()
        {
            // Arrange
            var clock = new HeldMotionClock();
            using var mounted = V.Mount(Root, V.Component(FadeIn, key: "root"), new MountOptions { MotionClock = clock });
            var element = Root.Q<VisualElement>("fade");

            // Act
            HeldTicks(10);
            var held = element.style.opacity.value;
            Step(clock, FrameSec);

            // Assert
            Assert.That(new[] { held, element.style.opacity.value },
                Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        [Test]
        public void Given_AMountNamingAHeldClock_When_AnAnimateLoopMounts_Then_TheLoopRunsOnThatClock()
        {
            // Arrange
            var clock = new HeldMotionClock();
            using var mounted = V.Mount(Root, V.Div(name: "spinner", className: "animate-spin"),
                new MountOptions { MotionClock = clock });
            var element = Root.Q<VisualElement>("spinner");

            // Act
            Step(clock, 0.25);

            // Assert
            Assert.That(element.style.rotate.value.angle.value, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Given_AMountNamingAHeldClock_When_ARenderSwapsOneAnimateLoopForAnother_Then_TheNewLoopRunsOnThatClock()
        {
            // Arrange
            var clock = new HeldMotionClock();
            using var mounted = V.Mount(Root, V.Div(name: "spinner", className: "animate-pulse"),
                new MountOptions { MotionClock = clock });
            var element = Root.Q<VisualElement>("spinner");
            mounted.Render(V.Div(name: "spinner", className: "animate-spin"));

            // Act
            Step(clock, 0.25);

            // Assert
            Assert.That(element.style.rotate.value.angle.value, Is.EqualTo(90f).Within(1e-3f));
        }

        // --- Tween, which UI Toolkit's transition runs on the panel's time unless the clock drives it ---

        // The inline opacity the play shows while the panel ticks on a held clock, then once the clock steps a frame.
        private float[] OpacityHeldThenStepped(HeldMotionClock clock, VisualElement element)
        {
            HeldTicks(10);
            var held = element.style.opacity.value;
            Step(clock, FrameSec);
            return new[] { held, element.style.opacity.value };
        }

        [Test]
        public void Given_ATweenVariantEnterOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("tween");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, s_linearTween);

            // Act
            var opacities = OpacityHeldThenStepped(clock, element);

            // Assert
            Assert.That(opacities, Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        // The exit's own curve, so a play eased by the enter's, or by the bezier's control points, lands elsewhere.
        [Test]
        public void Given_ATweenExitOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOneFrameAlongItsExitCurve()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, ExitEasing = EasingMode.EaseIn,
                ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            };
            _scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);

            // Act
            var opacities = OpacityHeldThenStepped(clock, element);

            // Assert
            Assert.That(opacities,
                Is.EqualTo(new[] { 1f, 1f - UssEasing.Evaluate(EasingMode.EaseIn, (float)FrameSec) }).Within(1e-6f));
        }

        [Test]
        public void Given_APresetTweenEnterOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("preset");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            _scheduler.PlayEnter(element, StyleTransition.Fade.With(durationSec: 1f, easing: EasingMode.Linear));

            // Act
            var opacities = OpacityHeldThenStepped(clock, element);

            // Assert
            Assert.That(opacities, Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        [Test]
        public void Given_APresetTweenEnterOnAHeldClock_When_TheClockRunsPastItsDuration_Then_ItsTransientClassComesOff()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("preset");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            _scheduler.PlayEnter(element, StyleTransition.Fade.With(durationSec: 1f));
            HeldTicks(2);
            var during = element.ClassListContains("anim-fade-enter-to");

            // Act
            Step(clock, 1.0);

            // Assert
            Assert.That(new[] { during, element.ClassListContains("anim-fade-enter-to") }, Is.EqualTo(new[] { true, false }));
        }

        // A class the driver reads no value from still holds the play's completion for the play's duration, as the
        // transition's own completion does.
        [Test]
        public void Given_ATweenWhoseClassesNameNothingTheDriverReads_When_TheClockHoldsAndThenRunsPastItsDuration_Then_ItCompletesOnlyThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("unread");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            _scheduler.PlayVariantEnter(element, new[] { "pose-a" }, new[] { "pose-b" }, s_linearTween,
                onComplete: () => completions++);

            // Act
            HeldTicks(10);
            var held = completions;
            Step(clock, 1.0);

            // Assert
            Assert.That(new[] { held, completions }, Is.EqualTo(new[] { 0, 1 }));
        }

        // A fade and a 64px translate over a linear second, a pixel a frame, on the overrides given.
        private VisualElement OverriddenTween(HeldMotionClock clock, params StylePropertyTransition[] overrides)
        {
            var element = OnPanel("override");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.Linear, PropertyOverrides = overrides };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" }, config);
            return element;
        }

        private float[] OpacityAndTranslateAfterAFrame(HeldMotionClock clock, VisualElement element)
        {
            HeldTicks(10);
            Step(clock, FrameSec);
            return new[] { element.style.opacity.value, element.style.translate.value.x.value };
        }

        [Test]
        public void Given_ATweenWithAShorterOpacityOverride_When_TheClockStepsAFrame_Then_OpacityMovesOnItsOwnTimingAndTranslateOnTheTopLevelOne()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OverriddenTween(clock, new StylePropertyTransition("opacity", durationSec: 0.5f, easing: EasingMode.EaseIn));

            // Act
            var values = OpacityAndTranslateAfterAFrame(clock, element);

            // Assert
            Assert.That(values,
                Is.EqualTo(new[] { UssEasing.Evaluate(EasingMode.EaseIn, (float)(2 * FrameSec)), 1f }).Within(1e-6f));
        }

        [Test]
        public void Given_ATweenWithTwoOverridesNamingOpacity_When_TheClockStepsAFrame_Then_TheLaterOneTimesIt()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OverriddenTween(clock, new StylePropertyTransition("opacity", durationSec: 0.25f),
                new StylePropertyTransition("opacity", durationSec: 0.5f));

            // Act
            var values = OpacityAndTranslateAfterAFrame(clock, element);

            // Assert
            Assert.That(values, Is.EqualTo(new[] { (float)(2 * FrameSec), 1f }).Within(1e-5f));
        }

        [Test]
        public void Given_ATweenWithAnOverrideNamingAll_When_TheClockStepsAFrame_Then_ItTimesEveryProperty()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OverriddenTween(clock, new StylePropertyTransition("all", durationSec: 0.5f));

            // Act
            var values = OpacityAndTranslateAfterAFrame(clock, element);

            // Assert
            Assert.That(values, Is.EqualTo(new[] { (float)(2 * FrameSec), 2f }).Within(1e-5f));
        }

        [Test]
        public void Given_ATweenWithAnOverrideNamingTheShorthandOfItsLength_When_TheClockStepsAFrame_Then_ItTimesTheLonghand()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("padded");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("padding", durationSec: 0.5f) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "pl-[0px]" }, new[] { "pl-[64px]" }, config);

            // Act
            HeldTicks(10);
            Step(clock, FrameSec);

            // Assert — two pixels, where the top-level second would have moved it one.
            Assert.That(element.style.paddingLeft.value.value, Is.EqualTo(2f).Within(1e-4f));
        }

        // The override's delay is shorter than the top-level one, and neither is zero, so the tick starts on the
        // override's delay and the top-level one is counted from there.
        [Test]
        public void Given_ATweenWhoseOpacityOverrideHasAShorterDelay_When_TheClockCoversBothDelays_Then_EachPropertyStartsOnItsOwn()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("delays");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, DelaySec = (float)(8 * FrameSec),
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", delaySec: (float)(4 * FrameSec)) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" }, config);

            // Act
            HeldTicks(10);
            Step(clock, 4 * FrameSec);
            Step(clock, 5 * FrameSec);

            // Assert — opacity five frames in, translate one.
            Assert.That(new[] { element.style.opacity.value, element.style.translate.value.x.value },
                Is.EqualTo(new[] { (float)(5 * FrameSec), 1f }).Within(1e-5f));
        }

        [Test]
        public void Given_ATweenOnAHeldClock_When_TheClockStepsTwoFrames_Then_ItHasMovedTwoFrames()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("tween");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, s_linearTween);

            // Act
            Step(clock, FrameSec);
            Step(clock, FrameSec);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo((float)(2 * FrameSec)).Within(1e-5f));
        }

        [Test]
        public void Given_AZeroDurationTweenOnAHeldClock_When_ItPlays_Then_ItCompletesAtOnce()
        {
            // Arrange
            var element = OnPanel("instant");
            _scheduler = new StyleAnimationScheduler { Clock = new HeldMotionClock() };
            var completions = 0;

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { DurationSec = 0f }, onComplete: () => completions++);

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        // Whether the play completed once the clock ran past its duration, and whether the element still carries
        // the class it played to.
        private bool[] CompletedHolding(HeldMotionClock clock, VisualElement element, string toClass,
            ref int completions)
        {
            Step(clock, 1.0);
            return new[] { completions == 1, element.ClassListContains(toClass) };
        }

        [Test]
        public void Given_ATweenVariantEnterOnAHeldClock_When_ItCompletes_Then_ItsRestingClassStays()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("tween");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, s_linearTween,
                onComplete: () => completions++);

            // Act
            var held = CompletedHolding(clock, element, "opacity-100", ref completions);

            // Assert
            Assert.That(held, Is.EqualTo(new[] { true, true }));
        }

        [Test]
        public void Given_ABezierVariantEnterOnAHeldClock_When_ItCompletes_Then_ItsRestingClassStays()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("bezier");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, s_linear,
                onComplete: () => completions++);

            // Act
            var held = CompletedHolding(clock, element, "opacity-100", ref completions);

            // Assert
            Assert.That(held, Is.EqualTo(new[] { true, true }));
        }

        // The pose stays until whoever waits on the exit removes the element.
        [Test]
        public void Given_ATweenExitOnAHeldClock_When_ItCompletes_Then_ItsExitPoseStays()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            var config = new StyleTransitionConfig { DurationSec = 1f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0" };
            _scheduler.PlayExit(element, config, onComplete: () => completions++, restoreFromOnCancel: true);

            // Act
            var held = CompletedHolding(clock, element, "opacity-0", ref completions);

            // Assert
            Assert.That(held, Is.EqualTo(new[] { true, true }));
        }

        // The opacity a play shows once the clock has covered its delay and stagger slot, four frames each, and
        // once it is a frame past both.
        private float[] OpacityThroughDelayAndSlot(HeldMotionClock clock, VisualElement element)
        {
            Step(clock, 4 * FrameSec);
            var inSlot = element.style.opacity.value;
            Step(clock, 4 * FrameSec);
            Step(clock, FrameSec);
            return new[] { inSlot, element.style.opacity.value };
        }

        [Test]
        public void Given_ABezierWithADelayAndAStaggerSlotOnAHeldClock_When_TheClockCoversBoth_Then_ItStartsOnlyThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("staggered");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = (float)(4 * FrameSec),
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                additionalDelaySec: (float)(4 * FrameSec));

            // Act
            var opacities = OpacityThroughDelayAndSlot(clock, element);

            // Assert
            Assert.That(opacities, Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        [Test]
        public void Given_ATweenWithADelayAndAStaggerSlotOnAHeldClock_When_TheClockCoversBoth_Then_ItStartsOnlyThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("staggered");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.Linear, DelaySec = (float)(4 * FrameSec) };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                additionalDelaySec: (float)(4 * FrameSec));

            // Act
            var opacities = OpacityThroughDelayAndSlot(clock, element);

            // Assert
            Assert.That(opacities, Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        // --- every channel kind the driver writes ---

        private static readonly string[] s_restPose =
            { "opacity-100", "translate-x-[64px]", "translate-y-[128px]", "scale-[2]", "rotate-[64deg]", "bg-[#ffffff]", "w-[64px]" };

        private static readonly string[] s_gonePose =
            { "opacity-0", "translate-x-[0px]", "translate-y-[0px]", "scale-[1]", "rotate-[0deg]", "bg-[#000000]", "w-[0px]" };

        // Each channel's value as the fraction of the way it stands from the gone pose to the rest pose.
        private static float[] RestFractions(VisualElement element) => new[]
        {
            element.style.opacity.value,
            element.style.translate.value.x.value / 64f,
            element.style.translate.value.y.value / 128f,
            element.style.scale.value.value.x - 1f,
            element.style.rotate.value.angle.value / 64f,
            element.style.backgroundColor.value.r,
            element.style.width.value.value / 64f,
        };

        [Test]
        public void Given_ATweenWhoseOverrideNamesAllOfEveryChannelKind_When_TheClockStepsAFrame_Then_EachMovesOnTheOverride()
        {
            // Arrange — half a second in place of the top-level one moves each two frames' worth.
            var clock = new HeldMotionClock();
            var element = OnPanel("every");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("all", durationSec: 0.5f) },
            };
            _scheduler.PlayVariantEnter(element, s_gonePose, s_restPose, config);

            // Act
            Step(clock, FrameSec);

            // Assert
            Assert.That(RestFractions(element), Is.EqualTo(new float[7].Select(_ => (float)(2 * FrameSec)).ToArray()).Within(1e-4f));
        }

        // A quarter of the way out on a linear curve, the cancel turns every channel back toward the rest pose from where
        // it stands, over a quarter of the duration: the reversal its native transition would take.
        [Test]
        public void Given_ATweenExitOfEveryChannelKindOnAHeldClock_When_ItIsCancelledAQuarterOfTheWayOut_Then_EachTurnsBackOverTheShortenedDuration()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                ExitFromClass = string.Join(" ", s_restPose), ExitToClass = string.Join(" ", s_gonePose),
            };
            _scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Step(clock, 0.25);

            // Act
            _scheduler.CancelExit(element);
            Step(clock, FrameSec);

            // Assert
            Assert.That(RestFractions(element),
                Is.EqualTo(new float[7].Select(_ => 0.75f + 0.25f * (float)(FrameSec / 0.25)).ToArray()).Within(1e-4f));
        }

        [Test]
        public void Given_ATweenExitCancelledAQuarterOfTheWayOut_When_TheClockRunsPastItsShortenedReversal_Then_ItHandsItsSlotBack()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            };
            _scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Step(clock, 0.25);
            _scheduler.CancelExit(element);

            // Act — past the quarter second the reversal takes, short of the second the exit took.
            Step(clock, 0.5);

            // Assert — the inline opacity is released, which the reversal does as it ends.
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // The exit starts a quarter of the way in, and its cancel turns back at once: the reversal's delay, a quarter
        // of a second before its start, is shortened with its duration, so it starts a sixteenth of a second in.
        [Test]
        public void Given_ATweenExitWithANegativeDelayOnAHeldClock_When_ItIsCancelledAtOnce_Then_TheReversalStartsOnItsShortenedDelay()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, DelaySec = -0.25f,
                ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            };
            _scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);

            // Act
            _scheduler.CancelExit(element);
            Step(clock, FrameSec);

            // Assert — from 0.75 toward 1 over a quarter second, a frame and a sixteenth of a second in.
            Assert.That(element.style.opacity.value,
                Is.EqualTo(0.75f + 0.25f * (float)((FrameSec + 0.0625) / 0.25)).Within(1e-4f));
        }

        // --- completion and delays under PropertyOverrides ---

        [Test]
        public void Given_ATweenWithALongerOpacityOverride_When_TheClockRunsPastTheTopLevelDuration_Then_ItCompletesOnlyOnceTheOverrideHasRun()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("longer");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 2f) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                onComplete: () => completions++);

            // Act
            Step(clock, 1.0);
            var atTopLevelEnd = completions;
            Step(clock, 1.0);

            // Assert
            Assert.That(new[] { atTopLevelEnd, completions }, Is.EqualTo(new[] { 0, 1 }));
        }

        // The override starts the tick four frames in, and the top-level timing runs a second from four frames
        // after that.
        [Test]
        public void Given_ATweenWhoseTopLevelDelayOutlastsAnOverrides_When_TheClockReachesTheOverridesEnd_Then_ItCompletesOnlyOnceTheTopLevelTimingHasRun()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("delays");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, DelaySec = (float)(8 * FrameSec),
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", delaySec: (float)(4 * FrameSec)) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" }, config, onComplete: () => completions++);
            Step(clock, 4 * FrameSec);

            // Act
            Step(clock, 1.0);
            var atOverrideEnd = completions;
            Step(clock, 4 * FrameSec);

            // Assert
            Assert.That(new[] { atOverrideEnd, completions }, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_ATweenWhoseOpacityOverrideHasALongerDelay_When_TheClockStepsPastIt_Then_OpacityStartsOnlyThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("later");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", delaySec: (float)(4 * FrameSec)) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" }, config);

            // Act
            Step(clock, 5 * FrameSec);

            // Assert — opacity a frame past its delay, translate five frames in.
            Assert.That(new[] { element.style.opacity.value, element.style.translate.value.x.value },
                Is.EqualTo(new[] { (float)FrameSec, 5f }).Within(1e-5f));
        }

        [Test]
        public void Given_ATweenWithADelayAndAnOverrideNamingNone_When_TheClockStepsAFrameIntoIt_Then_TheOverrideWaitedTheDelayToo()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("inherited");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, DelaySec = (float)(4 * FrameSec),
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.5f) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" }, config);
            Step(clock, 4 * FrameSec);

            // Act
            Step(clock, FrameSec);

            // Assert — both a frame in, opacity on its half second.
            Assert.That(new[] { element.style.opacity.value, element.style.translate.value.x.value },
                Is.EqualTo(new[] { (float)(2 * FrameSec), 1f }).Within(1e-5f));
        }

        // A zero duration lands at the end of the entry's delay, as UI Toolkit lands such an entry.
        [Test]
        public void Given_AZeroDurationOpacityOverrideWithADelay_When_TheClockReachesTheDelay_Then_OpacityLandsThen()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("landing");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0f, delaySec: (float)(4 * FrameSec)) },
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Act
            Step(clock, 3 * FrameSec);
            var before = element.style.opacity.value;
            Step(clock, FrameSec);

            // Assert
            Assert.That(new[] { before, element.style.opacity.value }, Is.EqualTo(new[] { 0f, 1f }));
        }

        [Component]
        private static VNode TweenFadeIn() => V.Motion(name: "fade", variants: s_fade, initial: "hidden",
            animate: "visible", transition: s_linearTween);

        [Test]
        public void Given_AMountNamingAHeldClock_When_ATweenMountEnterRunsWhileThePanelTicks_Then_TheEnterHoldsUntilTheClockSteps()
        {
            // Arrange
            var clock = new HeldMotionClock();
            using var mounted = V.Mount(Root, V.Component(TweenFadeIn, key: "root"), new MountOptions { MotionClock = clock });
            var element = Root.Q<VisualElement>("fade");

            // Act
            var opacities = OpacityHeldThenStepped(clock, element);

            // Assert
            Assert.That(opacities, Is.EqualTo(new[] { 0f, (float)FrameSec }).Within(1e-5f));
        }

        // --- layoutId ---

        private static StateUpdater<bool> s_setMoved;

        [Component]
        private static VNode MovingBox()
        {
            var (moved, setMoved) = Hooks.UseState(false);
            s_setMoved = setMoved;
            return V.Div(children: new VNode[]
            {
                V.Motion(name: "box", layoutId: "box", transition: s_linearTween,
                    className: moved
                        ? "absolute left-[64px] top-[0px] w-[100px] h-[100px]"
                        : "absolute left-[0px] top-[0px] w-[100px] h-[100px]"),
            });
        }

        // The move is 64px over a linear second, so the inverse translate starts at -64 and closes a pixel a frame.
        [Test]
        public void Given_ALayoutIdMoveOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldMotionClock();
            using var mounted = V.Mount(Root, V.Component(MovingBox, key: "root"), new MountOptions { MotionClock = clock });
            Tick();
            var element = Root.Q<VisualElement>("box");
            s_setMoved.Invoke(true);
            mounted.FlushStateForTest();
            Tick();

            // Act
            HeldTicks(10);
            var held = element.style.translate.value.x.value;
            Step(clock, FrameSec);

            // Assert
            Assert.That(new[] { held, element.style.translate.value.x.value },
                Is.EqualTo(new[] { -64f, -63f }).Within(1e-3f));
        }

        // --- the clock a filter-* transition reads off the element it starts on ---

        [Test]
        public void Given_AnElementUnderAnAncestorAMountRecorded_When_ItsClockIsAskedFor_Then_ItIsTheAncestors()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var ancestor = new VisualElement();
            var element = new VisualElement();
            ancestor.Add(element);
            MotionClock.RetainMount();
            MotionClock.Record(ancestor, clock);

            // Act
            var asked = MotionClock.Of(element);

            // Assert
            Assert.That(asked, Is.SameAs(clock));
        }

        [Test]
        public void Given_AnElementAMountOnAHeldClockRecorded_When_ThePoolResetsIt_Then_ItAnswersWithTheDefaultClock()
        {
            // Arrange
            var element = new VisualElement();
            MotionClock.RetainMount();
            MotionClock.Record(element, new HeldMotionClock());

            // Act
            FiberElementPoolReset.ResetCommonState(element);

            // Assert
            Assert.That(MotionClock.Of(element), Is.SameAs(MotionClock.Realtime));
        }

        // A later mount on such a clock reads the record afresh, so one the disposed mount made would answer for it.
        [Test]
        public void Given_TheOnlyMountOnAHeldClock_When_ItIsDisposedAndAnotherMounts_Then_TheElementsItRecordedAnswerWithTheDefaultClock()
        {
            // Arrange
            var mounted = V.Mount(Root, V.Div(name: "card"), new MountOptions { MotionClock = new HeldMotionClock() });
            var element = Root.Q<VisualElement>("card");
            var held = MotionClock.Of(element);

            // Act
            mounted.Dispose();
            MotionClock.RetainMount();

            // Assert
            Assert.That(new[] { held == MotionClock.Realtime, MotionClock.Of(element) == MotionClock.Realtime },
                Is.EqualTo(new[] { false, true }));
        }

        [Test]
        public void Given_AMountOnAHeldClockBesideOneOnTheDefaultClock_When_TheDefaultOneIsDisposed_Then_TheHeldOnesElementsKeepTheirClock()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var plainRoot = new VisualElement();
            Root.Add(plainRoot);
            using var mounted = V.Mount(Root, V.Div(name: "card"), new MountOptions { MotionClock = clock });
            var plain = V.Mount(plainRoot, V.Div(name: "plain"));
            var element = Root.Q<VisualElement>("card");

            // Act
            plain.Dispose();

            // Assert
            Assert.That(MotionClock.Of(element), Is.SameAs(clock));
        }

        [Test]
        public void Given_AMountsClockReleasedTwice_When_AnotherMountsRecordIsAskedFor_Then_ItStillAnswers()
        {
            // Arrange — another mount on a held clock recorded the element.
            var clock = new HeldMotionClock();
            var element = new VisualElement();
            MotionClock.RetainMount();
            MotionClock.Record(element, clock);
            var scheduler = new StyleAnimationScheduler();
            scheduler.MountOn(new HeldMotionClock());

            // Act
            scheduler.ReleaseClock();
            scheduler.ReleaseClock();

            // Assert
            Assert.That(MotionClock.Of(element), Is.SameAs(clock));
        }

        // --- the overshoot past a delay ---

        // 1/30 s steps pass the 0.05 s delay a sixtieth of a second into the second step.
        private const double ThirtiethSec = 1.0 / 30.0;
        private const float OvershootDelaySec = 0.05f;

        [Test]
        public void Given_ABezierPlayWithADelayTheClockStepsPast_When_ItTakesItsNextStep_Then_ItHasMovedByTheOvershootToo()
        {
            // Arrange
            var clock = new HeldMotionClock();
            var element = OnPanel("overshoot");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = OvershootDelaySec,
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Act
            for (var i = 0; i < 3; i++) Step(clock, ThirtiethSec);

            // Assert — three thirtieths in, less the delay; counted from the step that passed it, a thirtieth.
            Assert.That(element.style.opacity.value, Is.EqualTo((float)(3 * ThirtiethSec) - OvershootDelaySec).Within(1e-4f));
        }

        [Test]
        public void Given_ASpringPlayWithADelayTheClockStepsPast_When_ItTakesItsNextStep_Then_ItHasMovedByTheOvershootToo()
        {
            // Arrange — the reference is stepped as the play pre-rolls its overshoot, a frame at a time, then a step.
            var clock = new HeldMotionClock();
            var element = OnPanel("overshoot");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = OvershootDelaySec };
            var reference = new VisualElement();
            var referenceState = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(
                new[] { "opacity-0" }, new[] { "opacity-100" }), config.Stiffness, config.Damping, config.Mass);
            var frame = StyleAnimateDriver.TickMs / 1000f;
            for (var remaining = (float)(2 * ThirtiethSec) - OvershootDelaySec; remaining > 0f; remaining -= frame)
            {
                MotionSpringDriver.Step(reference, referenceState, Mathf.Min(remaining, frame));
            }
            MotionSpringDriver.Step(reference, referenceState, (float)ThirtiethSec);
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Act
            for (var i = 0; i < 3; i++) Step(clock, ThirtiethSec);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(reference.style.opacity.value).Within(1e-4f));
        }

        // --- what the driven Tween does not write ---

        [Test]
        public void Given_AnElementWhoseClassesTransitionColours_When_ATweenOfItsOpacityPlaysOnAHeldClock_Then_ItsNativeTransitionsAreSuspended()
        {
            // Arrange
            var element = OnPanel("coloured");
            element.AddToClassList("transition-colors");
            _scheduler = new StyleAnimationScheduler { Clock = new HeldMotionClock() };

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0", "bg-primary" }, new[] { "opacity-100", "bg-secondary" },
                s_linearTween);

            // Assert — an inline transition-property, the suspension's, which lands the colour the swap changes; with
            // none the class's list runs it.
            Assert.That(element.style.transitionProperty.keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_ATweenExitWithALongerOverrideOnAPropertyNoChannelPlays_When_TheClockRunsPastTheTopLevelDuration_Then_ItCompletesOnlyOnceTheOverrideHasRun()
        {
            // Arrange — the exit fades opacity; the override times a background colour the swap does not change.
            var clock = new HeldMotionClock();
            var element = OnPanel("leaving");
            _scheduler = new StyleAnimationScheduler { Clock = clock };
            var completions = 0;
            var config = new StyleTransitionConfig
            {
                DurationSec = 0.3f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
                PropertyOverrides = new[] { new StylePropertyTransition("background-color", durationSec: 1f) },
            };
            _scheduler.PlayExit(element, config, onComplete: () => completions++, restoreFromOnCancel: true);

            // Act
            Step(clock, 0.5);
            var pastTopLevel = completions;
            Step(clock, 0.5);

            // Assert
            Assert.That(new[] { pastTopLevel, completions }, Is.EqualTo(new[] { 0, 1 }));
        }
    }
}
