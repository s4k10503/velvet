using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// Specifies that <see cref="HostCommitQueue"/>'s insert, removal and reorder allocate nothing once warm, so
    /// what a reconcile writes through it costs no allocation of the queue's own.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class HostCommitQueueAllocationTests
    {
        private Reconciler _reconciler = null!;

        private HostCommitQueue Host => _reconciler.Context.HostCommit;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static VisualElement ParentOf(int children)
        {
            var parent = new VisualElement();
            for (var i = 0; i < children; i++) parent.Add(new VisualElement());
            return parent;
        }

        [Test]
        public void Given_ADelegateAllocatingAKnownArray_When_Probed_Then_TheProbeCountsIt()
        {
            // Act
            var blocks = GCAllocationCanary.BlocksForKnownArray();

            // Assert
            Assert.That(blocks, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AWarmQueue_When_AChildIsInsertedAtTheAppendPosition_Then_NothingIsAllocated()
        {
            // Arrange
            var parent = ParentOf(2);
            var child = new VisualElement();
            Action takeOut = () => child.RemoveFromHierarchy();
            Action insert = () => Host.Insert(parent, child, before: null, physicalHint: parent.childCount);
            takeOut();
            insert();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(takeOut, insert);

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AWarmQueue_When_AChildIsRemoved_Then_NothingIsAllocated()
        {
            // Arrange — each window removes the child its arrangement put back.
            var parent = ParentOf(2);
            var children = new List<VisualElement>();
            for (var i = 0; i < 8; i++) children.Add(new VisualElement());
            var next = 0;
            VisualElement child = null!;
            Action putBack = () =>
            {
                child = children[next++ % children.Count];
                parent.Add(child);
            };
            Action remove = () => Host.Remove(parent, child);
            putBack();
            remove();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(putBack, remove);

            // Assert
            Assert.That(blocks, Is.Zero);
        }

        [Test]
        public void Given_AWarmQueue_When_ARangeIsReorderedToMoveItsLastChildFirst_Then_NothingIsAllocated()
        {
            // Arrange — the last child moves to the front; the other three are the anchors that stay.
            var parent = ParentOf(4);
            var children = new List<VisualElement>();
            for (var i = 0; i < 4; i++) children.Add(parent[i]);
            var newElements = new List<(VisualElement? element, bool isExisting)>
            {
                (children[3], true), (children[0], true), (children[1], true), (children[2], true),
            };
            var anchors = new HashSet<int> { 1, 2, 3 };
            Action putInOrder = () =>
            {
                parent.Clear();
                foreach (var child in children) parent.Add(child);
            };
            Action reorder = () => Host.Reorder(parent, newElements, anchors, slotStart: 0);
            putInOrder();
            reorder();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(putInOrder, reorder);

            // Assert
            Assert.That(blocks, Is.Zero);
        }
    }
}
