using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a pass puts the rows it inserts or moves in a container it did not create: on the live list when
    /// the pass ends, and not at all when the pass is declined, so that the live order is the order the next pass diffs
    /// from.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedChildListWriteTests
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
        public void TearDown() => _reconciler.Dispose();

        private sealed class ThrowingElement : VisualElement
        {
            public ThrowingElement() => throw new InvalidOperationException("boom");
        }

        private static VNode Throwing() => V.Custom<ThrowingElement>();

        private static VNode[] Rows(params string[] keys)
            => keys.Select(key => (VNode)V.Div(key: key, name: key)).ToArray();

        private static string NamesOf(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        // GREEN_ON_BASE(characterization): the base places the row as it is declared; what the cases below pin is where
        // the row is while the reconcile is going on and what a throw leaves.
        [Test]
        public void Given_AnInsertIntoACommittedContainer_When_TheReconcileReturns_Then_TheLiveListHoldsTheRow()
        {
            // Arrange
            var oldTree = Rows("a", "b");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, Rows("a", "x", "b"));

            // Assert
            Assert.That(NamesOf(_root), Is.EqualTo("a,x,b"));
        }

        // GREEN_ON_BASE(characterization): the base moves the row as it is declared, as the case above does for an insert.
        [Test]
        public void Given_AMovedRow_When_TheReconcileReturns_Then_TheLiveListHoldsTheNewOrder()
        {
            // Arrange
            var oldTree = Rows("a", "b", "c");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            _reconciler.Reconcile(_root, oldTree, Rows("c", "a", "b"));

            // Assert
            Assert.That(NamesOf(_root), Is.EqualTo("c,a,b"));
        }

        [Test]
        public void Given_RowsInsertedBeforeALaterRow_When_ThatRowIsCreated_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            var oldTree = Rows("a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var seen = -1;
            var later = V.Div(key: "later", children: new VNode[] { V.ScrollView(onCreated: _ => seen = _root.childCount) });

            // Act
            _reconciler.Reconcile(_root, oldTree, Rows("a", "x", "y").Append(later).ToArray());

            // Assert
            Assert.That(seen, Is.EqualTo(1));
        }

        [Test]
        public void Given_RowsInserted_When_ALaterRowThrows_Then_TheContainerKeepsItsOldRows()
        {
            // Arrange
            var oldTree = Rows("a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            Assert.Catch(() =>
                _reconciler.Reconcile(_root, oldTree, Rows("a", "x", "y").Append(Throwing()).ToArray()));

            // Assert
            Assert.That(NamesOf(_root), Is.EqualTo("a"));
        }

        [Test]
        public void Given_RowsMoved_When_ALaterRowThrows_Then_TheContainerKeepsItsOldOrder()
        {
            // Arrange
            var oldTree = Rows("a", "b", "c");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);

            // Act
            Assert.Catch(() =>
                _reconciler.Reconcile(_root, oldTree, Rows("c", "a", "b").Append(Throwing()).ToArray()));

            // Assert
            Assert.That(NamesOf(_root), Is.EqualTo("a,b,c"));
        }
    }
}
