using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins validation of user-supplied bezier parameters, mirroring the spring path's guard: a non-finite
    /// control point would propagate into every inline style write and never reach the target, so the tick this
    /// drives would run forever and its completion callback — the only thing that removes a presence exit's ghost
    /// — would never fire. A negative / out-of-range duration is likewise a misconfiguration. Both must warn and
    /// complete immediately. A ZERO duration, by contrast, is an intentional "no animation" (like
    /// <c>StyleTransitionConfig.None</c>) and must complete silently — NUnit's strict LogAssert mode fails on any
    /// unexpected log, so the absence of a <c>LogAssert.Expect</c> in that case also pins "no warning".
    /// </summary>
    [TestFixture]
    internal sealed class BezierConfigValidationTests
    {
        [Test]
        public void Given_ANaNBezierControlPoint_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act — a NaN control point would propagate NaN into every style write.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0.3f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: float.NaN, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert — the play degrades to an immediate completion instead of a forever-tick.
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ANegativeBezierDuration_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act — a negative duration is out of the accepted range (unlike a zero, which is intentional).
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                -1f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: 0.4f, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_AZeroBezierDuration_When_AVariantEnterPlays_Then_ItCompletesImmediatelyWithoutWarning()
        {
            // Arrange — no LogAssert.Expect: a zero duration is an intentional no-animation, so any warning here
            // would fail the test under NUnit's strict LogAssert mode.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;

            // Act — a zero duration degrades exactly like StyleTransitionConfig.None: silent immediate complete.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: 0.4f, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }
    }
}
