using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins validation of user-supplied spring parameters, mirroring the tween path's
    /// duration guard: a spring that can never satisfy its settle predicate (zero or negative
    /// stiffness never approaches the target; NaN diverges into the styles it writes; zero damping
    /// never drops below rest speed) must warn and complete immediately instead of scheduling a
    /// 16ms panel tick that runs forever and a completion callback that never fires — on a
    /// presence exit that callback is the only thing that ever removes the ghost.
    /// </summary>
    [TestFixture]
    internal sealed class SpringConfigValidationTests
    {
        [Test]
        public void Given_AZeroStiffnessSpring_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Ss]pring"));

            // Act — stiffness 0 can never move the value toward its target.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Spring, stiffness: 0f, damping: 10f, mass: 1f);

            // Assert — the play degrades to an immediate completion instead of a forever-tick.
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ANaNSpringParameter_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Ss]pring"));

            // Act — NaN stiffness would propagate NaN into every style write.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Spring, stiffness: float.NaN, damping: 10f, mass: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }
    }
}
