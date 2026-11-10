using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class ColorCarryTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AnElementTransitioningItsColoursOverASecondOnEaseOut_When_ACarryHasRunAQuarterOfIt_Then_ItStandsWhereTailwindsEaseOutDoes()
        {
            // Arrange
            var element = MountAndResolve("transition-colors duration-1000 ease-out");
            var carry = new ColorCarry();
            carry.Land(Color.white);
            carry.Step(Color.black, 0f, element, "background-color", null);
            var expected = 1f - StyleEaseCurve.Evaluate(new BezierCurve(0f, 0f, 0.2f, 1f), 0.25f);

            // Act
            carry.Step(Color.black, 0.25f, element, "background-color", null);

            // Assert
            Assert.That(carry.Value.r, Is.EqualTo(expected).Within(1e-3f));
        }
    }
}
