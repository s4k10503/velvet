using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a classic enter on a spring or bezier config still reports parameters that config could not
    /// play with, though the enter itself moves nothing.
    /// </summary>
    [TestFixture]
    internal sealed class ClassicEnterParameterWarningTests
    {
        // GREEN_ON_BASE(characterization): the base's classic spring enter started a spring, which validated these.
        // So an invalid stiffness is still reported now that the enter only completes.
        [Test]
        public void Given_ASpringConfigWithZeroStiffness_When_AClassicEnterPlaysOnIt_Then_ItWarns()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            LogAssert.Expect(LogType.Warning, new Regex("Invalid spring parameters"));

            // Act
            scheduler.PlayEnter(new VisualElement(),
                new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 0f });

            // Assert — LogAssert.Expect verifies the warning was logged
        }

        // GREEN_ON_BASE(characterization): the base's classic bezier enter started a bezier, which validated these.
        // So a negative duration is still reported now that the enter only completes.
        [Test]
        public void Given_ABezierConfigWithANegativeDuration_When_AClassicEnterPlaysOnIt_Then_ItWarns()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            LogAssert.Expect(LogType.Warning, new Regex("Invalid bezier parameters"));

            // Act
            scheduler.PlayEnter(new VisualElement(),
                new StyleTransitionConfig { Type = TransitionType.Bezier, DurationSec = -1f });

            // Assert — LogAssert.Expect verifies the warning was logged
        }
    }
}
