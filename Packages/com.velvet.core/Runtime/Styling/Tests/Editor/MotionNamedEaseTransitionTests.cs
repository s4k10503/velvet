using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a Motion tween UI Toolkit's own transition plays runs a named ease on Framer Motion's curve for it,
    /// each property on the keyword its own entry names, and that an element handed to another consumer stops being
    /// listened to for it.
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

        // An element a one-second tween enter from opacity-0 to opacity-100 has started on, on the panel's own clock,
        // where it plays as UI Toolkit's transition.
        private VisualElement TweenEnter(float delaySec, EasingMode easing, IReadOnlyList<StylePropertyTransition> overrides)
        {
            VelvetStyleUtilities.AttachTo(_window.rootVisualElement);
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            element.AddToClassList("opacity-0");
            ForcePanelUpdate(element.panel);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            _scheduler = new StyleAnimationScheduler();
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                durationSec: 1f, easing: easing, delaySec: delaySec, propertyOverrides: overrides);
            ForcePanelUpdate(element.panel);
            return element;
        }

        private float FirstFrameOpacity(float delaySec, EasingMode easing, IReadOnlyList<StylePropertyTransition> overrides)
        {
            var element = TweenEnter(delaySec, easing, overrides);
            _now += 0.016;
            EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            return element.resolvedStyle.opacity;
        }

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
            var opacity = FirstFrameOpacity(-0.25f, EasingMode.EaseOut, null);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_ATweenEnterEasingOutBackThreeTenthsOfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersBackOutPutsIt()
        {
            // Arrange — Framer Motion's backOut three tenths of the way along, short of its overshoot.
            var expected = CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, 0.3f);

            // Act
            var opacity = FirstFrameOpacity(-0.3f, EasingMode.EaseOutBack, null);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_ATweenEnterEasingInBackSevenTenthsOfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersBackInPutsIt()
        {
            // Arrange — Framer Motion's backIn, backOut reversed, seven tenths of the way along, past its dip below 0.
            var expected = 1f - CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, 0.3f);

            // Act
            var opacity = FirstFrameOpacity(-0.7f, EasingMode.EaseInBack, null);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_ALinearTweenEnterWhoseOpacityEntryEasesInHalfASecondIn_When_TheFirstFrameRuns_Then_OpacityIsWhereFramersEaseInPutsIt()
        {
            // Arrange — the play's own keyword and the entry's differ; Framer Motion's easeIn half way along.
            var overrides = new[] { new StylePropertyTransition("opacity", durationSec: 1f, easing: EasingMode.EaseIn, delaySec: -0.5f) };
            var expected = CubicBezierEvaluator.Evaluate(0.42f, 0f, 1f, 1f, 0.5f);

            // Act
            var opacity = FirstFrameOpacity(0f, EasingMode.Linear, overrides);

            // Assert
            Assert.That(opacity, Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Given_AnElementATweenEnterStartedOn_When_ItIsReleasedForAnotherConsumer_Then_NothingListensForItsTransitions()
        {
            // Arrange
            var element = TweenEnter(0f, EasingMode.EaseOut, null);
            var listenedBefore = Listens(element);

            // Act
            StyleEaseCurve.Release(element);

            // Assert — read as null where nothing listened before the release, which False never matches.
            Assert.That(listenedBefore == true ? Listens(element) : null, Is.False);
        }
    }
}
