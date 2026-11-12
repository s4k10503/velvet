using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Specifies that an unchanged flat list reconciles without allocating once the reconciler's pools are warm,
    /// so a re-render reaching it pays nothing for its container's old instances.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class FlatListReconcileAllocationTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        // GREEN_ON_BASE(refactor): the probe counts a known array whatever the reconciler does.
        // It is what makes the two zero counts below mean something.
        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(refactor): the base allocates nothing reconciling an unchanged indexed list.
        // Dropping the return in ReconcileIndexedFrom's `finally` reddens it, at 5 blocks.
        [Test]
        public void Given_AWarmPool_When_AnUnchangedIndexedListReconciles_Then_NothingIsAllocated()
        {
            // Arrange — the first pass warms the pools, the lists' capacity and the delegate.
            var nodes = new VNode[] { V.Label(text: "a"), V.Label(text: "b"), V.Label(text: "c") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);
            Action pass = () => _reconciler.Reconcile(_root, nodes, nodes);
            pass();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(pass);

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        // GREEN_ON_BASE(refactor): the base allocates nothing reconciling an unchanged keyed list.
        // Dropping the return in ReconcileKeyedSync's `finally` reddens it, at 5 blocks.
        [Test]
        public void Given_AWarmPool_When_AnUnchangedKeyedListReconciles_Then_NothingIsAllocated()
        {
            // Arrange
            var nodes = new VNode[]
            {
                V.Label(text: "a", key: "a"), V.Label(text: "b", key: "b"), V.Label(text: "c", key: "c"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), nodes);
            Action pass = () => _reconciler.Reconcile(_root, nodes, nodes);
            pass();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(pass);

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
