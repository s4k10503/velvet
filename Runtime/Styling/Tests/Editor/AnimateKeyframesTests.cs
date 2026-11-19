using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the registry behind <c>animate-&lt;name&gt;</c> and <c>animate-[&lt;name&gt;_&lt;time&gt;_&lt;timing&gt;_infinite]</c>:
    /// what registers, which class tokens claim a name and what a track holds at a loop position.
    /// </summary>
    [TestFixture]
    internal sealed class AnimateKeyframesTests
    {
        private static readonly BezierCurve Linear = new(0f, 0f, 1f, 1f);

        private static KeyframeStop[] HalfwayRotation()
            => new[] { new KeyframeStop(0.5f) { RotateDeg = 10f } };

        [Test]
        public void Given_ARegisteredName_When_TheBracketFormNamesItWithATimeATimingAndInfinite_Then_ItIsAClaimedLoopOfThatDuration()
        {
            // Arrange
            VelvetKeyframes.Register("kf-bracket", HalfwayRotation());
            try
            {
                // Act
                var claimed = StyleAnimateClass.TryExtract(new[] { "animate-[kf-bracket_2s_linear_infinite]" }, out var spec);

                // Assert
                Assert.That((claimed, spec.Mode, spec.DurationSec), Is.EqualTo((true, AnimateMode.Custom, 2f)));
            }
            finally
            {
                VelvetKeyframes.Unregister("kf-bracket");
            }
        }

        [Test]
        public void Given_ARegisteredName_When_TheBracketFormLeavesOutInfinite_Then_ItIsNotClaimed()
        {
            // Arrange
            VelvetKeyframes.Register("kf-once", HalfwayRotation());
            try
            {
                // Act
                var claimed = StyleAnimateClass.TryExtract(new[] { "animate-[kf-once_2s_linear]" }, out _);

                // Assert
                Assert.That(claimed, Is.False);
            }
            finally
            {
                VelvetKeyframes.Unregister("kf-once");
            }
        }

        [Test]
        public void Given_ARegisteredName_When_UsedAsAPlainClassWithATimeOverride_Then_ItLoopsForThatTime()
        {
            // Arrange
            VelvetKeyframes.Register("kf-plain", HalfwayRotation(), durationSec: 1.5f);
            try
            {
                // Act
                var claimed = StyleAnimateClass.TryExtract(new[] { "animate-kf-plain", "animate-kf-plain-[400ms]" }, out var spec);

                // Assert
                Assert.That(claimed ? spec.DurationSec : float.NaN, Is.EqualTo(0.4f).Within(1e-5f));
            }
            finally
            {
                VelvetKeyframes.Unregister("kf-plain");
            }
        }

        [Test]
        public void Given_ATrackNamingOnlyTheMiddle_When_SampledAtAQuarter_Then_ItIsHalfwayFromTheImplicitStart()
        {
            // Arrange
            var track = KeyframeTrack.Of(HalfwayRotation(), static s => s.RotateDeg)!;

            // Act
            var value = track.Eval(0.25f, 0f, 0f, Linear);

            // Assert
            Assert.That(value, Is.EqualTo(5f).Within(1e-4f));
        }

        [TestCase("")]
        [TestCase("pulse")]
        [TestCase("a_b")]
        public void Given_ANameAClassCannotCarry_When_Registered_Then_ItIsRefused(string name)
        {
            // Act
            TestDelegate register = () => VelvetKeyframes.Register(name, HalfwayRotation());

            // Assert
            Assert.Throws<System.ArgumentException>(register);
        }
    }
}
