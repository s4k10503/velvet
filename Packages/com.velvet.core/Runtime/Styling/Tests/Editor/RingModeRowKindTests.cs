using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the row model of an element's child list says about a ring overlay once its ring changes between
    /// the inset role (a first child) and the sibling role: the model follows the class the overlay carries now.
    /// </summary>
    internal sealed class RingModeRowKindTests
    {
        private static readonly RingBand Band = new(2f, 0f, Color.red, currentColor: false);
        private static readonly RingSpec Inset = new(hasRing: true, Band, fill: default, inset: true, outline: default);
        private static readonly RingSpec Sibling = new(hasRing: true, Band, fill: default, inset: false, outline: default);

        private VisualElement _element;
        private RingBinding _binding;

        [TearDown]
        public void TearDown()
        {
            if (_binding != null)
            {
                RingOverlay.Detach(_element, _binding);
            }
        }

        // GREEN_ON_BASE(characterization): the base counts an overlay that stopped being inset as no leading child.
        [Test]
        public void Given_AnInsetRingOnAnElementWithNoParent_When_ItIsSyncedToASiblingBand_Then_AFirstChildGoesBeforeTheOverlayLeftInside()
        {
            // Arrange — with no parent to host the band beside, the overlay stays where the inset role put it.
            _element = new VisualElement();
            _binding = RingOverlay.Attach(_element, Inset, Array.Empty<string>());

            // Act
            RingOverlay.Sync(_element, _binding, Sibling, Array.Empty<string>());

            // Assert — read as -1 where the overlay is not the one child, which the slot's answer is never.
            Assert.That(_element.childCount == 1 ? LogicalChildSlots.ToPhysical(_element, 0) : -1, Is.EqualTo(0));
        }
    }
}
