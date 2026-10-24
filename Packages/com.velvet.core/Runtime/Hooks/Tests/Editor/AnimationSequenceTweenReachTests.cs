using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>AnimationSequenceControls.Pause</c>, <c>Speed</c> and <c>Cancel</c> reach the Tween play a
    /// step's label started, on the default clock, as <see cref="AnimationSequencePauseReachTests"/> and
    /// <see cref="AnimationSequenceSpeedCancelTests"/> pin them for a Bezier play.
    /// </summary>
    internal sealed class AnimationSequenceTweenReachTests : AnimationSequenceMotionTestsBase
    {
        private static readonly StyleTransitionConfig s_fade = new() { DurationSec = 1f, Easing = EasingMode.Linear };

        private void MountOnATweenFade()
        {
            Steps = new[] { AnimationSequenceStep.To("hidden", Instant), AnimationSequenceStep.To("visible", s_fade) };
            MountCoordinator();
            PlayIntoTheFade();
        }

        [Test]
        public void Given_ASequencePartWayThroughATweenFade_When_PausedForFiveFrames_Then_TheMotionsOpacityHolds()
        {
            // Arrange
            MountOnATweenFade();
            var moving = Opacity().value;

            // Act
            Controls.Pause();
            Frames(5);

            // Assert
            Assert.That(moving > 0f ? Opacity().value : float.NaN, Is.EqualTo(moving));
        }

        [Test]
        public void Given_ASequencePartWayThroughATweenFade_When_ItsSpeedIsHalvedForFourFrames_Then_TheMotionMovesByTwoFramesWorth()
        {
            // Arrange
            MountOnATweenFade();
            var moving = Opacity().value;

            // Act
            Controls.Speed = 0.5f;
            Frames(4);

            // Assert
            Assert.That(moving > 0f ? Opacity().value - moving : float.NaN, Is.EqualTo(0.032f).Within(1e-4f));
        }

        [Test]
        public void Given_ASequencePartWayThroughATweenFade_When_Cancelled_Then_TheMotionHoldsTheOpacityTheFadeStartedFrom()
        {
            // Arrange
            MountOnATweenFade();
            var moving = Opacity().value;

            // Act
            Controls.Cancel();
            Frames(2);

            // Assert
            var opacity = Opacity();
            Assert.That((moving > 0f, opacity.keyword, opacity.value), Is.EqualTo((true, StyleKeyword.Undefined, 0f)));
        }
    }
}
