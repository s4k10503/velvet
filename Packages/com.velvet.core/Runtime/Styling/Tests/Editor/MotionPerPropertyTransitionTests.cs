using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="StyleTransitionConfig.PropertyOverrides"/>: a variant-swap transition (the only place the
    /// scheduler sets transition-property: all — PlayVariantEnter / a variant-driven PlayExit) with per-property
    /// overrides switches transition-property from that implicit "all" catch-all to EXACTLY the overridden
    /// properties, in declaration order, with duration / easing / delay built as positionally-matched lists — a
    /// null override field falls back to the top-level DurationSec / Easing / DelaySec. The resolving cases use a
    /// real EditorWindow panel so resolvedStyle reflects the scheduler's inline styles; the cancel case is
    /// deliberately off-panel (mirrors the existing off-panel-cancel contract, so no panel is needed there).
    /// </summary>
    [TestFixture]
    internal sealed class MotionPerPropertyTransitionTests
    {
        private EditorWindow _window;

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.Close();
                Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        private VisualElement MountOnRealPanel()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            _window.Show();
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            return element;
        }

        [Test]
        public void Given_AVariantEnterWithTwoPropertyOverrides_When_Resolved_Then_TransitionPropertyIsExactlyThoseTwoWithMatchingDurations()
        {
            // Arrange — a variant enter (the allProperties path) whose transition carries two per-property
            // overrides with different durations.
            var element = MountOnRealPanel();
            var scheduler = new StyleAnimationScheduler();
            var overrides = new[]
            {
                new StylePropertyTransition("opacity", durationSec: 0.15f),
                new StylePropertyTransition("scale", durationSec: 0.5f),
            };
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            scheduler.PlayVariantEnter(element, System.Array.Empty<string>(), System.Array.Empty<string>(),
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f, propertyOverrides: overrides);
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);

            // Assert — transition-property is exactly [opacity, scale] (not the implicit "all"), each POSITIONALLY
            // paired with its OWN override duration instead of the shared top-level 0.3f. The scheduler authors
            // TimeValue entries in milliseconds (TimeUnit.Millisecond), and resolvedStyle reports them back in
            // that same unit (unlike a USS duration-* utility, which is parsed straight to seconds). Zipped into
            // an array of (property, durationMs) pairs so the array-of-scalar-tuples compares deep-equal instead
            // of a tuple-of-arrays (which NUnit does not expand element-wise).
            var props = element.resolvedStyle.transitionProperty.Select(p => p.ToString());
            var durationsMs = element.resolvedStyle.transitionDuration.Select(t => t.value);
            var resolved = props.Zip(durationsMs, (property, durationMs) => (property, durationMs)).ToArray();
            Assert.That(resolved, Is.EqualTo(new[] { ("opacity", 150f), ("scale", 500f) }).Within(1e-3f));
        }

        [Test]
        public void Given_APropertyOverrideWithNoDurationField_When_Resolved_Then_ItFallsBackToTheTopLevelDurationSec()
        {
            // Arrange — one override sets its own duration; the other omits it (null DurationSec).
            var element = MountOnRealPanel();
            var scheduler = new StyleAnimationScheduler();
            var overrides = new[]
            {
                new StylePropertyTransition("opacity", durationSec: 0.15f),
                new StylePropertyTransition("scale"),
            };
            Assume.That(element.panel, Is.Not.Null, "Precondition: the element is on a real panel");

            // Act
            scheduler.PlayVariantEnter(element, System.Array.Empty<string>(), System.Array.Empty<string>(),
                durationSec: 0.4f, easing: EasingMode.EaseOut, delaySec: 0f, propertyOverrides: overrides);
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);

            // Assert — the un-overridden "scale" duration falls back to the top-level DurationSec (0.4f = 400ms).
            var durationsMs = element.resolvedStyle.transitionDuration.Select(t => t.value).ToArray();
            Assert.That(durationsMs, Is.EqualTo(new[] { 150f, 400f }).Within(1e-3f));
        }

        [Test]
        public void Given_AnOffPanelVariantExitWithPropertyOverrides_When_Cancelled_Then_TheInlineTransitionStylesClearImmediately()
        {
            // Arrange — an off-panel variant exit (restoreFromOnCancel) whose config carries property
            // overrides. Off-panel mirrors the existing contract: nothing to interpolate, so a cancel clears
            // synchronously instead of deferring (which would also plant a stale deferred-attach callback).
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Assume.That(element.panel, Is.Null, "Precondition: the element is off-panel");
            var config = new StyleTransitionConfig
            {
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
                DurationSec = 0.2f,
                Easing = EasingMode.EaseOut,
                PropertyOverrides = new[]
                {
                    new StylePropertyTransition("opacity", durationSec: 0.1f),
                    new StylePropertyTransition("scale", durationSec: 0.3f),
                },
            };
            scheduler.PlayExit(element, config, onComplete: null, restoreFromOnCancel: true);
            Assume.That(element.style.transitionDuration.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: PlayExit applied the per-property inline transition styles");

            // Act — cancel before the element ever attaches.
            scheduler.CancelExit(element);

            // Assert — cleared immediately (no panel to interpolate against): the n-entry lists rented for the
            // overrides are returned rather than left applied.
            Assert.That(element.style.transitionDuration.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }

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
