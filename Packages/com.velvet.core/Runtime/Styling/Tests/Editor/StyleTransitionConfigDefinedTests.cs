using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which knobs make a transition one the caller defined, Framer Motion's isTransitionDefined, and
    /// when a tween's exit counts as animated.
    /// </summary>
    [TestFixture]
    internal sealed class StyleTransitionConfigDefinedTests
    {
        // GREEN_ON_BASE(characterization): the base treats a config naming a stiffness as defined.
        [Test]
        public void Given_AConfigNamingOnlyAStiffness_When_PlayedOnAVariant_Then_ItRunsOnItsOwnKnobs()
        {
            // Arrange
            var config = new StyleTransitionConfig { Stiffness = 200f };

            // Act
            var played = config.PlayedOnVariant;

            // Assert
            Assert.That(played, Is.SameAs(config));
        }

        // GREEN_ON_BASE(characterization): the base has an exit animate where a per-property override animates.
        [Test]
        public void Given_AZeroDurationTweenWithAnAnimatingOverride_When_AskedWhetherItsExitAnimates_Then_ItDoes()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                DurationSec = 0f,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.2f) },
            };

            // Act
            var animates = config.HasExitAnimation;

            // Assert
            Assert.That(animates, Is.True);
        }
    }
}
