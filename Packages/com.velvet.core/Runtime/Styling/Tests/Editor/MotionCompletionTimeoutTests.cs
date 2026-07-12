using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the enter/exit completion timeout against <see cref="StyleTransitionConfig.PropertyOverrides"/>: a
    /// variant swap's completion (the timer that clears the inline transition styles for an enter, or drops the
    /// ghost for an exit) must wait for the SLOWEST overridden property, not just the top-level DurationSec —
    /// sharing the same slowest-property computation an interrupted reversal already uses
    /// (StyleAnimationScheduler.SlowestPropertyTimeoutMs). Needs a real (simulated, time-driven) panel: the
    /// timeout is a scheduled item, so observing whether it has fired yet requires actually advancing the
    /// panel's clock, which <c>MotionPerPropertyTransitionTests</c>' ForcePanelUpdate-based harness (a
    /// synchronous style-resolution pass, not a clock) cannot do.
    /// </summary>
    [TestFixture]
    internal sealed class MotionCompletionTimeoutTests
    {
        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void AdvancePastMs(long ms)
        {
            var steps = (int)(ms / 16) + 1;
            for (var i = 0; i < steps; i++) _sim.FrameUpdateMs(16);
        }

        private static StylePropertyTransition[] SlowScaleOverrides() => new[]
        {
            new StylePropertyTransition("opacity", durationSec: 0.15f),
            new StylePropertyTransition("scale", durationSec: 0.5f),
        };

        [Test]
        public void Given_AVariantEnterWithASlowerPropertyOverride_When_OnlyTheTopLevelDurationHasElapsed_Then_OnCompleteHasNotFiredYet()
        {
            // Arrange — the top-level DurationSec (0.3s) is FASTER than the "scale" override (0.5s); sizing the
            // completion off the top-level value alone would fire while scale is still mid-tween.
            var element = new VisualElement();
            Root.Add(element);
            var scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, System.Array.Empty<string>(), System.Array.Empty<string>(),
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                onComplete: () => completed = true, propertyOverrides: SlowScaleOverrides());
            AdvancePastMs(400);

            // Assert — 400ms has cleared the top-level 0.3s (plus grace) but not the slowest override (0.5s).
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Given_AVariantEnterWithASlowerPropertyOverride_When_TheSlowestOverrideDurationPlusGraceHasElapsed_Then_OnCompleteFires()
        {
            // Arrange — same config as above; this time advance well past the slowest override.
            var element = new VisualElement();
            Root.Add(element);
            var scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, System.Array.Empty<string>(), System.Array.Empty<string>(),
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                onComplete: () => completed = true, propertyOverrides: SlowScaleOverrides());
            AdvancePastMs(700);

            // Assert — the completion still fires once the slowest override has genuinely elapsed.
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_AVariantExitWithASlowerPropertyOverride_When_OnlyTheTopLevelDurationHasElapsed_Then_OnCompleteHasNotFiredYet()
        {
            // Arrange — a variant exit (restoreFromOnCancel) whose PropertyOverrides carries the same
            // faster-top-level / slower-scale-override shape.
            var element = new VisualElement();
            Root.Add(element);
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var config = new StyleTransitionConfig
            {
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
                DurationSec = 0.3f,
                PropertyOverrides = SlowScaleOverrides(),
            };

            // Act
            scheduler.PlayExit(element, config, onComplete: () => completed = true, restoreFromOnCancel: true);
            AdvancePastMs(400);

            // Assert — completing here would drop the ghost while the slower "scale" override is still animating.
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Given_AVariantExitWithASlowerPropertyOverride_When_TheSlowestOverrideDurationPlusGraceHasElapsed_Then_OnCompleteFires()
        {
            // Arrange
            var element = new VisualElement();
            Root.Add(element);
            var scheduler = new StyleAnimationScheduler();
            var completed = false;
            var config = new StyleTransitionConfig
            {
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
                DurationSec = 0.3f,
                PropertyOverrides = SlowScaleOverrides(),
            };

            // Act
            scheduler.PlayExit(element, config, onComplete: () => completed = true, restoreFromOnCancel: true);
            AdvancePastMs(700);

            // Assert
            Assert.That(completed, Is.True);
        }
    }
}
