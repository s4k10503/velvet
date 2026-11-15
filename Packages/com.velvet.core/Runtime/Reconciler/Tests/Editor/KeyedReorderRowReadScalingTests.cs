using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that re-placing a few children of a long container reads a number of its children that does not
    /// grow with the container: each child is found from the container's row model rather than by a table of every
    /// child or a scan from the front.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedReorderRowReadScalingTests
    {
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private sealed class HashedChild : VisualElement
        {
            public int Hashes;

            public override int GetHashCode()
            {
                Hashes++;
                return base.GetHashCode();
            }
        }

        private const int Moved = 8;

        // Rotates the last Moved children of a container of count children, the first of them to the end, and
        // returns the hashes that took. The row model is built before the count starts, so what is counted is the
        // pass and not the model's first read.
        private int HashesOfARotationAtTheEndOf(int count)
        {
            var parent = new VisualElement();
            var children = new HashedChild[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = new HashedChild();
                parent.Add(children[i]);
            }
            var first = count - Moved;
            var newElements = new List<(VisualElement? element, bool isExisting)>();
            for (var i = 1; i < Moved; i++) newElements.Add((children[first + i], true));
            newElements.Add((children[first], true));
            var anchors = new HashSet<int>();
            for (var i = 0; i < Moved - 1; i++) anchors.Add(i);
            var placement = new ChildElementPlacement(new ReconcilerBufferPool(), _reconciler.Context.HostCommit);
            ChildListWrites.PhysicalIndexOf(parent, children[0]);
            foreach (var child in children) child.Hashes = 0;

            placement.ReorderToNewElementOrder(parent, newElements, anchors, slotStart: first);

            var hashes = 0;
            foreach (var child in children) hashes += child.Hashes;
            return hashes;
        }

        [Test]
        public void Given_ARotationOfTheLastChildren_When_TheContainerIsTwiceAsLong_Then_TheHashesGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            var hashesAtN = HashesOfARotationAtTheEndOf(1000);

            // Act
            var hashesAt2N = HashesOfARotationAtTheEndOf(2000);

            // Assert — a pass that hashed nothing leaves no ratio, so it fails rather than passing as constant.
            var growth = hashesAtN > 0 ? (double)hashesAt2N / hashesAtN : double.NaN;
            Assert.That(growth, Is.LessThan(1.5));
        }
    }
}
