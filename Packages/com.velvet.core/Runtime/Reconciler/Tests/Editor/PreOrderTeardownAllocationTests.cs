using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a warm <see cref="PreOrderTeardown"/> places and takes down a thousand fibers, each at a row of its
    /// own, without allocating: the shape of a keyed list of a thousand component rows a render clears or replaces,
    /// and that a warm <see cref="DeletionPlan"/> rented for such a deletion allocates nothing either.
    /// </summary>
    [TestFixture]
    internal sealed class PreOrderTeardownAllocationTests
    {
        private const int Rows = 1000;

        private sealed class PlacingWalk : PreOrderTeardown
        {
            private readonly ReconcilerContext _context;
            private readonly bool _plans;

            internal PlacingWalk(ReconcilerContext context, bool plans)
            {
                _context = context;
                _plans = plans;
            }

            protected override bool PlansTakeDowns => _plans;

            internal void PlaceAndTakeDown(List<VisualElement> rows, List<ComponentFiber> fibers)
            {
                Ctx = _context;
                for (var i = 0; i < rows.Count; i++) PlaceAt(rows[i], fibers[i]);
                for (var i = 0; i < rows.Count; i++) TakeDownStartingAt(rows[i]);
                RunPlan();
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

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AWarmWalk_When_ItPlacesAndTakesDownAThousandFibersEachAtARowOfItsOwn_Then_NothingIsAllocated(bool plans)
        {
            // Arrange
            var walk = new PlacingWalk(new ReconcilerContext(), plans);
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

        [Test]
        public void Given_AWarmPlanPool_When_APlanIsRentedFilledAndReleasedForAThousandRows_Then_NothingIsAllocated()
        {
            // Arrange
            var rows = new List<VisualElement>(Rows);
            for (var i = 0; i < Rows; i++) rows.Add(new VisualElement());
            Action rentFillAndRelease = () =>
            {
                var plan = DeletionPlan.Rent();
                for (var i = 0; i < rows.Count; i++)
                {
                    plan.Roots[rows[i]] = new DeletionPlan.PlannedRoot(null, rows[i]);
                    plan.Steps.Add(new PreOrderTeardown.Step { Element = rows[i] });
                }
                plan.Release();
            };
            Action canary = () => GC.KeepAlive(new object());
            rentFillAndRelease();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(rentFillAndRelease);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }
    }
}
