using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a reorder pass does with children that are already in the order the list names.
    /// <list type="bullet">
    /// <item>It leaves them without looking any of them up in the container's row model: a container of several
    /// blocks finds a child by hashing it, which the hashes of the children count.</item>
    /// <item>A pass that does move a child does look them up, so the count above is a count of that work and not of
    /// nothing, and it still reports the order the components in the moved children were created in.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ChildElementPlacementReorderTests
    {
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private ChildElementPlacement Placement()
            => new(new ReconcilerBufferPool(), _reconciler.Context.HostCommit);

        private sealed class HashedChild : VisualElement
        {
            public int Hashes;

            public override int GetHashCode()
            {
                Hashes++;
                return base.GetHashCode();
            }
        }

        // Wide enough for the row model to keep several blocks and find a child by hash.
        private const int Wide = 300;

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
            ChildListWrites.PhysicalIndexOf(parent, children[0]);
            foreach (var child in children) child.Hashes = 0;
            return (parent, children, newElements);
        }

        private int HashesOfAReorderWith(int[] newOrder, HashSet<int> anchors)
        {
            var (parent, children, newElements) = Arranged(Wide, newOrder);
            var placement = Placement();
            placement.ReorderToNewElementOrder(parent, newElements, anchors, slotStart: 0);
            var hashes = 0;
            foreach (var child in children) hashes += child.Hashes;
            return hashes;
        }

        // GREEN_ON_BASE(refactor): the base leaves children already in order unhashed too; the case keeps that
        // shortcut while the pass that does move reads the row model.
        [Test]
        public void Given_ChildrenAlreadyInTheListedOrder_When_TheyAreReordered_Then_NoChildIsHashed()
        {
            // Act
            var hashes = HashesOfAReorderWith(new[] { 0, 1, 2, 3, 4 }, new HashSet<int> { 0, 1, 2, 3, 4 });

            // Assert
            Assert.That(hashes, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the instrument for the case above: a pass that moves a child hashes, through
        // the base's position table there and through the row model's lookups here.
        [Test]
        public void Given_AChildListedOutOfOrder_When_TheChildrenAreReordered_Then_ChildrenAreHashed()
        {
            // Act
            var hashes = HashesOfAReorderWith(new[] { 4, 0, 1, 2, 3 }, new HashSet<int> { 1, 2, 3, 4 });

            // Assert
            Assert.That(hashes, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base reports this order too; the case holds it now that the creation-order
        // check reads each kept child's position from the row model.
        [Test]
        public void Given_AChildListedAheadOfTheOnesBeforeIt_When_TheChildrenAreReordered_Then_TheOrderIsReportedAsOutOfCreationOrder()
        {
            // Arrange
            var (parent, _, newElements) = Arranged(3, new[] { 2, 0, 1 });
            var placement = Placement();

            // Act
            var outOfCreationOrder = placement.ReorderToNewElementOrder(parent, newElements, new HashSet<int> { 1, 2 }, slotStart: 0);

            // Assert
            Assert.That(outOfCreationOrder, Is.True);
        }
    }
}
