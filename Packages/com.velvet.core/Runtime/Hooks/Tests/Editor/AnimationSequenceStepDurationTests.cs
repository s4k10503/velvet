using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a <c>Hooks.UseAnimationSequence</c> step moves the <c>V.Motion</c> handed
    /// <c>AnimationSequenceState.CurrentTransition</c> at the rate its own duration gives, after a step whose
    /// near-instant Tween started a play of its own.
    /// </summary>
    internal sealed class AnimationSequenceStepDurationTests : AnimationSequenceMotionTestsBase
    {
        private static StyleTransitionConfig LinearTween(float durationSec)
            => new() { DurationSec = durationSec, Easing = EasingMode.Linear };

        // GREEN_ON_BASE(characterization): the base hands the second step's play the 0.3 s that step names.
        // Narrowing `SequenceWalker.HandedTransition` to copy only `if (_handedTransition == null)` hands the
        // second step the first step's 0.001 s, which lands within a frame, and reddens this case.
        [Test]
        public void Given_ANearInstantTweenStepThenAThreeTenthsTweenStep_When_TwoFramesOfTheSecondRun_Then_TheMotionMovesTwoFramesShareOfThreeTenthsOfASecond()
        {
            // Arrange
            Steps = new[]
            {
                AnimationSequenceStep.To("hidden", LinearTween(0.001f)),
                AnimationSequenceStep.To("visible", LinearTween(0.3f)),
            };
            MountCoordinator();
            PlayIntoTheFade();
            var moving = Opacity().value;

            // Act
            Frames(2);

            // Assert
            Assert.That(moving > 0f ? Opacity().value - moving : float.NaN, Is.EqualTo(2 * 0.016f / 0.3f).Within(1e-4f));
        }
    }
}
