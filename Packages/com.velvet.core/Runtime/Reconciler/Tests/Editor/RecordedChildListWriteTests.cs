using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a pass puts the rows it inserts or moves in a container it did not create: on the live list
    /// where the pass ends and not before, and in the order the diff declares when a patch that rewrites a slot reads
    /// the list after an earlier row of the same pass was placed.
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

        private static VNode[] Rows(params string[] keys)
            => keys.Select(key => (VNode)V.Div(key: key, name: key)).ToArray();

        private static string NamesOf(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        // GREEN_ON_BASE(characterization): the base places the row as it is declared; the case below the next one pins
        // where the row is while the reconcile is going on.
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
        public void Given_ASliceThatParkedAfterOneRow_When_TheResumedSliceCreatesTheRest_Then_EachSeesTheRowsTheParkedSliceApplied()
        {
            // Arrange
            var seen = new List<int>();
            var rows = Enumerable.Range(0, 4)
                .Select(i => (VNode)V.ScrollView(onCreated: _ => seen.Add(_root.childCount)))
                .ToArray();
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), rows, frameBudgetMs: 0.001);

            // Act
            _reconciler.ContinueReconcile(frameBudgetMs: 10000);

            // Assert
            Assert.That(string.Join(",", seen), Is.EqualTo("0,1,1,1"));
        }

        // GREEN_ON_BASE(refactor): the base inserts the replaced row at once, so the index the clip-path pass reads is
        // already current there; what the case pins is that it still is where the row waits for the end of the pass.
        [Test]
        public void Given_AnEarlierRowReplacedAndALaterOneGivenAClipPath_When_TheyPatchInOnePass_Then_TheRowsKeepTheirOrder()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div(name: "first"), V.Div(name: "second") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var newTree = new VNode[]
            {
                V.Label(text: "x", name: "first"),
                V.Div(name: "second", className: "clip-path-[circle(50%)]"),
            };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(_root[0].name, Is.EqualTo("first"));
        }

        // GREEN_ON_BASE(refactor): the base inserts the replaced row at once, so the index the clip-path pass reads is
        // already current there; what the case pins is that it still is where the row waits for the end of the pass.
        [Test]
        public void Given_AnEarlierRowReplacedAndALaterOneLosingItsClipPath_When_TheyPatchInOnePass_Then_TheRowsKeepTheirOrder()
        {
            // Arrange
            var oldTree = new VNode[] { V.Div(name: "first"), V.Div(name: "second", className: "clip-path-[circle(50%)]") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var newTree = new VNode[] { V.Label(text: "x", name: "first"), V.Div(name: "second") };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(_root[0].name, Is.EqualTo("first"));
        }
    }
}
