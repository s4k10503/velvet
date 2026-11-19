using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// What <see cref="ChildElementPlacement"/> reports as the order the components in the placed children were
    /// created in, and that the row buffer its anchor scan rents goes back to the pool.
    /// </summary>
    [TestFixture]
    internal sealed class ChildElementPlacementCreationOrderTests
    {
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static VisualElement ParentOf(params VisualElement[] children)
        {
            var parent = new VisualElement();
            foreach (var child in children) parent.Add(child);
            return parent;
        }

        [Test]
        public void Given_KeptChildrenInOrderAndACreatedOneAfterThem_When_TheyAreReordered_Then_TheOrderIsNotReportedAsOutOfCreationOrder()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            var parent = ParentOf(first, second);
            var placement = new ChildElementPlacement(new ReconcilerBufferPool(), _reconciler.Context.HostCommit);
            var newElements = new List<(VisualElement? element, bool isExisting)>
            {
                (first, true), (second, true), (new VisualElement(), false),
            };

            // Act
            var outOfCreationOrder = placement.ReorderToNewElementOrder(parent, newElements, new HashSet<int> { 0, 1 }, slotStart: 0);

            // Assert
            Assert.That(outOfCreationOrder, Is.False);
        }

        [Test]
        public void Given_TheSameKeptChildListedTwice_When_TheChildrenAreReordered_Then_TheOrderIsNotReportedAsOutOfCreationOrder()
        {
            // Arrange
            var repeated = new VisualElement();
            var parent = ParentOf(repeated, new VisualElement());
            var placement = new ChildElementPlacement(new ReconcilerBufferPool(), _reconciler.Context.HostCommit);
            var newElements = new List<(VisualElement? element, bool isExisting)> { (repeated, true), (repeated, true) };

            // Act
            var outOfCreationOrder = placement.ReorderToNewElementOrder(parent, newElements, new HashSet<int> { 0 }, slotStart: 0);

            // Assert
            Assert.That(outOfCreationOrder, Is.False);
        }

        [Test]
        public void Given_AnAnchorScan_When_ItFinishes_Then_TheRowBufferItRentedIsBackInThePool()
        {
            // Arrange
            var pool = new ReconcilerBufferPool();
            var rented = pool.RentRowRead();
            pool.ReturnRowRead(rented);
            var child = new VisualElement();
            var parent = ParentOf(child);
            var placement = new ChildElementPlacement(pool, _reconciler.Context.HostCommit);
            var newElements = new List<(VisualElement? element, bool isExisting)> { (child, true) };
            var range = new ChildElementPlacement.PlacementRange { SlotStart = 0, ScanStart = 0, OldLen = 1, LogicalNewLen = 1 };

            // Act
            placement.ComputeLisAnchors(parent, newElements, in range, new HashSet<int>());

            // Assert
            Assert.That(pool.RentRowRead(), Is.SameAs(rented));
        }
    }
}
