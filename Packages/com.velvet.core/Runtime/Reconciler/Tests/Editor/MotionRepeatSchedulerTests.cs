using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what <see cref="StyleAnimationScheduler"/> does with a repeating transition: a bezier or spring play that
    /// ends on its from-values holds them past its completion until a cancel, one that ends on its to-values hands
    /// them back to the classes, and a tween play warns that it does not repeat. Also pins the wait
    /// <see cref="TransitionWhen.BeforeChildren"/> derives from a repeating swap, and a play whose delay never runs
    /// out, which is what a child waiting on an endless one is handed.
    /// </summary>
    [TestFixture]
    internal sealed class MotionRepeatSchedulerTests : MotionSimulatedPanelTestsBase
    {
        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            _scheduler = null;
            base.TearDown();
        }

        private VisualElement OnPanel()
        {
            var element = new VisualElement();
            element.AddToClassList("opacity-100");
            Root.Add(element);
            return element;
        }

        private static StyleTransitionConfig Bezier(float repeat, TransitionRepeatType type) => new()
        {
            Type = TransitionType.Bezier, DurationSec = 0.2f, Repeat = repeat, RepeatType = type,
        };

        private static int RepeatWarnings(System.Action play)
        {
            var count = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains(StyleAnimationScheduler.RepeatNotPlayedWarning))
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
        public void Given_AnOddReverseRepeatEnter_When_ItCompletes_Then_TheElementKeepsItsFromOpacityInline()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                Bezier(1f, TransitionRepeatType.Reverse), onComplete: () => completed = true);

            // Act
            AdvancePast(0.4f);

            // Assert — completed, yet still showing the from-pose rather than the opacity-100 class.
            var opacity = element.style.opacity;
            Assert.That((completed, opacity.keyword, opacity.value), Is.EqualTo((true, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_AHeldFromPose_When_TheEnterIsCancelled_Then_TheInlineOpacityIsReleased()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                Bezier(1f, TransitionRepeatType.Mirror));
            AdvancePast(0.4f);
            var heldKeyword = element.style.opacity.keyword;

            // Act
            _scheduler.CancelEnter(element);

            // Assert
            Assert.That((heldKeyword, element.style.opacity.keyword),
                Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AnEvenReverseRepeatEnter_When_ItCompletes_Then_ItHandsTheOpacityBackToTheClasses()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                Bezier(2f, TransitionRepeatType.Reverse), onComplete: () => completed = true);

            // Act
            AdvancePast(0.6f);

            // Assert
            Assert.That((completed, element.style.opacity.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ARepeatingTween_When_TwoEntersPlay_Then_OneWarningSaysItPlaysOnce()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var first = OnPanel();
            var second = OnPanel();
            var tween = new StyleTransitionConfig { DurationSec = 0.2f, Repeat = 1f };

            // Act
            var warnings = RepeatWarnings(() =>
            {
                _scheduler.PlayVariantEnter(first, new[] { "opacity-0" }, new[] { "opacity-100" }, tween);
                _scheduler.PlayVariantEnter(second, new[] { "opacity-0" }, new[] { "opacity-100" }, tween);
            });

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARepeatingTween_When_AnExitPlays_Then_AWarningSaysItPlaysOnce()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var leaving = OnPanel();
            var tween = new StyleTransitionConfig { DurationSec = 0.2f, Repeat = 1f };

            // Act
            var warnings = RepeatWarnings(() => _scheduler.PlayExit(leaving,
                tween.WithExitClasses("opacity-100", "opacity-0"), onComplete: null, restoreFromOnCancel: true));

            // Assert
            Assert.That(warnings, Is.EqualTo(1));
        }

        [Test]
        public void Given_ATweenWithNoRepeat_When_AnEnterPlays_Then_NoRepeatWarningIsLogged()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var tween = new StyleTransitionConfig { DurationSec = 0.2f };

            // Act
            var warnings = RepeatWarnings(() =>
                _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, tween));

            // Assert
            Assert.That(warnings, Is.EqualTo(0));
        }

        [Test]
        public void Given_AnOddMirrorRepeatSpringEnter_When_ItCompletes_Then_TheElementKeepsItsFromOpacityInline()
        {
            // Arrange — the opacity spring rests at 1.1 s, so two passes take 2.2 s.
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            var spring = new StyleTransitionConfig
            {
                Type = TransitionType.Spring, Repeat = 1f, RepeatType = TransitionRepeatType.Mirror,
            };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, spring,
                onComplete: () => completed = true);

            // Act
            AdvancePast(2.2f);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((completed, opacity.keyword, opacity.value), Is.EqualTo((true, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ARepeatingSpring_When_AnExitPlays_Then_NoRepeatWarningIsLogged()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring, Repeat = 1f }
                .WithExitClasses("opacity-100", "opacity-0");

            // Act
            var warnings = RepeatWarnings(() =>
                _scheduler.PlayExit(element, spring, onComplete: null, restoreFromOnCancel: true));

            // Assert
            Assert.That(warnings, Is.EqualTo(0));
        }

        [Test]
        public void Given_ARepeatingBezier_When_AnEnterAndAnExitPlay_Then_NoRepeatWarningIsLogged()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var entering = OnPanel();
            var leaving = OnPanel();
            var bezier = Bezier(1f, TransitionRepeatType.Loop);

            // Act
            var warnings = RepeatWarnings(() =>
            {
                _scheduler.PlayVariantEnter(entering, new[] { "opacity-0" }, new[] { "opacity-100" }, bezier);
                _scheduler.PlayExit(leaving, bezier.WithExitClasses("opacity-100", "opacity-0"), onComplete: null,
                    restoreFromOnCancel: true);
            });

            // Assert
            Assert.That(warnings, Is.EqualTo(0));
        }

        [Test]
        public void Given_BeforeChildrenOnARepeatingBezierSwap_When_AChildClaimsItsSlot_Then_ItWaitsOutEveryPass()
        {
            // Arrange — a 0.1 s delay and two half-second passes with a quarter-second wait between them.
            var swap = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, DelaySec = 0.1f, Repeat = 1f, RepeatDelaySec = 0.25f,
                When = TransitionWhen.BeforeChildren,
            };

            // Act
            var frame = FiberNodePatcher.ResolveChildOrchestration(V.Motion(), swap, childLabelChanged: true,
                ambientOrchestration: null, extraDelaySec: 0f);
            var delay = frame?.ClaimNextChildDelaySec() ?? float.NaN;

            // Assert
            Assert.That(delay, Is.EqualTo(1.35f).Within(1e-5f));
        }

        [Test]
        public void Given_BeforeChildrenOnAnEndlesslyRepeatingSwap_When_AChildClaimsItsSlot_Then_ItsWaitNeverRunsOut()
        {
            // Arrange
            var swap = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = float.PositiveInfinity,
                When = TransitionWhen.BeforeChildren,
            };

            // Act
            var frame = FiberNodePatcher.ResolveChildOrchestration(V.Motion(), swap, childLabelChanged: true,
                ambientOrchestration: null, extraDelaySec: 0f);
            var delay = frame?.ClaimNextChildDelaySec() ?? float.NaN;

            // Assert
            Assert.That(delay, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Given_BeforeChildrenOnASpringSwap_When_AChildClaimsItsSlot_Then_ItWaitsUntilTheSpringRests()
        {
            // Arrange — the opacity's travel-100 easing rests at 1.05 s on Framer's default spring; DurationSec is
            // not read.
            var swap = new StyleTransitionConfig
            {
                Type = TransitionType.Spring, DurationSec = 0.3f, DelaySec = 0.1f, When = TransitionWhen.BeforeChildren,
            };

            // Act
            var frame = FiberNodePatcher.ResolveChildOrchestration(V.Motion(), swap, childLabelChanged: true,
                ambientOrchestration: null, extraDelaySec: 0f, new[] { "opacity-0" }, new[] { "opacity-100" });
            var delay = frame?.ClaimNextChildDelaySec() ?? float.NaN;

            // Assert
            Assert.That(delay, Is.EqualTo(1.15f).Within(1e-5f));
        }

        [Test]
        public void Given_ABezierEnterWhoseDelayNeverRunsOut_When_ThePanelTicks_Then_ItHoldsItsFromPose()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            var bezier = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.2f };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, bezier,
                onComplete: () => completed = true, additionalDelaySec: float.PositiveInfinity);

            // Act
            AdvancePast(0.5f);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((completed, opacity.keyword, opacity.value), Is.EqualTo((false, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ASpringEnterWhoseDelayNeverRunsOut_When_ThePanelTicks_Then_ItHoldsItsFromPose()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, spring,
                onComplete: () => completed = true, additionalDelaySec: float.PositiveInfinity);

            // Act
            AdvancePast(1.5f);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((completed, opacity.keyword, opacity.value), Is.EqualTo((false, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ATweenEnterWhoseDelayNeverRunsOut_When_ThePanelTicks_Then_ItKeepsItsFromClasses()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var tween = new StyleTransitionConfig { DurationSec = 0.2f };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, tween,
                additionalDelaySec: float.PositiveInfinity);

            // Act
            AdvancePast(0.5f);

            // Assert
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ATweenExitWhoseDelayNeverRunsOut_When_ThePanelTicks_Then_ItNeverCompletes()
        {
            // Arrange
            _scheduler = new StyleAnimationScheduler();
            var element = OnPanel();
            var completed = false;
            var tween = new StyleTransitionConfig { DurationSec = 0.2f }.WithExitClasses("opacity-100", "opacity-0");
            _scheduler.PlayExit(element, tween, () => completed = true, restoreFromOnCancel: true,
                additionalDelaySec: float.PositiveInfinity);

            // Act
            AdvancePast(0.5f);

            // Assert — still at its resting class, the exit's swap never run.
            Assert.That((completed, element.ClassListContains("opacity-0")), Is.EqualTo((false, false)));
        }
    }
}
