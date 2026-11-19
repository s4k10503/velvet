using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which channels of a driven play ease <c>EaseInBack</c> as Framer Motion's Web Animations path does: its
    /// opacity, on the play's own timing or on a <c>PropertyOverrides</c> entry's, where no repeat delay keeps Framer's
    /// opacity off that path; a translate eases by back.ts's backIn.
    /// </summary>
    [TestFixture]
    internal sealed class MotionAcceleratedEaseTests
    {
        private static readonly StylePropertyTransition[] s_opacityBackIn =
        {
            new("opacity", durationSec: 1f, easing: EasingMode.EaseInBack),
        };

        private static MotionSpringClassParser.SpringPlan Opacity()
            => MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });

        // The supportedWaapiEasing backIn, and back.ts's: backOut reversed.
        private static float WebAnimationsBackIn(float t) => CubicBezierEvaluator.Evaluate(0.31f, 0.01f, 0.66f, -0.59f, t);

        private static float FramerBackIn(float t) => 1f - CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, 1f - t);

        private static float OpacityAt(BezierTweenState state, float timeSec)
        {
            var element = new VisualElement();
            BezierTweenDriver.SeekTo(element, state, timeSec);
            return element.style.opacity.value;
        }

        [Test]
        public void Given_ADrivenTweenEasingInBack_When_SoughtThreeQuartersIn_Then_ItsOpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var state = BezierTweenDriver.CreateTween(Opacity(), new DrivenTweenTiming(0f, 1f, EasingMode.EaseInBack, null), default);

            // Act
            var opacity = OpacityAt(state, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ADrivenTweenEasingInBackWithARepeatDelay_When_SoughtThreeQuartersIntoItsFirstPass_Then_ItsOpacityIsWhereBackTsBackInPutsIt()
        {
            // Arrange
            var state = BezierTweenDriver.CreateTween(Opacity(), new DrivenTweenTiming(0f, 1f, EasingMode.EaseInBack, null),
                new MotionRepeat(1f, TransitionRepeatType.Loop, 0.5f));

            // Act
            var opacity = OpacityAt(state, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(FramerBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ADrivenTweenOfATranslateEasingInBack_When_SoughtThreeQuartersIn_Then_ItIsWhereBackTsBackInPutsIt()
        {
            // Arrange
            var plan = MotionSpringClassParser.Resolve(new[] { "translate-x-[0px]" }, new[] { "translate-x-[100px]" });
            var state = BezierTweenDriver.CreateTween(plan, new DrivenTweenTiming(0f, 1f, EasingMode.EaseInBack, null), default);
            var element = new VisualElement();

            // Act
            BezierTweenDriver.SeekTo(element, state, 0.75f);

            // Assert
            Assert.That(element.style.translate.value.x.value, Is.EqualTo(100f * FramerBackIn(0.75f)).Within(1e-2f));
        }

        [Test]
        public void Given_ADrivenTweenWhoseOpacityEntryEasesInBack_When_SoughtThreeQuartersIn_Then_ItsOpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var state = BezierTweenDriver.CreateTween(Opacity(),
                new DrivenTweenTiming(0f, 1f, EasingMode.Linear, s_opacityBackIn), default);

            // Act
            var opacity = OpacityAt(state, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ABezierPlayWhoseOpacityEntryEasesInBack_When_SoughtThreeQuartersIn_Then_ItsOpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var state = BezierTweenDriver.WithOverrides(BezierTweenDriver.Create(Opacity(), 0f, 0f, 1f, 1f, 1f), s_opacityBackIn, 0f);

            // Act
            var opacity = OpacityAt(state!, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASpringPlayWhoseOpacityEntryEasesInBack_When_SoughtThreeQuartersIn_Then_ItsOpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var state = MotionSpringDriver.WithOverrides(MotionSpringDriver.Create(Opacity(), 100f, 10f, 1f), s_opacityBackIn, 0f);
            var element = new VisualElement();

            // Act
            MotionSpringDriver.SeekTo(element, state!, 0.75f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }
    }
}
