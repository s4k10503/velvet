using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a reorder pass does with children that are already in the order the list names.
    /// <list type="bullet">
    /// <item>It leaves them without building a position table of the container: each child is hashed once by that
    /// table to insert it and once to look it up, which the hashes of the children count.</item>
    /// <item>A pass that does move a child does build the table, so the count above is a count of that work and not of
    /// nothing, and it still reports the order the components in the moved children were created in.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ChildElementPlacementReorderTests
    {
        private sealed class HashedChild : VisualElement
        {
            public int Hashes;

            public override int GetHashCode()
            {
                Hashes++;
                return base.GetHashCode();
            }
        }

        private static (VisualElement Parent, HashedChild[] Children, List<(VisualElement? element, bool isExisting)> NewElements)
            Arranged(int count, int[] newOrder)
        {
            var parent = new VisualElement();
            var children = new HashedChild[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = new HashedChild();
                parent.Add(children[i]);
            }
            var newElements = new List<(VisualElement? element, bool isExisting)>();
            foreach (var index in newOrder) newElements.Add((children[index], true));
            return (parent, children, newElements);
        }

        private static int HashesOfAReorderWith(int[] newOrder, HashSet<int> anchors)
        {
            var (parent, children, newElements) = Arranged(5, newOrder);
            var placement = new ChildElementPlacement(new ReconcilerBufferPool());
            placement.ReorderToNewElementOrder(parent, newElements, anchors, slotStart: 0);
            var hashes = 0;
            foreach (var child in children) hashes += child.Hashes;
            return hashes;
        }

        [Test]
        public void Given_ChildrenAlreadyInTheListedOrder_When_TheyAreReordered_Then_NoChildIsHashed()
        {
            // Act
            var hashes = HashesOfAReorderWith(new[] { 0, 1, 2, 3, 4 }, new HashSet<int> { 0, 1, 2, 3, 4 });

            // Assert
            Assert.That(hashes, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the instrument for the case above: a pass that moves a child hashes the base's own position table.
        [Test]
        public void Given_AChildListedOutOfOrder_When_TheChildrenAreReordered_Then_ChildrenAreHashed()
        {
            // Act
            var hashes = HashesOfAReorderWith(new[] { 4, 0, 1, 2, 3 }, new HashSet<int> { 1, 2, 3, 4 });

            // Assert
            Assert.That(hashes, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base already reports this order, which the shortcut has to leave to the pass that moves.
        [Test]
        public void Given_AChildListedAheadOfTheOnesBeforeIt_When_TheChildrenAreReordered_Then_TheOrderIsReportedAsOutOfCreationOrder()
        {
            // Arrange
            var (parent, _, newElements) = Arranged(3, new[] { 2, 0, 1 });
            var placement = new ChildElementPlacement(new ReconcilerBufferPool());

            // Act
            var outOfCreationOrder = placement.ReorderToNewElementOrder(parent, newElements, new HashSet<int> { 1, 2 }, slotStart: 0);

            // Assert
            Assert.That(outOfCreationOrder, Is.True);
        }
    }
}
