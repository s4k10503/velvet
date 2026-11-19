using System.Collections.Generic;
using System.Linq;
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

        private static float[] Velocities(MotionSpringState state)
        {
            var channels = new List<SpringChannel>();
            foreach (var channel in new[] { state.Opacity, state.TranslateX, state.TranslateY, state.Scale, state.Rotate })
            {
                if (channel != null) channels.Add(channel);
            }
            channels.AddRange((state.Colors ?? new List<SpringColorChannel>()).Select(c => c.Progress));
            channels.AddRange((state.Lengths ?? new List<SpringLengthChannel>()).Select(l => l.Value));
            return channels.Select(c => c.Integrator.Velocity).ToArray();
        }

        // Each case moves one kind of channel, so the cut that leaves that kind's call out is the only one it reddens.
        [TestCase("translate-x-[0px]", "translate-x-[100px]")]
        [TestCase("translate-y-[0px]", "translate-y-[100px]")]
        [TestCase("scale-50", "scale-150")]
        [TestCase("scale-33", "-scale-67")]
        [TestCase("rotate-0", "rotate-90")]
        [TestCase("bg-[#000000]", "bg-[#ffffff]")]
        [TestCase("w-[0px]", "w-[100px]")]
        public void Given_ASpringPlayMovingOnAChannelKind_When_ItIsHalted_Then_EveryChannelsVelocityIsZero(
            string from, string to)
        {
            // Arrange
            var state = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(new[] { from }, new[] { to }),
                100f, 10f, 1f);
            MotionSpringDriver.SeekTo(new VisualElement(), state, 0.3f);
            var movedBefore = Velocities(state).Any(v => v != 0f);

            // Act
            MotionSpringDriver.Halt(state);

            // Assert
            Assert.That((movedBefore, Velocities(state).All(v => v == 0f)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ASpringPlayMovingAfterAStep_When_SteppedByNoTime_Then_ItsVelocityIsZero()
        {
            // Arrange
            var state = Opacity();
            var element = new VisualElement();
            MotionSpringDriver.Step(element, state, 0.1f);
            var movedBefore = state.Opacity.Integrator.Velocity != 0f;

            // Act
            MotionSpringDriver.Step(element, state, 0f);

            // Assert
            Assert.That((movedBefore, state.Opacity.Integrator.Velocity), Is.EqualTo((true, 0f)));
        }
    }
}
