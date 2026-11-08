using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the transition a <c>To</c> step naming none holds for: Framer Motion's sequence default of 0.3 s on an
    /// ease-out, whatever an earlier step named.
    /// </summary>
    [TestFixture]
    internal sealed class SequenceDefaultTransitionTests
    {
        [Test]
        public void Given_AToStepNamingNoTransition_When_AQuarterSecondPasses_Then_ItIsStillHeld()
        {
            // Arrange
            var walker = new SequenceWalker();
            walker.Reset(new[] { AnimationSequenceStep.To("a"), AnimationSequenceStep.To("b") });

            // Act
            var index = walker.Advance(0.25f);

            // Assert
            Assert.That(index, Is.EqualTo(0));
        }

        [Test]
        public void Given_AToStepNamingNoTransitionAfterALongerOne_When_ItsOwnThreeTenthsHavePassed_Then_TheCursorHasMovedOn()
        {
            // Arrange
            var walker = new SequenceWalker();
            walker.Reset(new[]
            {
                AnimationSequenceStep.To("a", new StyleTransitionConfig { DurationSec = 1f }),
                AnimationSequenceStep.To("b"),
                AnimationSequenceStep.To("c"),
            });

            // Act
            var indices = new[] { walker.Advance(1.05f), walker.Advance(0.32f) };

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 1, 2 }));
        }
    }
}
