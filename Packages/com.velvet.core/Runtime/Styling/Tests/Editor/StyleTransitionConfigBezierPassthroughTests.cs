using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a custom bezier curve survives the config's copy builders unchanged. <see cref="StyleTransitionConfig.With"/>
    /// and <see cref="StyleTransitionConfig.WithExitClasses"/> each rebuild the config through an object
    /// initializer that must copy every knob; forgetting the four <c>Bezier*</c> fields there would silently reset
    /// a caller's curve back to the default on every <c>.With(...)</c> — the highest-value regression pin against
    /// that copy-paste-and-forget-one-field bug class.
    /// </summary>
    [TestFixture]
    internal sealed class StyleTransitionConfigBezierPassthroughTests
    {
        // A deliberately non-default curve, so a reset back to the (0.4,0,0.2,1) default would be visible.
        private static StyleTransitionConfig CustomBezier() => new()
        {
            Type = TransitionType.Bezier,
            DurationSec = 0.3f,
            BezierX1 = 0.11f,
            BezierY1 = 0.22f,
            BezierX2 = 0.33f,
            BezierY2 = 0.44f,
        };

        [Test]
        public void Given_ACustomBezierConfig_When_WithIsCalled_Then_TheCurveSurvivesUnchanged()
        {
            // Arrange
            var config = CustomBezier();

            // Act — With() only tunes the top-level timing; the curve must pass through untouched.
            var result = config.With(durationSec: 0.5f);

            // Assert
            Assert.That(
                (result.BezierX1, result.BezierY1, result.BezierX2, result.BezierY2),
                Is.EqualTo((0.11f, 0.22f, 0.33f, 0.44f)));
        }

        [Test]
        public void Given_ACustomBezierConfig_When_WithExitClassesIsCalled_Then_TheCurveSurvivesUnchanged()
        {
            // Arrange
            var config = CustomBezier();

            // Act — WithExitClasses() replaces only the exit class pair; the curve must pass through untouched.
            var result = config.WithExitClasses("opacity-100", "opacity-0");

            // Assert
            Assert.That(
                (result.BezierX1, result.BezierY1, result.BezierX2, result.BezierY2),
                Is.EqualTo((0.11f, 0.22f, 0.33f, 0.44f)));
        }
    }
}
