using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class MotionNegativeDelayTests : PanelTestBase
    {
        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            base.TearDown();
        }

        private VisualElement MountOnRealPanel()
        {
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            return element;
        }

        [Test]
        public void Given_ATweenVariantEnterWithANegativeDelay_When_Started_Then_TheInlineTransitionDelayCarriesIt()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 0.3f, easing: EasingMode.Linear, delaySec: -0.1f);

            // Assert
            var delays = element.style.transitionDelay.value;
            Assert.That(delays is { Count: > 0 } ? delays[0].value : float.NaN, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_APropertyOverrideWithTheOnlyNegativeDelay_When_Started_Then_TheInlineTransitionDelayCarriesIt()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var overrides = new[] { new StylePropertyTransition("opacity", delaySec: -0.1f) };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 0.3f, easing: EasingMode.Linear, delaySec: 0f, propertyOverrides: overrides);

            // Assert
            var delays = element.style.transitionDelay.value;
            Assert.That(delays is { Count: 2 } ? delays[1].value : float.NaN, Is.EqualTo(-100f));
        }

        [Test]
        public void Given_ABezierVariantEnterWithANegativeDelay_When_Started_Then_ItShowsThatFarIntoItsRun()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
                DurationSec = 1f, DelaySec = -0.5f,
            };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ABezierVariantEnterWithADelayPastItsDuration_When_Started_Then_ItCompletesAtOnce()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.3f, DelaySec = -1f };
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                onComplete: () => completed = true);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ASpringVariantEnterWithANegativeDelay_When_Started_Then_ItHasAlreadyMoved()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = -0.2f };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Assert
            Assert.That(element.style.opacity.value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_ASpringVariantEnterWithADelayPastItsSettle_When_Started_Then_ItCompletesAtOnce()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = -30f };
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                onComplete: () => completed = true);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ABezierEnterWithASubMillisecondNegativeDelay_When_Started_Then_ItPreservesTheOffset()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
                DurationSec = 1f, DelaySec = -0.0005f,
            };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.0005f).Within(1e-6f));
        }

        [Test]
        public void Given_ABezierEnterWithANegativeDelayAndAStaggerOffset_When_Started_Then_TheOffsetsAreCombined()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
                DurationSec = 1f, DelaySec = -0.5f,
            };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                additionalDelaySec: 0.2f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.3f).Within(1e-4f));
        }

        private VisualElement NativeTweenElement()
        {
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            var element = MountOnRealPanel();
            element.AddToClassList("opacity-0");
            ForcePanelUpdate(element.panel);
            return element;
        }

        private double _now;

        private void StartNativeClock(VisualElement element)
        {
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
        }

        private void NativeFrame(VisualElement element, double seconds)
        {
            _now += seconds;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        [Test]
        public void Given_ATweenEnterWithANegativeCombinedDelay_When_TheFirstFrameRuns_Then_ItHasAlreadyProgressed()
        {
            // Arrange
            var element = NativeTweenElement();
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 1f, easing: EasingMode.Linear, delaySec: -0.5f, additionalDelaySec: 0.2f);
            ForcePanelUpdate(element.panel);

            // Act
            NativeFrame(element, 0.016);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.3f).Within(0.003f));
        }

        [Test]
        public void Given_ATweenExitWithANegativeCombinedDelay_When_TheFirstFrameRuns_Then_ItHasAlreadyProgressed()
        {
            // Arrange
            var element = NativeTweenElement();
            element.RemoveFromClassList("opacity-0");
            element.AddToClassList("opacity-100");
            ForcePanelUpdate(element.panel);
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, DelaySec = -0.5f,
                ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            };
            scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true, additionalDelaySec: 0.2f);
            ForcePanelUpdate(element.panel);

            // Act
            NativeFrame(element, 0.016);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.7f).Within(0.003f));
        }

        [Test]
        public void Given_APropertyOverrideWithANegativeCombinedDelay_When_TheFirstFrameRuns_Then_ItHasAlreadyProgressed()
        {
            // Arrange
            var element = NativeTweenElement();
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f, additionalDelaySec: 0.2f,
                propertyOverrides: new[] { new StylePropertyTransition("opacity", delaySec: -0.5f) });
            ForcePanelUpdate(element.panel);

            // Act
            NativeFrame(element, 0.016);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(0.3f).Within(0.003f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_ATweenEnterWithAPositiveCombinedDelay_When_TheDelayElapses_Then_ItAnimatesFromTheStart(
            bool propertyOverride)
        {
            // Arrange
            var element = NativeTweenElement();
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            element.style.transitionDelay = new List<TimeValue> { new(0.5f) };
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 1f, easing: EasingMode.Linear, delaySec: -0.1f, additionalDelaySec: 0.2f,
                propertyOverrides: propertyOverride ? new[] { new StylePropertyTransition("opacity") } : null);
            ForcePanelUpdate(element.panel);

            // Act
            NativeFrame(element, 0.016);
            var waitingOpacity = element.resolvedStyle.opacity;
            NativeFrame(element, 0.085);
            NativeFrame(element, 0.016);

            // Assert
            Assert.That(new[] { waitingOpacity, element.resolvedStyle.opacity },
                Is.EqualTo(new[] { 0f, 0.016f }).Within(0.003f));
        }

        [Test]
        public void Given_ASpringEnterWithANegativeDelayAndAStaggerOffset_When_Started_Then_TheOffsetsAreCombined()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = -0.5f };
            var expectedElement = new VisualElement();
            var expected = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(
                new[] { "opacity-0" }, new[] { "opacity-100" }), config.Stiffness, config.Damping, config.Mass);
            for (var remaining = 0.3f; remaining > 0f; remaining -= 0.016f)
                MotionSpringDriver.Step(expectedElement, expected, Math.Min(remaining, 0.016f));

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                additionalDelaySec: 0.2f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(expectedElement.style.opacity.value).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): a zero combined delay starts the recurring tick without a delayed start.
        [Test]
        public void Given_ABezierEnterWithAZeroCombinedDelay_When_TheFirstFrameRuns_Then_ItAlreadyTicks()
        {
            // Arrange
            var element = MountOnRealPanel();
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = -0.2f,
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config,
                additionalDelaySec: 0.2f);

            // Act
            NativeFrame(element, 0.016);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.016f).Within(1e-4f));
        }

        // GREEN_ON_BASE(characterization): without pre-roll, even an already-resting spring completes on its tick.
        [Test]
        public void Given_ASpringEnterAlreadyAtRestWithNoPreRoll_When_Started_Then_CompletionWaitsForItsTick()
        {
            // Arrange
            var element = MountOnRealPanel();
            StartNativeClock(element);
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var completed = false;
            var config = new StyleTransitionConfig { Type = TransitionType.Spring };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-100" }, config,
                onComplete: () => completed = true);
            var completedSynchronously = completed;
            NativeFrame(element, 0.016);

            // Assert
            Assert.That((completedSynchronously, completed), Is.EqualTo((false, true)));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AControllerEnterWhoseNegativeDelayFinishesIt_When_Started_Then_ItsPendingEntryIsReleased(
            TransitionType type)
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = type, DurationSec = 0.3f, DelaySec = -30f };
            var entries = (IDictionary)typeof(StyleAnimationScheduler).GetField("_pendingEnters",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scheduler);

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Assert
            Assert.That(entries.Contains(element), Is.False);
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_ARingedControllerEnterWhoseNegativeDelayFinishesIt_When_Started_Then_TheBandIsReleased(
            TransitionType type)
        {
            // Arrange
            var element = MountOnRealPanel();
            var binding = RingOverlay.Attach(element, new RingSpec(2f, Color.red, 0f, false), Array.Empty<string>());
            binding.Overlay.style.opacity = 0.77f;
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var config = new StyleTransitionConfig { Type = type, DurationSec = 0.3f, DelaySec = -30f };

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, config);

            // Assert
            Assert.That(binding.Overlay.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_APropertyOverrideWhoseEntriesAllHaveNegativeDelays_When_Started_Then_TheDelayListIsWritten()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 1f, easing: EasingMode.Linear, delaySec: -0.1f, additionalDelaySec: 0f,
                propertyOverrides: new[] { new StylePropertyTransition("opacity") });

            // Assert
            var delays = element.style.transitionDelay.value;
            Assert.That(delays is { Count: 2 } ? delays[1].value : float.NaN, Is.EqualTo(-100f));
        }

        // GREEN_ON_BASE(characterization): scheduling a fresh bezier tick does not re-sample an unrelated loop.
        [Test]
        public void Given_AFresherSpinPhaseBesideADelayedBezierEnter_When_TheTickStartsWithoutPreRoll_Then_TheCachedLoopFrameStays()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var loop = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Spin, 10f), panVertical: false);
            try
            {
                scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                    new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = 1f });
                var cachedRotation = element.style.rotate;
                loop.StartTime -= 2.0;
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Rotate);
                var phaseMoved = Mathf.Abs(Mathf.DeltaAngle(cachedRotation.value.angle.value,
                    element.style.rotate.value.angle.value)) > StyleAnimateDriver.SpinAngleDeg(0.1f);
                element.style.rotate = cachedRotation;
                var entries = (IDictionary)typeof(StyleAnimationScheduler).GetField("_pendingEnters",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scheduler);
                var start = typeof(StyleAnimationScheduler).GetMethod("StartBezierTick",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                // Act
                start.Invoke(scheduler, start.GetParameters().Length == 3
                    ? new object[] { element, entries[element], 0f }
                    : new object[] { element, entries[element] });

                // Assert
                Assert.That((phaseMoved, element.style.rotate.Equals(cachedRotation)), Is.EqualTo((true, true)));
            }
            finally
            {
                StyleAnimateDriver.Detach(element, loop);
            }
        }
    }
}
