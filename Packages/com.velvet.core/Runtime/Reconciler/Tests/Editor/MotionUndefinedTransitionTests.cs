using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a variant play runs on where its transition names nothing but a delay, the repeat options or the
    /// child orchestration: Framer counts such a transition as none, so each value takes its default transition.
    /// </summary>
    [TestFixture]
    internal sealed class MotionUndefinedTransitionTests
    {
        private static Dictionary<string, MotionVariant> Poses(StyleTransitionConfig? visible = null) => new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = new MotionVariant("opacity-100", visible),
        };

        [Test]
        public void Given_AMotionWhoseTransitionSetsOnlyAStagger_When_ItsVariantTransitionIsRead_Then_ItIsTheDefaultsCarryingThatStagger()
        {
            // Arrange
            var motion = V.Motion(variants: Poses(), animate: "visible",
                transition: new StyleTransitionConfig { StaggerChildrenSec = 0.1f });

            // Act
            var played = motion.VariantTransition;

            // Assert
            Assert.That(played is { PerValueDefaults: true } ? played.StaggerChildrenSec : float.NaN, Is.EqualTo(0.1f));
        }

        [Test]
        public void Given_AMotionGivenOnlyADelay_When_ItsVariantTransitionIsRead_Then_ItIsTheDefaultsAfterThatDelay()
        {
            // Arrange
            var motion = V.Motion(variants: Poses(), animate: "visible", delay: 0.2f);

            // Act
            var played = motion.VariantTransition;

            // Assert
            Assert.That(played is { PerValueDefaults: true } ? played.DelaySec : float.NaN, Is.EqualTo(0.2f));
        }

        [Test]
        public void Given_APoseWhoseTransitionSetsOnlyARepeat_When_ASwapIntoItResolves_Then_ItPlaysTheDefaultsOnThatRepeat()
        {
            // Arrange
            var motion = V.Motion(variants: Poses(new StyleTransitionConfig { Repeat = 2f }), animate: "visible",
                transition: new StyleTransitionConfig { DurationSec = 0.5f });

            // Act
            MotionVariantResolver.ResolveAppliedAt(motion, "visible", out _, out var played);

            // Assert
            Assert.That(played is { PerValueDefaults: true } ? played.Repeat : float.NaN, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AMotionGivenOnlyAnEasing_When_ItsVariantTransitionIsRead_Then_ItIsATweenOfFramersDefaultDuration()
        {
            // Arrange — Framer counts an ease as a transition, which runs 0.3 s, not the Fade preset's 0.2 s.
            var motion = V.Motion(variants: Poses(), animate: "visible", easing: UnityEngine.UIElements.EasingMode.EaseIn);

            // Act
            var played = motion.VariantTransition;

            // Assert
            Assert.That(played is { PerValueDefaults: false } ? played.DurationSec : float.NaN, Is.EqualTo(0.3f));
        }
    }
}
