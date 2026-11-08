using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a reversal of a play whose PropertyOverrides entry started its driver before the play's own delay, which
    /// starts every value at once rather than waiting that shift out again, and a gesture handing a channel back,
    /// which leaves the timeline of every channel it did not hold as it was.
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

        [Test]
        public void Given_ASpringPlayWithALaterOpacityEntry_When_AGestureHandsTranslateBack_Then_OpacityKeepsItsOwnDelay()
        {
            // Arrange — opacity waits a second; a gesture held translate and lets it go half a second in.
            var element = new VisualElement();
            var from = new[] { "opacity-0", "translate-x-[0px]" };
            var to = new[] { "opacity-100", "translate-x-[64px]" };
            var state = MotionSpringDriver.WithOverrides(MotionSpringDriver.Create(Plan(), 100f, 10f, 1f),
                new[] { new StylePropertyTransition("opacity", durationSec: 0.3f, easing: EasingMode.Linear, delaySec: 1f) }, 0f)!;
            MotionSpringDriver.ApplyCurrentValues(element, state);
            MotionSpringDriver.Step(element, state, 0.5f);
            state.TranslateX!.Masked = true;
            MotionSpringDriver.ReturnChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Translate),
                MotionSlotContext.ReadImportant(element, from, to));

            // Act — 1.1 s since the play started: opacity a tenth of a second into its 0.3 s.
            MotionSpringDriver.Step(element, state, 0.6f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f / 3f).Within(1e-3f));
        }
    }
}
