using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which classes declare a backdrop-filter (<see cref="StyleBackdropClass.TryExtract"/>).
    /// </summary>
    internal sealed class BackdropClassExtractTests
    {
        private static bool Declares(string cls) => StyleBackdropClass.TryExtract(new[] { cls }, out _);

        // GREEN_ON_BASE(characterization): the base declares nothing for a negated blur, which Tailwind never generates.
        [Test]
        public void Given_ANegatedBackdropFunctionOtherThanHueRotate_When_Extracted_Then_NothingIsDeclared()
        {
            // Act — the control is the one function Tailwind does negate.
            var declared = Declares("-backdrop-hue-rotate-15") ? Declares("-backdrop-blur-sm") : true;

            // Assert
            Assert.That(declared, Is.False);
        }
    }
}
