using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how a <see cref="AnimationSequenceStep.To"/> step treats a transition that repeats, as Framer Motion's
    /// sequence treats a segment's repeat: a repeat below 20 lengthens the hold it derives by every pass and the
    /// waits between them, and one of 20 or more, an endless one included, is dropped from the transition the step
    /// hands out, which then plays and holds once. Drives <see cref="SequenceWalker"/> directly.
    /// </summary>
    [TestFixture]
    internal sealed class SequenceRepeatHoldTests
    {
        private static SequenceWalker Walk(StyleTransitionConfig transition)
        {
            var walker = new SequenceWalker();
            walker.Reset(new[]
            {
                AnimationSequenceStep.To("up", transition),
                AnimationSequenceStep.To("down"),
            });
            return walker;
        }

        private static int[] StepIndexAfter(StyleTransitionConfig transition, params float[] advances)
        {
            var walker = Walk(transition);
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
            // Arrange — a 0.1 s delay, then three half-second passes with a quarter-second wait between each: 2.1 s.
            var transition = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, DelaySec = 0.1f, Repeat = 2f, RepeatDelaySec = 0.25f,
            };

            // Act
            var indices = StepIndexAfter(transition, 2f, 0.2f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_AToStepOnARepeatingSpring_When_ThePlayIsAboutToEndAndThenEnds_Then_TheCursorMovesOnOnlyAtTheEnd()
        {
            // Arrange — the sequence's spring travels 100 and rests at 1.05 s on Framer's default spring: two passes.
            var transition = new StyleTransitionConfig { Type = TransitionType.Spring, Repeat = 1f };

            // Act
            var indices = StepIndexAfter(transition, 2f, 0.2f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_AToStepRepeating19Times_When_ItsTwentyPassesAreAboutToEndAndThenEnd_Then_TheCursorMovesOnOnlyAtTheEnd()
        {
            // Arrange — the largest repeat a sequence step plays: twenty half-second passes.
            var transition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = 19f };

            // Act
            var indices = StepIndexAfter(transition, 9.9f, 0.2f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Given_AToStepOnAnOpacitySpringRepeating19Times_When_ThePlayIsAboutToEndAndThenEnds_Then_TheCursorMovesOnWithIt()
        {
            // Arrange — the step's label plays the opacity's spring twenty times; the hold has to end with that play.
            var transition = new StyleTransitionConfig { Type = TransitionType.Spring, Repeat = 19f };
            var play = (float)MotionPlaySpan.Of(transition, new[] { "opacity-0" }, new[] { "opacity-100" });

            // Act
            var indices = StepIndexAfter(transition, play - 0.05f, 0.1f);

            // Assert
            Assert.That(indices, Is.EqualTo(new[] { 0, 1 }));
        }

        [TestCase(20f)]
        [TestCase(float.PositiveInfinity)]
        public void Given_AToStepRepeating20TimesOrMore_When_OnePassElapses_Then_ItHandedOutNoRepeatAndMovesOn(float repeat)
        {
            // Arrange
            var transition = new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = repeat };
            var walker = Walk(transition);
            var handedOut = walker.ToState().CurrentTransition?.Repeat ?? float.NaN;

            // Act
            var index = walker.Advance(0.6f);

            // Assert
            Assert.That((handedOut, index), Is.EqualTo((0f, 1)));
        }
    }
}
