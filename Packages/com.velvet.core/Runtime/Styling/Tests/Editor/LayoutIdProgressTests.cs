using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a <c>layoutId</c> move on a spring ends: as Framer Motion's animation over its spring generator
    /// does, on the sample at which the generator rests, that sample included.
    /// </summary>
    [TestFixture]
    internal sealed class LayoutIdProgressTests
    {
        [Test]
        public void Given_ALayoutIdSpringProgress_When_ExactlyItsPassHasElapsed_Then_ItHasEnded()
        {
            // Arrange — stiffness 100, damping 10, mass 1: Framer's generator first rests over the projection's
            // travel of 1000 on its 1700 ms sample.
            var progress = LayoutIdTiming.From(new StyleTransitionConfig
            {
                Type = TransitionType.Spring, Stiffness = 100f, Damping = 10f, Mass = 1f,
            }).Start();

            // Act
            var ended = progress.Step(1.7f);

            // Assert
            Assert.That(ended, Is.True);
        }
    }
}
