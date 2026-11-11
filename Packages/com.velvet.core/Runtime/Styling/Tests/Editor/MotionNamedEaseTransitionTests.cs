using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a Motion tween UI Toolkit's own transition plays runs a named ease on Framer Motion's curve for it
    /// from its first frame, each property on the keyword its own entry names and opacity as Framer's Web Animations path
    /// eases it, that a transition the tween did not start keeps its curve, and that the element stops being listened to
    /// once no tween plays on it.
    /// </summary>
    [TestFixture]
    internal sealed class MotionNamedEaseTransitionTests : PanelTestBase
    {
        private StyleAnimationScheduler _scheduler;
        private double _now;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            base.TearDown();
        }

        // An element carrying from, on the panel's own clock at 100 s, after arrange.
        private VisualElement OnClock(string from, System.Action<VisualElement> arrange = null)
        {
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            element.AddToClassList(from);
            arrange?.Invoke(element);
            ForcePanelUpdate(element.panel);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            return element;
        }

        // A tween enter from from to to on config, which plays as UI Toolkit's transition on the panel's clock.
        private void TweenEnter(VisualElement element, string from, string to, StyleTransitionConfig config)
        {
            _scheduler = new StyleAnimationScheduler();
            _scheduler.PlayVariantEnter(element, new[] { from }, new[] { to }, config);
            ForcePanelUpdate(element.panel);
        }

        // The frame that swaps the classes and the one the transitions it starts first run in.
        private void FirstFrame(VisualElement element)
        {
            _now += 0.016;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
        }

        private float FirstFrameOpacity(StyleTransitionConfig config)
        {
            var element = OnClock("opacity-0");
            TweenEnter(element, "opacity-0", "opacity-100", config);
            FirstFrame(element);
            return element.resolvedStyle.opacity;
        }

        private static StyleTransitionConfig OneSecond(EasingMode easing, float delaySec)
            => new() { DurationSec = 1f, Easing = easing, DelaySec = delaySec };

        // Framer Motion's backIn: back.ts's, backOut reversed, and the Web Animations one supportedWaapiEasing names.
        private static float FramerBackIn(float t) => 1f - CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, 1f - t);

        private static float WebAnimationsBackIn(float t) => CubicBezierEvaluator.Evaluate(0.31f, 0.01f, 0.66f, -0.59f, t);

        // Whether StyleEaseCurve listens for the element's transitions; null where it holds nothing for the element.
        private static bool? Listens(VisualElement element)
        {
            var table = typeof(StyleEaseCurve).GetField("s_bindings", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var arguments = new object[] { element, null };
            if (!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments))
            {
                return null;
            }
            return (bool)arguments[1].GetType().GetField("Registered")!.GetValue(arguments[1]);
        }

        [Test]
        public void Given_ATweenEnterEasingOutAQuarterSecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersEaseOutPutsIt()
        {
            // Arrange — Framer Motion's easeOut a quarter of the way along.
            var expected = CubicBezierEvaluator.Evaluate(0f, 0f, 0.58f, 1f, 0.25f);

            // Act
            var opacity = FirstFrameOpacity(OneSecond(EasingMode.EaseOut, -0.25f));

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenEnterEasingOutBackThreeTenthsOfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersBackOutPutsIt()
        {
            // Arrange — Framer Motion's backOut three tenths of the way along, short of its overshoot.
            var expected = CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, 0.3f);

            // Act
            var opacity = FirstFrameOpacity(OneSecond(EasingMode.EaseOutBack, -0.3f));

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenEnterEasingInBackThreeQuartersOfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersWebAnimationsBackInPutsIt()
        {
            // Arrange
            var expected = WebAnimationsBackIn(0.75f);

            // Act
            var opacity = FirstFrameOpacity(OneSecond(EasingMode.EaseInBack, -0.75f));

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenEnterEasingInBackWithARepeatDelay_When_TheFirstFrameRuns_Then_OpacityIsWhereBackTsBackInPutsIt()
        {
            // Arrange — a repeat delay keeps Framer's opacity off the Web Animations API, whatever the count.
            var config = new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInBack, DelaySec = -0.75f, RepeatDelaySec = 0.5f };
            var expected = FramerBackIn(0.75f);

            // Act
            var opacity = FirstFrameOpacity(config);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ATweenExitEasingInBackWithARepeatDelay_When_TheFirstFrameRuns_Then_OpacityIsWhereBackTsBackInPutsIt()
        {
            // Arrange — an exit from opacity-100 to opacity-0; a repeat delay keeps Framer's opacity off the Web Animations
            // API, whatever the count.
            var element = OnClock("opacity-100");
            _scheduler = new StyleAnimationScheduler();
            _scheduler.PlayExit(element, new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear, ExitEasing = EasingMode.EaseInBack, DelaySec = -0.75f,
                RepeatDelaySec = 0.5f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            }, onComplete: null, restoreFromOnCancel: true);
            ForcePanelUpdate(element.panel);
            var expected = 1f - FramerBackIn(0.75f);

            // Act
            FirstFrame(element);

            // Assert
            Assert.That(element.resolvedStyle.opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ASlideUpTweenEnterEasingInBack_When_TheFirstFrameRuns_Then_ItsTranslateIsWhereBackTsBackInPutsIt()
        {
            // Arrange — anim-slide-up-enter-from translates 20px down; Framer runs a translate on back.ts's backIn.
            var element = OnClock("anim-slide-up-enter-from");
            TweenEnter(element, "anim-slide-up-enter-from", "anim-slide-up-enter-to", OneSecond(EasingMode.EaseInBack, -0.75f));
            var expected = 20f - 20f * FramerBackIn(0.75f);

            // Act
            FirstFrame(element);

            // Assert
            Assert.That(element.resolvedStyle.translate.y, Is.EqualTo(expected).Within(1e-2f));
        }

        [Test]
        public void Given_ALinearTweenEnterWhoseOpacityEntryEasesInHalfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersEaseInPutsIt()
        {
            // Arrange — the play's own keyword and the entry's differ; Framer Motion's easeIn half way along.
            var config = new StyleTransitionConfig
            {
                DurationSec = 1f, Easing = EasingMode.Linear,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 1f, easing: EasingMode.EaseIn, delaySec: -0.5f) },
            };
            var expected = CubicBezierEvaluator.Evaluate(0.42f, 0f, 1f, 1f, 0.5f);

            // Act
            var opacity = FirstFrameOpacity(config);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base retimes no transition for a Motion tween, so the width keeps its curve.
        // Having StyleEaseCurve.OnRun retime every running transition of the element, not those its event reports, reddens it.
        [Test]
        public void Given_AWidthTransitionRunningOnTheElementsOwnEaseOut_When_ATweenEnterStartsOnIt_Then_TheWidthKeepsTheCurveItStartedOn()
        {
            // Arrange — width runs a second on an inline ease-out of the element's own from 100 s, a quarter of a second
            // before a tween enter of its opacity starts.
            var element = OnClock("opacity-0", e =>
            {
                e.style.width = 0f;
                e.style.transitionProperty = new List<StylePropertyName> { new("width") };
                e.style.transitionDuration = new List<TimeValue> { new(1f) };
                e.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOut) };
            });
            element.style.width = 100f;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            _now = 100.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            TweenEnter(element, "opacity-0", "opacity-100", OneSecond(EasingMode.EaseOut, 0f));
            FirstFrame(element);

            // Act
            _now = 100.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);

            // Assert — half way along UI Toolkit's own curve, which UssEasingTests holds UssEasing to.
            Assert.That(element.resolvedStyle.width / 100f, Is.EqualTo(UssEasing.Evaluate(EasingMode.EaseOut, 0.5f)).Within(0.01f));
        }

        [Test]
        public void Given_AnElementATweenEnterStartedOn_When_ItIsReleasedForAnotherConsumer_Then_NothingListensForItsTransitions()
        {
            // Arrange
            var element = OnClock("opacity-0");
            TweenEnter(element, "opacity-0", "opacity-100", OneSecond(EasingMode.EaseOut, 0f));
            var listenedBefore = Listens(element);

            // Act
            StyleEaseCurve.Release(element);

            // Assert — read as null where nothing listened before the release, which False never matches.
            Assert.That(listenedBefore == true ? Listens(element) : null, Is.False);
        }

        [Test]
        public void Given_AnElementATweenEnterStartedOn_When_EveryPlayIsCancelled_Then_NothingListensForItsTransitions()
        {
            // Arrange
            var element = OnClock("opacity-0");
            TweenEnter(element, "opacity-0", "opacity-100", OneSecond(EasingMode.EaseOut, 0f));
            var listenedBefore = Listens(element);

            // Act
            _scheduler.CancelAll();

            // Assert — read as null where nothing listened before the cancel, which False never matches.
            Assert.That(listenedBefore == true ? Listens(element) : null, Is.False);
        }
    }
}
