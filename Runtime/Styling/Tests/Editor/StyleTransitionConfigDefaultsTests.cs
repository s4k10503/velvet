using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>Pins the duration a <see cref="StyleTransitionConfig"/> naming none plays for.</summary>
    [TestFixture]
    internal sealed class StyleTransitionConfigDefaultsTests
    {
        [Test]
        public void Given_ABezierConfigNamingNoDuration_When_ItsDurationIsRead_Then_ItIsFramersThreeTenthsOfASecond()
        {
            // Arrange
            var config = new StyleTransitionConfig { Type = TransitionType.Bezier };

            // Act
            var duration = config.DurationSec;

            // Assert
            Assert.That(duration, Is.EqualTo(0.3f));
        }
    }
}
