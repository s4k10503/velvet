using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a spring play's vertical translate keeps its unit in what the play reports: in the values it
    /// draws, which the next play continues from, and in the start values a held element's next play starts from.
    /// </summary>
    [TestFixture]
    internal sealed class MotionSpringTranslateUnitTests
    {
        // A horizontal slide over an element resting at half its height down, so the vertical axis rests in percent.
        private static MotionSpringState SlideOverAPercentRest()
            => MotionSpringDriver.Create(
                MotionSpringClassParser.ResolveResting(new[] { "translate-x-0" }, new[] { "translate-x-4" },
                    new Translate(new Length(0f), new Length(50f, LengthUnit.Percent))),
                100f, 20f, 1f)!;

        // GREEN_ON_BASE(characterization): the base draws a spring's vertical translate in its rest's unit.
        [Test]
        public void Given_ASpringOverAPercentVerticalRest_When_ItsDrawnValuesAreRead_Then_TheVerticalUnitIsPercent()
        {
            // Arrange
            var state = SlideOverAPercentRest();

            // Act
            var drawn = MotionSpringDriver.Drawn(state);

            // Assert
            Assert.That(drawn.TranslateYUnit, Is.EqualTo(LengthUnit.Percent));
        }

        // GREEN_ON_BASE(characterization): the base starts a spring's vertical translate in its rest's unit.
        [Test]
        public void Given_ASpringOverAPercentVerticalRest_When_ItsStartValuesAreRead_Then_TheVerticalUnitIsPercent()
        {
            // Arrange
            var state = SlideOverAPercentRest();

            // Act
            var plan = MotionSpringDriver.StartValues(state);

            // Assert
            Assert.That(plan.TranslateYUnit, Is.EqualTo(LengthUnit.Percent));
        }
    }
}
