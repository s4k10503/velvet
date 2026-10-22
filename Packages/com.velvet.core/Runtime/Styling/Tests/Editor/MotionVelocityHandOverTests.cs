using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which channels of a spring take the velocity of the play or the native transition it interrupts, and the
    /// reads that velocity comes from, without a panel.
    /// </summary>
    [TestFixture]
    internal sealed class MotionVelocityHandOverTests
    {
        private const float Slope = 100f;

        private static BezierTweenState LinearBezierAtHalf()
        {
            return new BezierTweenState
            {
                X1 = 0f, Y1 = 0f, X2 = 1f, Y2 = 1f, DurationSec = 1f, ElapsedSec = 0.5f,
                Opacity = new BezierTweenChannel(0f, Slope),
                TranslateX = new BezierTweenChannel(0f, Slope),
                TranslateY = new BezierTweenChannel(0f, Slope),
                Scale = new BezierTweenChannel(0f, Slope),
                Rotate = new BezierTweenChannel(0f, Slope),
                Lengths = new() { new BezierLengthChannel(ArbitraryProperty.Width, 0f, Slope, LengthUnit.Pixel) },
            };
        }

        private static MotionSpringState SpringOverEveryChannel()
        {
            var plan = new MotionSpringClassParser.SpringPlan
            {
                Opacity = (0f, Slope), TranslateX = (0f, Slope), TranslateY = (0f, Slope), Scale = (0f, Slope),
                Rotate = (0f, Slope),
                Lengths = new() { new MotionSpringClassParser.LengthChannelPlan(ArbitraryProperty.Width, 0f, Slope, LengthUnit.Pixel) },
            };
            return MotionSpringDriver.Create(plan, 100f, 10f, 1f)!;
        }

        // The velocity the spring's channel was released with: a channel that is not accelerated carries it unscaled.
        private static float StartVelocity(MotionSpringState state, string channel) => channel switch
        {
            "TranslateX" => state.TranslateX!.StartVelocity,
            "TranslateY" => state.TranslateY!.StartVelocity,
            "Scale" => state.Scale!.StartVelocity,
            "Rotate" => state.Rotate!.StartVelocity,
            _ => state.Lengths![0].Value.StartVelocity,
        };

        [TestCase("TranslateX")]
        [TestCase("TranslateY")]
        [TestCase("Scale")]
        [TestCase("Rotate")]
        [TestCase("Width")]
        public void Given_ABezierPlayMovingEveryChannel_When_ASpringInterruptsIt_Then_TheChannelStartsWithTheCurvesSlope(string channel)
        {
            // Arrange
            var spring = SpringOverEveryChannel();

            // Act
            MotionSpringDriver.ContinueFrom(spring, BezierTweenDriver.Drawn(LinearBezierAtHalf(), 1f), fromDrawn: false);

            // Assert
            Assert.That(StartVelocity(spring, channel), Is.EqualTo(Slope).Within(0.5f));
        }

        [TestCase("TranslateX", ArbitraryProperty.TranslateX)]
        [TestCase("TranslateY", ArbitraryProperty.TranslateY)]
        [TestCase("Scale", ArbitraryProperty.Scale)]
        [TestCase("Rotate", ArbitraryProperty.Rotate)]
        [TestCase("Width", ArbitraryProperty.Width)]
        public void Given_ANativeTransitionMovingOneSlot_When_ASpringInterruptsIt_Then_ThatChannelStartsWithItsRate(
            string channel, ArbitraryProperty slot)
        {
            // Arrange
            var spring = SpringOverEveryChannel();

            // Act
            MotionSpringDriver.InheritVelocity(spring, (asked, unit) => asked == slot && unit == LengthUnit.Pixel ? 7f : null);

            // Assert
            Assert.That(StartVelocity(spring, channel), Is.EqualTo(7f));
        }

        [TestCase(ArbitraryProperty.Width, LengthUnit.Pixel, LengthUnit.Pixel, 10f)]
        [TestCase(ArbitraryProperty.Width, LengthUnit.Percent, LengthUnit.Pixel, null)]
        [TestCase(ArbitraryProperty.TranslateX, LengthUnit.Pixel, LengthUnit.Pixel, 3f)]
        [TestCase(ArbitraryProperty.TranslateY, LengthUnit.Pixel, LengthUnit.Pixel, 4f)]
        [TestCase(ArbitraryProperty.TranslateY, LengthUnit.Percent, LengthUnit.Pixel, null)]
        public void Given_ALengthOrTranslateValue_When_ItsMagnitudeIsRead_Then_ItIsTheAskedAxisInTheAskedUnit(
            ArbitraryProperty slot, LengthUnit valueUnit, LengthUnit asked, float? expected)
        {
            // Arrange — a translate carries 3 along x in pixels and 4 along y in the unit given; a width is 10.
            object value = slot == ArbitraryProperty.Width
                ? new Length(10f, valueUnit)
                : new Translate(new Length(3f, LengthUnit.Pixel), new Length(4f, valueUnit));

            // Act
            var magnitude = MotionSlotContext.NativeMagnitude(value, slot, asked);

            // Assert
            Assert.That(magnitude, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABezierPlayAtItsEnd_When_ItsVelocityIsRead_Then_ItHasStopped()
        {
            // Arrange
            var state = LinearBezierAtHalf();
            state.ElapsedSec = 1f;

            // Act
            var velocity = BezierTweenDriver.VelocityOf(state, state.Opacity);

            // Assert
            Assert.That(velocity, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ABezierPlayAtItsStart_When_ItsVelocityIsRead_Then_ItsTimeIsWhereItWas()
        {
            // Arrange
            var state = LinearBezierAtHalf();
            state.ElapsedSec = 0f;

            // Act
            BezierTweenDriver.VelocityOf(state, state.Opacity);

            // Assert
            Assert.That(state.ElapsedSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ATransitionAtTheEndOfItsDelay_When_ItsRateIsRead_Then_ItIsTheForwardSlope()
        {
            // Arrange — a linear run of 10 units over 2 s, read at the instant it starts.

            // Act
            var rate = NativeTransitionVelocity.Rate(1.0, 1.0, 2f, t => t, 0f, 10f);

            // Assert
            Assert.That(rate, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void Given_AnEaseInTransitionAQuarterThrough_When_ItsRateIsRead_Then_ItIsTheCurvesSlopeThere()
        {
            // Arrange — t squared has slope 0.5 a quarter through, so 10 units over 2 s move at 10 * 0.5 / 2.

            // Act
            var rate = NativeTransitionVelocity.Rate(1.5, 1.0, 2f, t => t * t, 0f, 10f);

            // Assert
            Assert.That(rate, Is.EqualTo(2.5f).Within(0.01f));
        }
    }
}
