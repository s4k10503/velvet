using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the transition <c>Animator.Animate</c> plays a value on where its caller names none or only orchestration,
    /// the repeat options it honours, and how a sequence segment differs from a single animation in both.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateTransitionDefaultsTests
    {
        private static Animator NewAnimator() => new(new Ref<VisualElement>());

        private static StyleTransitionConfig OneSecondEaseIn(TransitionRepeatType type, float repeatDelaySec = 0f)
            => new() { DurationSec = 1f, Easing = EasingMode.EaseIn, Repeat = 1f, RepeatType = type, RepeatDelaySec = repeatDelaySec };

        private static float TranslateX(VisualElement element) => element.style.translate.value.x.value;

        [Test]
        public void Given_AnXAnimationWithNoTransition_When_ATenthOfASecondPasses_Then_ItIsWhereTheTransformSpringPutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } });

            // Act
            controls.Tick(0.1f);

            // Assert
            Assert.That(TranslateX(element),
                Is.EqualTo(MotionSpringDriver.SampleSpring(0f, 100f, 1f, 0f, 0.1, (500f, 25f, 1f), null, 10f)).Within(1e-3f));
        }

        [Test]
        public void Given_AScaleAnimationToZeroWithNoTransition_When_ATenthOfASecondPasses_Then_ItIsWhereTheCriticallyDampedScaleSpringPutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Scale = new[] { 1f, 0f } });

            // Act
            controls.Tick(0.1f);

            // Assert
            Assert.That(element.style.scale.value.value.x,
                Is.EqualTo(MotionSpringDriver.SampleSpring(1f, 0f, 1f, 0f, 0.1, (550f, 2f * Mathf.Sqrt(550f), 1f), null, 10f))
                    .Within(1e-4f));
        }

        [Test]
        public void Given_AnOpacityAnimationWithNoTransition_When_HalfOfThreeTenthsOfASecondPasses_Then_ItIsWhereTheDefaultEasePutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } });

            // Act
            controls.Tick(0.15f);

            // Assert
            Assert.That(element.style.opacity.value,
                Is.EqualTo(CubicBezierEvaluator.Evaluate(0.25f, 0.1f, 0.35f, 1f, 0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesWithNoTransition_When_ThreeQuartersOfASecondPass_Then_TheyAreStillPlaying()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } });

            // Act
            controls.Tick(0.75f);

            // Assert
            Assert.That(controls.IsFinished, Is.False);
        }

        [Test]
        public void Given_ThreeKeyframesOnAnEaseOutSecond_When_AQuarterSecondPasses_Then_TheFirstPairIsHalfwayAlongItsOwnEase()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f, 200f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseOut });

            // Act
            controls.Tick(0.25f);

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(100f * UssEasing.Evaluate(EasingMode.EaseOut, 0.5f)).Within(1e-3f));
        }

        [Test]
        public void Given_AnXAnimationWhoseTransitionSetsOnlyADelay_When_TheDelayAndATenthOfASecondPass_Then_ItIsWhereTheTransformSpringPutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                new StyleTransitionConfig { DelaySec = 0.1f });

            // Act
            controls.Tick(0.2f);

            // Assert
            Assert.That(TranslateX(element),
                Is.EqualTo(MotionSpringDriver.SampleSpring(0f, 100f, 1f, 0f, 0.1, (500f, 25f, 1f), null, 10f)).Within(1e-3f));
        }

        [Test]
        public void Given_AReverseRepeat_When_AQuarterOfTheSecondPassHasRun_Then_ThePassPlaysTheFirstBackwardsInTime()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                OneSecondEaseIn(TransitionRepeatType.Reverse));

            // Act
            controls.Tick(1.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(UssEasing.Evaluate(EasingMode.EaseIn, 0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_AMirrorRepeat_When_AQuarterOfTheSecondPassHasRun_Then_ThePassRunsFromTheLastKeyframeOnTheSameEase()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                OneSecondEaseIn(TransitionRepeatType.Mirror));

            // Act
            controls.Tick(1.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - UssEasing.Evaluate(EasingMode.EaseIn, 0.25f)).Within(1e-4f));
        }

        [Test]
        public void Given_ALoopRepeatWithARepeatDelay_When_TheDelayAndAQuarterOfTheSecondPassHaveRun_Then_ThePassHasRestarted()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                OneSecondEaseIn(TransitionRepeatType.Loop, repeatDelaySec: 0.5f));

            // Act
            controls.Tick(1.75f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(UssEasing.Evaluate(EasingMode.EaseIn, 0.25f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceSegmentRepeatingInReverse_When_AQuarterOfTheSecondPassHasRun_Then_ThePassIsMirrored()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f } }, OneSecondEaseIn(TransitionRepeatType.Reverse)),
            });

            // Act
            controls.Tick(1.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - UssEasing.Evaluate(EasingMode.EaseIn, 0.25f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceSpringSegmentOfThreeKeyframesNamingNoDuration_When_HalfOfThreeTenthsOfASecondPasses_Then_ItTweensToTheMiddleKeyframe()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { X = new[] { 0f, 50f, 100f } },
                    new StyleTransitionConfig { Type = TransitionType.Spring, Easing = EasingMode.Linear }),
            });

            // Act
            controls.Tick(0.15f);

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(50f).Within(1e-3f));
        }

        [Test]
        public void Given_ASequenceSpringSegmentOfTwoKeyframes_When_ItsSpringOverThatTravelHasRested_Then_TheSequenceIsFinished()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { X = new[] { 0f, 10f } },
                    new StyleTransitionConfig { Type = TransitionType.Spring }),
            });

            // Act
            controls.Tick(MotionSpringDriver.PassDurationSec(10f, 100f, 10f, 1f));

            // Assert
            Assert.That(controls.IsFinished, Is.True);
        }

        [Test]
        public void Given_AnAnimationStopped_When_ItsFinishedTaskIsRead_Then_ItHasNotSettled()
        {
            // Arrange
            var controls = NewAnimator().Animate(new VisualElement(), new AnimateValues { Opacity = new[] { 0f, 1f } });

            // Act
            controls.Stop();

            // Assert
            Assert.That(controls.IsStopped ? controls.Finished.Status : (VelvetTaskStatus)(-1), Is.EqualTo(VelvetTaskStatus.Pending));
        }

        [Test]
        public void Given_AnAnimationCancelled_When_ItsFinishedTaskIsRead_Then_ItHasNotSettled()
        {
            // Arrange
            var controls = NewAnimator().Animate(new VisualElement(), new AnimateValues { Opacity = new[] { 0f, 1f } });

            // Act
            controls.Cancel();

            // Assert
            Assert.That(controls.IsStopped ? controls.Finished.Status : (VelvetTaskStatus)(-1), Is.EqualTo(VelvetTaskStatus.Pending));
        }
    }
}
