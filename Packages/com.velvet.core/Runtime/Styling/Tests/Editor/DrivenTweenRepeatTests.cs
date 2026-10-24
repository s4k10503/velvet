using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how a driven Tween plays the passes <see cref="MotionRepeat"/> adds, sampled with
    /// <see cref="BezierTweenDriver.SeekTo"/> and stepped with <see cref="BezierTweenDriver.Step"/>: each later pass
    /// restarts, runs back or mirrors the first, and a play past its last pass shows the end the repeat names.
    /// </summary>
    [TestFixture]
    internal sealed class DrivenTweenRepeatTests
    {
        private static BezierTweenState Tween(MotionRepeat repeat, EasingMode easing = EasingMode.Linear)
        {
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            return BezierTweenDriver.CreateTween(plan, new DrivenTweenTiming(0f, 1f, easing, null), repeat);
        }

        private static float OpacityAt(BezierTweenState state, float timeSec)
        {
            var element = new VisualElement();
            BezierTweenDriver.SeekTo(element, state, timeSec);
            return element.style.opacity.value;
        }

        [Test]
        public void Given_ALoopingTween_When_SoughtAQuarterIntoItsSecondPass_Then_ItsOpacityRestartedFromTheFromValue()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));

            // Act
            var opacity = OpacityAt(state, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void Given_AMirroringTween_When_SoughtAQuarterIntoItsSecondPass_Then_ItRunsTheEasingFromTheToValueBack()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Mirror, 0f), EasingMode.EaseIn);

            // Act
            var opacity = OpacityAt(state, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(1f - UssEasing.Evaluate(EasingMode.EaseIn, 0.25f)).Within(1e-5f));
        }

        [Test]
        public void Given_AReversingTween_When_SoughtAQuarterIntoItsSecondPass_Then_ItRunsBackAlongTheEasing()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f), EasingMode.EaseIn);

            // Act
            var opacity = OpacityAt(state, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(UssEasing.Evaluate(EasingMode.EaseIn, 0.75f)).Within(1e-5f));
        }

        [Test]
        public void Given_AnOddReversingTween_When_SoughtPastItsLastPass_Then_ItShowsTheFromValueAndIsDone()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f));
            var element = new VisualElement();

            // Act
            var done = BezierTweenDriver.SeekTo(element, state, 2.5f);

            // Assert
            Assert.That((done, element.style.opacity.value), Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_ALoopingTween_When_SteppedThroughItsFirstPass_Then_ItIsNotDone()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));

            // Act
            var done = BezierTweenDriver.Step(new VisualElement(), state, 1f);

            // Assert
            Assert.That(done, Is.False);
        }

        [Test]
        public void Given_ATweenWaitingOutADelay_When_SoughtInsideIt_Then_ItShowsTheFromValueWhateverItRepeats()
        {
            // Arrange
            var state = Tween(new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));
            state.DelaySec = 1f;

            // Act
            var opacity = OpacityAt(state, 0.5f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AnEndlessTweenWaitingOutADelay_When_SteppedToTheEndOfItsSecondPass_Then_ItShowsTheToValue()
        {
            // Arrange
            var state = Tween(new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0f));
            state.DelaySec = 0.3f;
            var element = new VisualElement();

            // Act
            BezierTweenDriver.Step(element, state, 2.3f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AnEndlessTweenWithALongDelay_When_SteppedInsideIt_Then_ItStillShowsTheFromValue()
        {
            // Arrange
            var state = Tween(new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0f));
            state.DelaySec = 5f;
            var element = new VisualElement();

            // Act
            BezierTweenDriver.Step(element, state, 1f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0f));
        }
    }
}
