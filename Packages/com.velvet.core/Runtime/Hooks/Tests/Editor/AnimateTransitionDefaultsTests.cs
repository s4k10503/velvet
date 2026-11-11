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
