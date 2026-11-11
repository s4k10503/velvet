using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the transition <c>Animator.Animate</c> plays a value on where its caller names none or only orchestration,
    /// the repeat options it honours, and how a sequence segment differs from a single animation in both; the curves
    /// its named eases take; and how a single call eases opacity's keyframes.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateTransitionDefaultsTests
    {
        private static Animator NewAnimator() => new(new Ref<VisualElement>());

        private static StyleTransitionConfig OneSecondEaseIn(TransitionRepeatType type, float repeatDelaySec = 0f)
            => new() { DurationSec = 1f, Easing = EasingMode.EaseIn, Repeat = 1f, RepeatType = type, RepeatDelaySec = repeatDelaySec };

        private static float TranslateX(VisualElement element) => element.style.translate.value.x.value;

        // Framer Motion's easeIn and easeOut.
        private static float FramerEaseIn(float t) => CubicBezierEvaluator.Evaluate(0.42f, 0f, 1f, 1f, t);

        private static float FramerEaseOut(float t) => CubicBezierEvaluator.Evaluate(0f, 0f, 0.58f, 1f, t);

        // Framer Motion's backOut, and backIn and backInOut as back.ts derives them: the reverse and its mirror.
        private static float FramerBackOut(float t) => CubicBezierEvaluator.Evaluate(0.33f, 1.53f, 0.69f, 0.99f, t);

        private static float FramerBackIn(float t) => 1f - FramerBackOut(1f - t);

        private static float FramerBackInOut(float t)
            => t <= 0.5f ? FramerBackIn(2f * t) / 2f : (2f - FramerBackIn(2f * (1f - t))) / 2f;

        // The backIn Framer's supportedWaapiEasing table gives the Web Animations API.
        private static float WebAnimationsBackIn(float t) => CubicBezierEvaluator.Evaluate(0.31f, 0.01f, 0.66f, -0.59f, t);

        private static float OpacityAfter(AnimateControls controls, VisualElement element, float elapsedSec)
        {
            controls.Tick(elapsedSec);
            return element.style.opacity.value;
        }

        private static float XAfterOneSecondTween(EasingMode easing, float elapsedSec)
        {
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = easing });
            controls.Tick(elapsedSec);
            return TranslateX(element);
        }

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
        public void Given_ASequenceSegmentWithNoTransition_When_HalfOfThreeTenthsOfASecondPasses_Then_ItIsHalfwayAlongTheEaseOut()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[] { new AnimateSegment(element, new AnimateValues { X = new[] { 0f, 100f } }) });

            // Act
            controls.Tick(0.15f);

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(100f * FramerEaseOut(0.5f)).Within(1e-3f));
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
            Assert.That(TranslateX(element), Is.EqualTo(100f * FramerEaseOut(0.5f)).Within(1e-3f));
        }

        [Test]
        public void Given_AnXTweenEasingInBack_When_SevenTenthsOfItsSecondPass_Then_XIsWhereFramersBackInPutsIt()
        {
            // Arrange
            var expected = 100f * FramerBackIn(0.7f);

            // Act
            var x = XAfterOneSecondTween(EasingMode.EaseInBack, 0.7f);

            // Assert
            Assert.That(x, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnXTweenEasingOutBack_When_ThreeTenthsOfItsSecondPass_Then_XIsWhereFramersBackOutPutsIt()
        {
            // Arrange
            var expected = 100f * FramerBackOut(0.3f);

            // Act
            var x = XAfterOneSecondTween(EasingMode.EaseOutBack, 0.3f);

            // Assert
            Assert.That(x, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnXTweenEasingInOutBack_When_AQuarterOfItsSecondPasses_Then_XIsWhereFramersBackInOutPutsItOnItsFirstHalf()
        {
            // Arrange
            var expected = 100f * FramerBackInOut(0.25f);

            // Act
            var x = XAfterOneSecondTween(EasingMode.EaseInOutBack, 0.25f);

            // Assert
            Assert.That(x, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnXTweenEasingInOutBack_When_ThreeQuartersOfItsSecondPass_Then_XIsWhereFramersBackInOutPutsItOnItsSecondHalf()
        {
            // Arrange
            var expected = 100f * FramerBackInOut(0.75f);

            // Act
            var x = XAfterOneSecondTween(EasingMode.EaseInOutBack, 0.75f);

            // Assert
            Assert.That(x, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnOpacityTweenEasingInBack_When_ThreeQuartersOfItsSecondPass_Then_OpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInBack });

            // Act
            var opacity = OpacityAfter(controls, element, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_AnOpacityTweenEasingInBackWithARepeatDelay_When_ThreeQuartersOfItsFirstPassRun_Then_OpacityIsWhereBackTsBackInPutsIt()
        {
            // Arrange — a repeat delay keeps Framer's opacity off the Web Animations API.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInBack, Repeat = 1f, RepeatDelaySec = 0.5f });

            // Act
            var opacity = OpacityAfter(controls, element, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(FramerBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceSegmentOfOpacityEasingInBack_When_ThreeQuartersOfItsSecondPass_Then_OpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                    new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInBack }),
            });

            // Act
            var opacity = OpacityAfter(controls, element, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceSegmentOfOpacityEasingInBackWithARepeatDelay_When_ThreeQuartersOfItsFirstPassRun_Then_OpacityIsWhereTheWebAnimationsBackInPutsIt()
        {
            // Arrange — a sequence writes the repeat into its keyframes, which leaves the animation no repeat delay.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                    new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInBack, Repeat = 1f, RepeatDelaySec = 0.5f }),
            });

            // Act
            var opacity = OpacityAfter(controls, element, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(WebAnimationsBackIn(0.75f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnAnEaseInCircSecond_When_SevenTenthsOfItPass_Then_OpacityIsWhereCircInOverTheWholeRunPutsIt()
        {
            // Arrange — Framer's supportedWaapiEasing gives circIn cubic-bezier(0, 0.65, 0.55, 1), circOut's shape, which
            // inverts its own circIn, 1 - sin(acos(p)); this keeps circIn's curve over the run the Web Animations path eases.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseInCirc });
            var along = 2f * (1f - Mathf.Sqrt(1f - 0.7f * 0.7f));

            // Act
            var opacity = OpacityAfter(controls, element, 0.7f);

            // Assert
            Assert.That(opacity, Is.EqualTo(2f - along).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnABezierWhoseFirstXPassesOne_When_ThreeTenthsOfItsSecondPass_Then_TheFirstPairIsAlongTheCurve()
        {
            // Arrange — the browser refuses a cubic-bezier with an x outside [0, 1], so Framer eases each pair in JavaScript.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 1f, BezierX1 = 1.2f, BezierY1 = 0f, BezierX2 = 0.8f, BezierY2 = 1f });

            // Act
            var opacity = OpacityAfter(controls, element, 0.3f);

            // Assert
            Assert.That(opacity, Is.EqualTo(CubicBezierEvaluator.Evaluate(1.2f, 0f, 0.8f, 1f, 0.6f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnABezierWhoseSecondXPassesOne_When_ThreeTenthsOfItsSecondPass_Then_TheFirstPairIsAlongTheCurve()
        {
            // Arrange — the browser refuses a cubic-bezier with an x outside [0, 1], so Framer eases each pair in JavaScript.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 1f, BezierX1 = 0.2f, BezierY1 = 0f, BezierX2 = 1.3f, BezierY2 = 1f });

            // Act
            var opacity = OpacityAfter(controls, element, 0.3f);

            // Assert
            Assert.That(opacity, Is.EqualTo(CubicBezierEvaluator.Evaluate(0.2f, 0f, 1.3f, 1f, 0.6f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnATweenCarryingABezierXItIgnores_When_HalfASecondPasses_Then_OpacityIsWhereOneEaseInOverTheWholeRunPutsIt()
        {
            // Arrange — a Tween eases by its Easing, so the control points it carries do not reach the browser.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseIn, BezierX1 = 1.2f });

            // Act
            var opacity = OpacityAfter(controls, element, 0.5f);

            // Assert
            Assert.That(opacity, Is.EqualTo(2f * FramerEaseIn(0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeXKeyframesWithNoTransition_When_AQuarterOfTheirEightTenthsOfASecondPasses_Then_TheFirstPairIsHalfwayAlongItsOwnEaseOut()
        {
            // Arrange
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f, 200f } });

            // Act
            controls.Tick(0.2f);

            // Assert
            Assert.That(TranslateX(element), Is.EqualTo(100f * FramerEaseOut(0.5f)).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesWithNoTransition_When_HalfOfTheirEightTenthsOfASecondPasses_Then_OpacityIsWhereOneEaseOutOverTheWholeRunPutsIt()
        {
            // Arrange — halfway along the run is the middle keyframe; the run eased out is past it, on the second pair.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } });

            // Act
            controls.Tick(0.4f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(2f - 2f * FramerEaseOut(0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnAnEaseInSecond_When_HalfASecondPasses_Then_OpacityIsWhereOneEaseInOverTheWholeRunPutsIt()
        {
            // Arrange — halfway along the run is the middle keyframe; the run eased in is short of it, on the first pair.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { DurationSec = 1f, Easing = EasingMode.EaseIn });

            // Act
            controls.Tick(0.5f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(2f * FramerEaseIn(0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesOnABezierOvershootingTheirRun_When_HalfASecondPasses_Then_OpacityCarriesOnAlongTheLastPair()
        {
            // Arrange — the curve stands at 1.25 half way along, so the run is half a pair past the last keyframe.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0.5f } },
                new StyleTransitionConfig
                {
                    Type = TransitionType.Bezier, DurationSec = 1f, BezierX1 = 0.25f, BezierY1 = 1.5f, BezierX2 = 0.75f, BezierY2 = 1.5f,
                });

            // Act
            controls.Tick(0.5f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.25f).Within(1e-3f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesRepeatingAsAMirrorWithNoTransition_When_SevenTenthsOfTheFirstPassHaveRun_Then_TheSecondPairIsAlongItsOwnEaseOut()
        {
            // Arrange — seven tenths of the run is four tenths of the way along the second pair.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { Repeat = 1f, RepeatType = TransitionRepeatType.Mirror });

            // Act
            controls.Tick(0.56f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - FramerEaseOut(0.4f)).Within(1e-4f));
        }

        [Test]
        public void Given_ThreeOpacityKeyframesWithARepeatDelayAndNoTransition_When_SevenTenthsOfTheFirstPassHaveRun_Then_TheSecondPairIsAlongItsOwnEaseOut()
        {
            // Arrange — seven tenths of the run is four tenths of the way along the second pair.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } },
                new StyleTransitionConfig { Repeat = 1f, RepeatDelaySec = 0.5f });

            // Act
            controls.Tick(0.56f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - FramerEaseOut(0.4f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASequenceSegmentOfThreeOpacityKeyframesWithNoTransition_When_SevenTenthsOfItsThreeTenthsOfASecondPass_Then_TheSecondPairIsAlongItsOwnEaseOut()
        {
            // Arrange — seven tenths of the run is four tenths of the way along the second pair.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f, 0f } }),
            });

            // Act
            controls.Tick(0.21f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - FramerEaseOut(0.4f)).Within(1e-4f));
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
            Assert.That(element.style.opacity.value, Is.EqualTo(FramerEaseIn(0.75f)).Within(1e-4f));
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
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - FramerEaseIn(0.25f)).Within(1e-4f));
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
            Assert.That(element.style.opacity.value, Is.EqualTo(FramerEaseIn(0.25f)).Within(1e-4f));
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
            Assert.That(element.style.opacity.value, Is.EqualTo(1f - FramerEaseIn(0.25f)).Within(1e-4f));
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

        private static StyleTransitionConfig OneSecondLinearOverriding(StylePropertyTransition entry)
            => new() { DurationSec = 1f, Easing = EasingMode.Linear, PropertyOverrides = new[] { entry } };

        [Test]
        public void Given_ASingleCallWhoseOpacityEntryNamesOnlyADuration_When_AQuarterSecondPasses_Then_OpacityEasesOutOverThatDuration()
        {
            // Arrange — the entry replaces the call's linear second rather than inheriting its easing.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                OneSecondLinearOverriding(new StylePropertyTransition("opacity", durationSec: 0.5f)));

            // Act
            controls.Tick(0.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(FramerEaseOut(0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASingleCallWhoseTranslateEntryNamesOnlyADelay_When_TheDelayAndATenthOfASecondPass_Then_XIsWhereTheTransformSpringPutsIt()
        {
            // Arrange — an entry naming nothing but a delay is no transition, so the value takes its default.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                OneSecondLinearOverriding(new StylePropertyTransition("translate", delaySec: 0.1f)));

            // Act
            controls.Tick(0.2f);

            // Assert
            Assert.That(TranslateX(element),
                Is.EqualTo(MotionSpringDriver.SampleSpring(0f, 100f, 1f, 0f, 0.1, (500f, 25f, 1f), null, 10f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASequenceSegmentWhoseOpacityEntryNamesOnlyADuration_When_AQuarterSecondPasses_Then_ItKeepsTheSegmentsEasing()
        {
            // Arrange — a sequence lays the entry over the segment's transition rather than in place of it.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(new[]
            {
                new AnimateSegment(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                    OneSecondLinearOverriding(new StylePropertyTransition("opacity", durationSec: 0.5f))),
            });

            // Act
            controls.Tick(0.25f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ASingleCallOnASpringThatRestsPastTwentySeconds_When_TwentyFiveSecondsPass_Then_ItIsStillPlaying()
        {
            // Arrange — a damping ratio of 0.015 decays a travel of 100 below its rest distance only after about 35 s.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 0.3f, Mass = 1f });

            // Act
            controls.Tick(25f);

            // Assert
            Assert.That(controls.IsFinished, Is.False);
        }

        [Test]
        public void Given_ASingleCallWhoseEntryNamesADelayOfZero_When_ThreeQuartersOfASecondPass_Then_OpacityWaitedTheCallsDelay()
        {
            // Arrange — Framer reads `valueTransition.delay || transition.delay`, so the entry's 0 takes the call's 0.5 s.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { Opacity = new[] { 0f, 1f } },
                new StyleTransitionConfig
                {
                    DurationSec = 2f, Easing = EasingMode.Linear, DelaySec = 0.5f,
                    PropertyOverrides = new[]
                    {
                        new StylePropertyTransition("opacity", durationSec: 1f, easing: EasingMode.Linear, delaySec: 0f),
                    },
                });

            // Act
            controls.Tick(0.75f);

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Given_ASingleCallOnAnUndampedSpring_When_TwentyFiveSecondsPass_Then_ItIsStillPlaying()
        {
            // Arrange — a spring with no damping never rests, so it runs on as Framer's does.
            var element = new VisualElement();
            var controls = NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 0f, Mass = 1f });

            // Act
            controls.Tick(25f);

            // Assert
            Assert.That(controls.IsFinished, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base measured a single call's spring for at most 20 s, which this case
        // pins against a search for its rest that runs on past that up front.
        [Test]
        public void Given_ASingleCallOnAnUndampedSpring_When_ItIsStarted_Then_ItReturnsWithoutSearchingForARestItNeverReaches()
        {
            // Arrange — a search to the rest that never comes would take seconds on the calling thread.
            var element = new VisualElement();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            NewAnimator().Animate(element, new AnimateValues { X = new[] { 0f, 100f } },
                new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 100f, Damping = 0f, Mass = 1f });
            watch.Stop();

            // Assert
            Assert.That(watch.Elapsed.TotalMilliseconds, Is.LessThan(250.0));
        }
    }
}
