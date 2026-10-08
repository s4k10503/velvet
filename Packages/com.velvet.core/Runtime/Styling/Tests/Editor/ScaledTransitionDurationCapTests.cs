using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>StyleAnimationScheduler</c> holds a transition <c>StyleTransitionConfig.ScaledBy</c> slowed
    /// to its duration cap at the authored length: a three-second transition played at a quarter of its speed
    /// runs twelve seconds, past the cap at face, and each play below still starts instead of completing at
    /// once.
    /// </summary>
    internal sealed class ScaledTransitionDurationCapTests : PanelTestBase
    {
        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            base.TearDown();
        }

        private VisualElement MountOnRealPanel()
        {
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            return element;
        }

        // The preset carries the classes a tween exit and a preset enter move; the bezier is given an opacity
        // pair to drive, since a bezier play with no channel to animate completes at once whatever its duration.
        private static StyleTransitionConfig QuarterSpeed(TransitionType type)
            => (type == TransitionType.Tween
                ? StyleTransition.Fade.With(durationSec: 3f)
                : new StyleTransitionConfig
                {
                    Type = type, DurationSec = 3f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
                }).ScaledBy(0.25f);

        [TestCase(TransitionType.Tween)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AThreeSecondTransitionAtAQuarterOfItsSpeed_When_AVariantEnterStarts_Then_ItHasNotCompleted(
            TransitionType type)
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, QuarterSpeed(type),
                onComplete: () => completed = true);

            // Assert
            Assert.That(completed, Is.False);
        }

        [TestCase(TransitionType.Tween)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AThreeSecondTransitionAtAQuarterOfItsSpeed_When_AnExitStarts_Then_ItHasNotCompleted(
            TransitionType type)
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayExit(element, QuarterSpeed(type), () => completed = true);

            // Assert
            Assert.That(completed, Is.False);
        }

        [Test]
        public void Given_AThreeSecondPresetAtAQuarterOfItsSpeed_When_APresetEnterStarts_Then_ItHasNotCompleted()
        {
            // Arrange
            var element = MountOnRealPanel();
            var scheduler = _scheduler = new StyleAnimationScheduler();
            var completed = false;

            // Act
            scheduler.PlayEnter(element, QuarterSpeed(TransitionType.Tween), () => completed = true);

            // Assert
            Assert.That(completed, Is.False);
        }
    }
}
