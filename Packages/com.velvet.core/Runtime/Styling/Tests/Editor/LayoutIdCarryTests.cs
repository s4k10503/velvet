using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class LayoutIdCarryTests
    {
        // A carry whose held list runs opacity linearly over a second, which Step reads in place of the cascade.
        private static LayoutIdCarry CarryHoldingASecond()
        {
            var carry = new LayoutIdCarry();
            var lists = new TransitionLists(
                new List<StylePropertyName> { new("opacity") },
                new List<TimeValue> { new(1f, TimeUnit.Second) },
                null,
                new List<EasingFunction> { new(EasingMode.Linear) });
            carry.Hold(lists, true, "opacity", null);
            return carry;
        }

        [Test]
        public void Given_ACarryPartWayThroughATransition_When_LandedOnAValue_Then_ItHasLanded()
        {
            // Arrange
            var carry = CarryHoldingASecond();
            carry.Step(1f, 0.25f, new VisualElement(), "opacity", null);

            // Act
            carry.Land(0.5f);

            // Assert
            Assert.That(carry.Landed, Is.True);
        }

        [Test]
        public void Given_ATakenOverTransitionWhoseCurveMissesItsEnd_When_ItsDurationHasElapsed_Then_ItIsAtItsTarget()
        {
            // Arrange — a curve that stands half way at its end.
            var carry = new LayoutIdCarry();
            carry.TakeOver((0f, 1f, 1f, 0f), t => t * 0.5f, (0f, 1f, 0f));

            // Act
            var value = carry.Step(1f, 1f, new VisualElement(), "opacity", null);

            // Assert
            Assert.That(value, Is.EqualTo(1f));
        }
    }
}
