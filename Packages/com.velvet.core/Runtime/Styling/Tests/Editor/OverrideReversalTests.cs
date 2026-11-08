using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a reversal of a play whose PropertyOverrides entry started its driver before the play's own delay: the
    /// reversal starts every value at once rather than waiting that shift out again.
    /// </summary>
    [TestFixture]
    internal sealed class OverrideReversalTests
    {
        private static MotionSpringClassParser.SpringPlan Plan()
            => MotionSpringClassParser.Resolve(new[] { "opacity-0", "translate-x-[0px]" },
                new[] { "opacity-100", "translate-x-[64px]" });

        // Opacity names no delay, so it starts with the play, 0.4 s before translate does.
        private static readonly StylePropertyTransition[] s_opacityAtOnce =
        {
            new("opacity", durationSec: 0.2f, easing: EasingMode.Linear, delaySec: 0f),
        };

        [Test]
        public void Given_ASpringPlayStartedEarlyByAnEntry_When_ItIsReversedMidFlight_Then_TranslateMovesBackAtOnce()
        {
            // Arrange
            var element = new VisualElement();
            var state = MotionSpringDriver.WithOverrides(MotionSpringDriver.Create(Plan(), 100f, 10f, 1f), s_opacityAtOnce, 0.4f)!;
            MotionSpringDriver.ApplyCurrentValues(element, state);
            MotionSpringDriver.Step(element, state, 0.5f);
            var before = element.style.translate.value.x.value;

            // Act
            MotionSpringDriver.Retarget(state);
            MotionSpringDriver.Step(element, state, 0.05f);

            // Assert
            Assert.That(element.style.translate.value.x.value, Is.LessThan(before));
        }

        [Test]
        public void Given_ABezierPlayStartedEarlyByAnEntry_When_ItIsReversedMidFlight_Then_TranslateMovesBackAtOnce()
        {
            // Arrange
            var element = new VisualElement();
            var state = BezierTweenDriver.WithOverrides(BezierTweenDriver.Create(Plan(), 0f, 0f, 1f, 1f, 1f), s_opacityAtOnce, 0.4f)!;
            BezierTweenDriver.ApplyCurrentValues(element, state);
            BezierTweenDriver.Step(element, state, 0.6f);
            var before = element.style.translate.value.x.value;

            // Act
            BezierTweenDriver.Retarget(state);
            BezierTweenDriver.Step(element, state, 0.05f);

            // Assert
            Assert.That(element.style.translate.value.x.value, Is.LessThan(before));
        }
    }
}
