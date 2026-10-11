using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the velocity a spring play keeps after being sampled at a time, which is what an interruption releases
    /// the next spring with: the slope of its curve at that time on the clock it is shown on, not the average over
    /// the seek that reached it.
    /// </summary>
    internal sealed class SpringSeekVelocityTests
    {
        private const float StepSec = 0.001f;

        private static MotionSpringState Opacity()
            => MotionSpringDriver.Create(MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" }),
                100f, 10f, 1f);

        private static float ValueAt(float timeSec)
        {
            var state = Opacity();
            MotionSpringDriver.SeekTo(new VisualElement(), state, timeSec);
            return state.Opacity.Integrator.Value;
        }

        // A reading taken a few thousandths either side of the time, from springs that were never seeked anywhere
        // else, so it holds no velocity the driver kept.
        private static float SlopeAt(float timeSec)
            => (ValueAt(timeSec + StepSec / 2f) - ValueAt(timeSec - StepSec / 2f)) / StepSec;

        [Test]
        public void Given_ASpringSeekedFromItsStartToAMovingTime_When_ItsVelocityIsRead_Then_ItIsTheCurvesSlopeThere()
        {
            // Arrange
            var state = Opacity();

            // Act
            MotionSpringDriver.SeekTo(new VisualElement(), state, 0.3f);

            // Assert
            Assert.That(state.Opacity.Integrator.Velocity, Is.EqualTo(SlopeAt(0.3f)).Within(0.05f));
        }

        [Test]
        public void Given_ASpringSeekedAtHalfATimeScale_When_ItsVelocityIsRead_Then_ItIsHalfTheCurvesSlope()
        {
            // Arrange
            var state = Opacity();

            // Act
            MotionSpringDriver.SeekTo(new VisualElement(), state, 0.3f, timeScale: 0.5f);

            // Assert
            Assert.That(state.Opacity.Integrator.Velocity, Is.EqualTo(SlopeAt(0.3f) * 0.5f).Within(0.05f));
        }

        [Test]
        public void Given_ASpringPlayMovingOnAHaltedPlayback_When_ItIsHalted_Then_ItsVelocityIsZero()
        {
            // Arrange
            var state = Opacity();
            MotionSpringDriver.SeekTo(new VisualElement(), state, 0.3f);

            // Act
            MotionSpringDriver.Halt(state);

            // Assert
            Assert.That(state.Opacity.Integrator.Velocity, Is.Zero);
        }
    }
}
