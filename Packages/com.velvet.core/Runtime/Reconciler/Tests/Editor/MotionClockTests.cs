using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="MotionClock"/>: the motion Velvet drives frame by frame advances by the clock a mount
    /// chooses, so a clock that holds still holds it while the panel keeps ticking, and a clock stepped by one
    /// frame moves it by one frame.
    /// </summary>
    [TestFixture]
    internal sealed class MotionClockTests : MotionSimulatedPanelTestsBase
    {
        // A power of two, so the clock's double difference and the driver's float step agree exactly.
        private const double FrameSec = 1.0 / 64.0;

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

        private sealed class HeldClock : MotionClock
        {
            public double Now;

            // An integral start keeps every sum of FrameSec steps exact.
            public HeldClock(double start = 100.0) => Now = start;

            public override double NowSec => Now;
        }

        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            _scheduler = null;
            base.TearDown();
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

        private void Step(HeldClock clock, double seconds)
        {
            clock.Now += seconds;
            Tick();
        }

        [Test]
        public void Given_ABezierPlayOnAHeldClock_When_ThePanelTicksAndThenTheClockStepsAFrame_Then_ItMovesOnlyThatFrame()
        {
            // Arrange
            var clock = new HeldClock();
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
            var clock = new HeldClock();
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
            var clock = new HeldClock();
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

        private static readonly StyleTransitionConfig s_tween = new()
        {
            DurationSec = 0.3f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
        };

        private static int TweenClockWarnings(System.Action play)
        {
            var count = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("does not follow this mount's MountOptions.MotionClock"))
                {
                    count++;
                }
            }
            Application.logMessageReceived += OnLog;
            try
            {
                play();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }
            return count;
        }

        [Test]
        public void Given_AHeldClock_When_TwoTweenEntersPlay_Then_OneWarningSaysTheyDoNotFollowIt()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler { Clock = new HeldClock() };
            var first = OnPanel("first");
            var second = OnPanel("second");

            // Act
            var warnings = TweenClockWarnings(() =>
            {
                _scheduler.PlayVariantEnter(first, new[] { "opacity-0" }, new[] { "opacity-100" }, s_tween);
                _scheduler.PlayVariantEnter(second, new[] { "opacity-0" }, new[] { "opacity-100" }, s_tween);
            });

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHeldClock_When_ATweenExitPlays_Then_AWarningSaysItDoesNotFollowIt()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler { Clock = new HeldClock() };
            var element = OnPanel("leaving");

            // Act
            var warnings = TweenClockWarnings(() =>
                _scheduler.PlayExit(element, s_tween, onComplete: null, restoreFromOnCancel: true));

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        [Test]
        public void Given_TheDefaultClock_When_TweenPlaysRun_Then_NoClockWarningIsLogged()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var entering = OnPanel("entering");
            var leaving = OnPanel("leaving");

            // Act
            var warnings = TweenClockWarnings(() =>
            {
                _scheduler.PlayVariantEnter(entering, new[] { "opacity-0" }, new[] { "opacity-100" }, s_tween);
                _scheduler.PlayExit(leaving, s_tween, onComplete: null, restoreFromOnCancel: true);
            });

            // Assert
            Assert.That(warnings, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnAnimateLoopOnAHeldClock_When_TheClockMovesAQuarterLoop_Then_TheLoopShowsAQuarterTurn()
        {
            // Arrange — a start whose double is not integral, so a phase read from the sum of the two readings
            // lands elsewhere than one read from their difference.
            var clock = new HeldClock(100.3);
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

        [Component]
        private static VNode FadeIn() => V.Motion(name: "fade", variants: s_fade, initial: "hidden", animate: "visible",
            transition: s_linear);

        [Test]
        public void Given_AMountNamingAHeldClock_When_ItsMountEnterRunsWhileThePanelTicks_Then_TheEnterHoldsUntilTheClockSteps()
        {
            // Arrange
            var clock = new HeldClock();
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
            var clock = new HeldClock();
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
            var clock = new HeldClock();
            using var mounted = V.Mount(Root, V.Div(name: "spinner", className: "animate-pulse"),
                new MountOptions { MotionClock = clock });
            var element = Root.Q<VisualElement>("spinner");
            mounted.Render(V.Div(name: "spinner", className: "animate-spin"));

            // Act
            Step(clock, 0.25);

            // Assert
            Assert.That(element.style.rotate.value.angle.value, Is.EqualTo(90f).Within(1e-3f));
        }
    }
}
