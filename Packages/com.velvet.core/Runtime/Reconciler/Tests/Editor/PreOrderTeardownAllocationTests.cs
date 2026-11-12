using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a warm <see cref="PreOrderTeardown"/> places and takes down a thousand fibers, each at a row of its
    /// own, without allocating: the shape of a keyed list of a thousand component rows a render clears or replaces.
    /// </summary>
    [TestFixture]
    internal sealed class PreOrderTeardownAllocationTests
    {
        private const int Rows = 1000;

        private sealed class PlacingWalk : PreOrderTeardown
        {
            private readonly ReconcilerContext _context;

            internal PlacingWalk(ReconcilerContext context) => _context = context;

            internal void PlaceAndTakeDown(List<VisualElement> rows, List<ComponentFiber> fibers)
            {
                Ctx = _context;
                for (var i = 0; i < rows.Count; i++) PlaceAt(rows[i], fibers[i]);
                for (var i = 0; i < rows.Count; i++) TakeDownStartingAt(rows[i]);
                Clear();
            }

            protected override void TakeDown(ComponentFiber fiber)
            {
            }

            protected override void TakeDown(VisualElement element)
            {
            }

            protected override bool IsRoot(VisualElement element) => false;
        }

        [Test]
        public void Given_AWarmWalk_When_ItPlacesAndTakesDownAThousandFibersEachAtARowOfItsOwn_Then_NothingIsAllocated()
        {
            // Arrange
            var walk = new PlacingWalk(new ReconcilerContext());
            var rows = new List<VisualElement>(Rows);
            var fibers = new List<ComponentFiber>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(new VisualElement());
                fibers.Add(new ComponentFiber());
            }
            Action placeAndTakeDown = () => walk.PlaceAndTakeDown(rows, fibers);
            Action canary = () => GC.KeepAlive(new object());
            placeAndTakeDown();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(placeAndTakeDown);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }
    }
}
