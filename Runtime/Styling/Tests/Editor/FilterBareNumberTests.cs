using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind v4's bare value for the amount filters: any whole number, a percentage of the function's
    /// amount (degrees for hue-rotate), clamped to 1 where CSS clamps the function.
    /// </summary>
    internal sealed class FilterBareNumberTests
    {
        [Test]
        public void Given_FilterUtilitiesOffTheOldPresetScale_When_Parsed_Then_EachReadsItsNumberAsAPercentage()
        {
            // Arrange
            var tokens = new[]
            {
                "brightness-80", "contrast-33", "saturate-7", "grayscale-50", "invert-250", "sepia-5", "-hue-rotate-45",
            };

            // Act
            var read = string.Join(" ", tokens.Select(token => StyleArbitraryValueResolver.TryParse(token, out var style)
                ? style.Property + "=" + style.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "declined"));

            // Assert
            Assert.That(read, Is.EqualTo("FilterBrightness=0.8 FilterContrast=0.33 FilterSaturate=0.07 FilterGrayscale=0.5 "
                + "FilterInvert=1 FilterSepia=0.05 FilterHueRotate=-45"));
        }
    }
}
