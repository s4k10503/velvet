using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a write made outside <see cref="HostCommitQueue"/> lands in a container the pass holds recorded
    /// removals of: in the model at once and on the live list where the pass ends, behind the recorded ones, and not
    /// on the live list while the walk goes on.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedWritesOutsideTheQueueTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;
        private string _liveSeen = "unset";
        private string _modelSeen = "unset";
        private VisualElement _left = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            ChildListWrites.EndRecording(_reconciler.Context.Pass);
            _reconciler.Dispose();
        }

        private static VNode Branch(string name, params VNode[] children)
            => V.Div(key: name, name: name, children: children);

        private static string LiveNamesOf(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        private static string ModelNamesOf(VisualElement container)
            => string.Join(",", Enumerable.Range(0, ChildListWrites.ChildCount(container))
                .Select(index => ChildListWrites.ElementAtPhysical(container, index)!.name));

        // The container "left" holds a, b and c, and the pass removes c from it: the removal is recorded, so the model
        // holds a and b while the live list holds all three. The walk then creates a row of "right", whose callback
        // makes the write under test to "left" and reads both lists.
        private void Walk(Action<VisualElement> write)
        {
            var oldTree = new[] { Branch("left", V.Div(name: "a"), V.Div(name: "b"), V.Div(name: "c")), Branch("right") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var left = _left = _root.Q<VisualElement>("left");
            var newTree = new[]
            {
                Branch("left", V.Div(name: "a"), V.Div(name: "b")),
                Branch("right", V.ScrollView(onCreated: _ =>
                {
                    write(left);
                    _liveSeen = LiveNamesOf(left);
                    _modelSeen = ModelNamesOf(left);
                })),
            };
            _reconciler.Reconcile(_root, oldTree, newTree);
        }

        // ---- Insert ----

        [Test]
        public void Given_AnInsertOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Insert(left, 0, new VisualElement { name = "extra" });

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and made the insert by then, so its live list holds what the model does.
        [Test]
        public void Given_AnInsertOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsTheInsertedRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Insert(left, 0, new VisualElement { name = "extra" });

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.EqualTo("extra,a,b"));
        }

        // GREEN_ON_BASE(characterization): the base makes the insert after the recorded removal, so it lands where the pass ends in the same order.
        [Test]
        public void Given_AnInsertOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsTheInsertedRowAndNotTheRemovedOne()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Insert(left, 0, new VisualElement { name = "extra" });

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.EqualTo("extra,a,b"));
        }

        // ---- Remove ----

        [Test]
        public void Given_ARemoveOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Remove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and made this one by then, so its live list holds what the model does.
        [Test]
        public void Given_ARemoveOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Remove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base makes the removal behind the recorded one, as the pass does.
        [Test]
        public void Given_ARemoveOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Remove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.EqualTo("b"));
        }

        // ---- HierarchyRemove ----

        [Test]
        public void Given_AHierarchyRemoveOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.HierarchyRemove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and made this one by then, so its live list holds what the model does.
        [Test]
        public void Given_AHierarchyRemoveOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.HierarchyRemove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base makes the removal behind the recorded one, as the pass does.
        [Test]
        public void Given_AHierarchyRemoveOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.HierarchyRemove(left, left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.EqualTo("b"));
        }

        // ---- RemoveAt ----

        [Test]
        public void Given_ARemoveAtOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveAt(left, 0);

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and made this one by then, so its live list holds what the model does.
        [Test]
        public void Given_ARemoveAtOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveAt(left, 0);

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base makes the removal behind the recorded one, as the pass does.
        [Test]
        public void Given_ARemoveAtOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveAt(left, 0);

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.EqualTo("b"));
        }

        // ---- RemoveFromHierarchy ----

        [Test]
        public void Given_ARemoveFromHierarchyOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveFromHierarchy(left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and made this one by then, so its live list holds what the model does.
        [Test]
        public void Given_ARemoveFromHierarchyOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveFromHierarchy(left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base makes the removal behind the recorded one, as the pass does.
        [Test]
        public void Given_ARemoveFromHierarchyOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsOnlyTheRemainingRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.RemoveFromHierarchy(left[0]);

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.EqualTo("b"));
        }

        // ---- Clear ----

        [Test]
        public void Given_AClearOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheOldRows()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Clear(left);

            // Act
            Walk(write);

            // Assert
            Assert.That(_liveSeen, Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base has applied the recorded removal and cleared by then, so its live list holds what the model does.
        [Test]
        public void Given_AClearOutsideTheQueue_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsNoRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Clear(left);

            // Act
            Walk(write);

            // Assert
            Assert.That(_modelSeen, Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the base clears behind the recorded removal, as the pass does.
        [Test]
        public void Given_AClearOutsideTheQueue_When_TheReconcileReturns_Then_TheLiveListHoldsNoRow()
        {
            // Arrange
            Action<VisualElement> write = left => ChildListWrites.Clear(left);

            // Act
            Walk(write);

            // Assert
            Assert.That(LiveNamesOf(_left), Is.Empty);
        }
    }
}
