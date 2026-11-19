using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The keyframe arithmetic of <c>animate-ping</c> and <c>animate-bounce</c>, and of <c>animate-pulse</c> over an
    /// opacity of its own, taken from Tailwind v4's theme: ping reaches <c>scale(2)</c> and opacity 0 at 75% on
    /// <c>cubic-bezier(0, 0, 0.2, 1)</c>; bounce lifts <c>translateY(-25%)</c> at 0% and 100%, none at 50%, easing the
    /// first half by <c>cubic-bezier(0.8, 0, 1, 1)</c> and the second by <c>cubic-bezier(0, 0, 0.2, 1)</c>. The
    /// curves' mid values are declared constants (the y of each curve at x = 0.5), not read back from the evaluator.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateKeyframeTests
    {
        private const float CurveAtHalf = 0.8392451f;

        [Test]
        public void Given_APingAtTheStartOfItsLoop_When_ProgressIsRead_Then_ItIsOnTheElementsOwnValues()
        {
            Assert.That(StyleAnimateDriver.PingProgress(0f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_APingHalfwayToItsEndKeyframe_When_ProgressIsRead_Then_ItFollowsTheOutCurve()
        {
            // Arrange / Act — 0.375 of the loop is the middle of the 0..75% interval.
            var progress = StyleAnimateDriver.PingProgress(0.375f);

            // Assert
            Assert.That(progress, Is.EqualTo(CurveAtHalf).Within(1e-4f));
        }

        [Test]
        public void Given_APingPastItsEndKeyframe_When_ProgressIsRead_Then_ItHoldsTheEndValues()
        {
            Assert.That(StyleAnimateDriver.PingProgress(0.9f), Is.EqualTo(1f));
        }

        [Test]
        public void Given_ABounceAtTheStartOfItsLoop_When_LiftIsRead_Then_ItIsFullyLifted()
        {
            Assert.That(StyleAnimateDriver.BounceLift(0f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_ABounceAMidwayThroughItsFirstHalf_When_LiftIsRead_Then_ItFollowsTheInCurve()
        {
            // Arrange / Act — 0.25 of the loop is the middle of the 0..50% interval, eased by cubic-bezier(0.8, 0, 1, 1).
            var lift = StyleAnimateDriver.BounceLift(0.25f);

            // Assert
            Assert.That(lift, Is.EqualTo(CurveAtHalf).Within(1e-4f));
        }

        [Test]
        public void Given_ABounceAtItsMiddleKeyframe_When_LiftIsRead_Then_ItIsDown()
        {
            Assert.That(StyleAnimateDriver.BounceLift(0.5f), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Given_ABounceMidwayThroughItsSecondHalf_When_LiftIsRead_Then_ItFollowsTheOutCurve()
        {
            // Arrange / Act — 0.75 of the loop is the middle of the 50..100% interval, eased by cubic-bezier(0, 0, 0.2, 1).
            var lift = StyleAnimateDriver.BounceLift(0.75f);

            // Assert
            Assert.That(lift, Is.EqualTo(CurveAtHalf).Within(1e-4f));
        }

        [Test]
        public void Given_ABounceAtTheEndOfItsLoop_When_LiftIsRead_Then_ItIsFullyLiftedAgain()
        {
            Assert.That(StyleAnimateDriver.BounceLift(1f), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AnUnturnedUnscaledElement_When_ItIsLiftedFully_Then_ItMovesAQuarterOfItsHeightUp()
        {
            // Arrange / Act
            var offset = StyleAnimateDriver.BounceOffsetPx(1f, 80f, 0f, 1f);

            // Assert
            Assert.That(offset.y, Is.EqualTo(-20f).Within(1e-4f));
        }

        [Test]
        public void Given_AnElementScaledToTwiceItsSize_When_ItIsLiftedFully_Then_TheLiftIsScaledWithIt()
        {
            // Arrange / Act — the keyframes' transform applies beneath the element's own scale property.
            var offset = StyleAnimateDriver.BounceOffsetPx(1f, 80f, 0f, 2f);

            // Assert
            Assert.That(offset.y, Is.EqualTo(-40f).Within(1e-4f));
        }

        [Test]
        public void Given_AnElementTurnedAQuarter_When_ItIsLiftedFully_Then_TheLiftRunsAlongItsTurnedAxis()
        {
            // Arrange / Act — a clockwise quarter turn carries the element's up to the right.
            var offset = StyleAnimateDriver.BounceOffsetPx(1f, 80f, 90f, 1f);

            // Assert
            Assert.That(offset.x, Is.EqualTo(20f).Within(1e-4f));
        }

        [Test]
        public void Given_AnElementTurnedAQuarter_When_ItIsLiftedFully_Then_NoneOfTheLiftRemainsVertical()
        {
            // Arrange / Act
            var offset = StyleAnimateDriver.BounceOffsetPx(1f, 80f, 90f, 1f);

            // Assert
            Assert.That(offset.y, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Given_APulseOverAnOpacityOfItsOwn_When_AQuarterThroughItsLoop_Then_ItIsHalfwayFromThatOpacityToHalf()
        {
            // Arrange / Act — the pulse names no opacity at 0%, so the falling half starts from the element's own.
            var opacity = StyleAnimateDriver.PulseOpacityOver(0.75f, 0.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0.625f).Within(1e-4f));
        }

        [Test]
        public void Given_APulseOverAnOpacityOfItsOwn_When_ThreeQuartersThroughItsLoop_Then_ItIsHalfwayBackToThatOpacity()
        {
            // Arrange / Act — the rising half ends on the element's own opacity, not on full.
            var opacity = StyleAnimateDriver.PulseOpacityOver(0.75f, 0.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0.625f).Within(1e-4f));
        }

        [Test]
        public void Given_TheBounceToken_When_Extracted_Then_ItIsABounceOfOneSecond()
        {
            // Arrange / Act
            StyleAnimateClass.TryExtract(new[] { "animate-bounce" }, out var spec);

            // Assert
            Assert.That((spec.Mode, spec.DurationSec), Is.EqualTo((AnimateMode.Bounce, 1f)));
        }

        [Test]
        public void Given_ThePingToken_When_Extracted_Then_ItIsAPingOfOneSecond()
        {
            // Arrange / Act
            StyleAnimateClass.TryExtract(new[] { "animate-ping" }, out var spec);

            // Assert
            Assert.That((spec.Mode, spec.DurationSec), Is.EqualTo((AnimateMode.Ping, 1f)));
        }

        [Test]
        public void Given_APingTokenWithADuration_When_Extracted_Then_TheOverrideWins()
        {
            // Arrange / Act
            StyleAnimateClass.TryExtract(new[] { "animate-ping-[2500ms]" }, out var spec);

            // Assert
            Assert.That((spec.Mode, spec.DurationSec), Is.EqualTo((AnimateMode.Ping, 2.5f)));
        }

        [Test]
        public void Given_APingHeldOffOpacityByAMotionDriver_When_TheLoopIsReasserted_Then_OnlyItsScaleIsWritten()
        {
            // Arrange — a Motion play holds opacity against the loop, as a spring or bezier opacity channel does.
            var element = new VisualElement();
            var ping = StyleAnimateDriver.Attach(element, new AnimateSpec(AnimateMode.Ping, 1f), panVertical: false);
            var owner = new object();
            try
            {
                StyleAnimateDriver.HoldAgainstLoop(element, owner, MotionTransitionSlots.Opacity);
                element.style.opacity = 0.2f;

                // Act
                StyleAnimateDriver.ReassertLoop(element, MotionTransitionSlots.Opacity);

                // Assert
                Assert.That((element.style.opacity.value, element.style.scale.keyword),
                    Is.EqualTo((0.2f, StyleKeyword.Undefined)));
            }
            finally
            {
                StyleAnimateDriver.HoldAgainstLoop(element, owner, MotionTransitionSlots.None);
                StyleAnimateDriver.Detach(element, ping);
            }
        }
    }
}
