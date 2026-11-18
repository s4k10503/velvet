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

        [Test]
        public void Given_AnInsetRingOnAnElementWithNoParent_When_ItIsSyncedToASiblingBand_Then_ItsRowIsOneHostedAfterItsRow()
        {
            // Arrange — with no parent to host the band beside, the overlay stays where the inset role put it.
            _element = new VisualElement();
            _binding = RingOverlay.Attach(_element, Inset, Array.Empty<string>());

            // Act
            RingOverlay.Sync(_element, _binding, Sibling, Array.Empty<string>());

            // Assert — a missing model reads as null, which no kind equals.
            Assert.That(RowModelAccess.Peek(_element)?.KindAt(0), Is.EqualTo(RowKind.Hosted));
        }
    }
}
