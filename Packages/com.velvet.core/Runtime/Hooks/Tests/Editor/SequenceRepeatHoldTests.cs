using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the hold a <see cref="AnimationSequenceStep.To"/> step derives from a <see cref="TransitionType.Bezier"/>
    /// transition that repeats: the walker stays on the step for every pass the label's play makes and the waits
    /// between them, and for one pass of an endless repeat. Drives <see cref="SequenceWalker"/> directly.
    /// </summary>
    [TestFixture]
    internal sealed class SequenceRepeatHoldTests
    {
        private static int[] StepIndexAfter(StyleTransitionConfig transition, params float[] advances)
        {
            var walker = new SequenceWalker();
            walker.Reset(new[]
            {
                AnimationSequenceStep.To("up", transition),
                AnimationSequenceStep.To("down"),
            });
            var indices = new int[advances.Length];
            for (var i = 0; i < advances.Length; i++)
            {
                indices[i] = walker.Advance(advances[i]);
            }
            return indices;
        }

        [Test]
        public void Given_AToStepOnARepeatingBezier_When_ThePlayIsAboutToEndAndThenEnds_Then_TheCursorMovesOnOnlyAtTheEnd()
        {
            // Arrange — three half-second passes with a quarter-second wait between each: 2 s in all.
            var transition = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = 2f, RepeatDelaySec = 0.25f,
            };

            // Act
            var indices = StepIndexAfter(transition, 1.9f, 0.2f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_AToStepOnAnEndlesslyRepeatingBezier_When_OnePassElapses_Then_TheCursorMovesOn()
        {
            // Arrange
            var transition = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = float.PositiveInfinity,
            };

            // Act
            var indices = StepIndexAfter(transition, 0.4f, 0.2f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }
    }
}
