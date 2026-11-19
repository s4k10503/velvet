using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a spring exit given its own physics (<see cref="StyleTransitionConfig.ExitSpring"/>) plays on
    /// them rather than ending without a play.
    /// </summary>
    [TestFixture]
    internal sealed class SpringExitPhysicsTests
    {
        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        // GREEN_ON_BASE(characterization): the base plays a spring exit that names its own physics.
        [Test]
        public void Given_ASpringExitWithItsOwnPhysics_When_Played_Then_TheElementIsExiting()
        {
            // Arrange
            var element = new VisualElement();
            element.AddToClassList("opacity-100");
            _sim.rootVisualElement.Add(element);
            var scheduler = new StyleAnimationScheduler();
            var exit = new StyleTransitionConfig { Type = TransitionType.Spring, ExitSpring = new SpringPhysics(300f, 20f, 2f) }
                .WithExitClasses("opacity-100", "opacity-0");

            // Act
            scheduler.PlayExit(element, exit, null, restoreFromOnCancel: true);

            // Assert
            Assert.That(scheduler.IsExiting(element), Is.True);
        }
    }
}
