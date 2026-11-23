using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The writes <see cref="ChildListWrites"/> records while a pass is recording and applies later: what a write
    /// that reads or changes the same container does with the ones still recorded, and what the model and the live
    /// list hold once they apply. None needs a panel.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedChildListWritesTests
    {
        private static readonly Type Writes = typeof(ChildListWrites);

        private ReconcilePass _pass = null!;

        [SetUp]
        public void SetUp()
        {
            _pass = new ReconcilePass();
            ClearStatic("s_touched");
            ClearStatic("s_nested");
        }

        [TearDown]
        public void TearDown()
        {
            if (_pass.Recording) ChildListWrites.EndRecording(_pass);
        }

        private static void ClearStatic(string name)
        {
            var set = Writes.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            set.GetType().GetMethod("Clear")!.Invoke(set, null);
        }

        private static VisualElement Holding(params VisualElement[] children)
        {
            var container = new VisualElement();
            foreach (var child in children) ChildListWrites.Add(container, child);
            return container;
        }

        private static void RecordedInsert(VisualElement container, int index, VisualElement child)
        {
            using (ChildListWrites.QueueWrite()) ChildListWrites.Insert(container, index, child);
        }

        private static void RecordedRemoveAt(VisualElement container, int index)
        {
            using (ChildListWrites.QueueWrite()) ChildListWrites.RemoveAtForMove(container, index);
        }

        // GREEN_ON_BASE(characterization): the base applies a removal behind the insert recorded into the container ahead of it.
        // Replace `HoldingPass(container, child)` in Remove with `null` and the removal runs ahead of the insert.
        [Test]
        public void Given_ARecordedInsertIntoAContainer_When_AChildOfItIsRemovedAndThePassEnds_Then_OnlyTheInsertedChildIsLeft()
        {
            // Arrange
            var kept = new VisualElement();
            var container = Holding(kept);
            ChildListWrites.BeginRecording(_pass);
            var inserted = new VisualElement();
            RecordedInsert(container, 1, inserted);

            // Act
            ChildListWrites.Remove(container, kept);
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(container.hierarchy[0], Is.SameAs(inserted));
        }

        // GREEN_ON_BASE(characterization): the base applies a removal through the hierarchy behind the insert recorded into the container ahead of it.
        // Replace `HoldingPass(parent, child)` in HierarchyRemove with `null` and the removal runs ahead of the insert.
        [Test]
        public void Given_ARecordedInsertIntoAContainer_When_AChildOfItIsRemovedFromItsHierarchyAndThePassEnds_Then_OnlyTheInsertedChildIsLeft()
        {
            // Arrange
            var kept = new VisualElement();
            var container = Holding(kept);
            ChildListWrites.BeginRecording(_pass);
            var inserted = new VisualElement();
            RecordedInsert(container, 1, inserted);

            // Act
            ChildListWrites.HierarchyRemove(container, kept);
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(container.hierarchy[0], Is.SameAs(inserted));
        }

        // GREEN_ON_BASE(characterization): the base applies a clear behind the insert recorded into the container ahead of it.
        // Replace `HoldingPass(container, null)` in Clear with `null` and the clear runs ahead of the insert.
        [Test]
        public void Given_ARecordedInsertAheadOfAChild_When_TheContainerIsCleared_Then_TheInsertedChildIsNotLeftBehind()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording(_pass);
            RecordedInsert(container, 0, new VisualElement());

            // Act
            ChildListWrites.Clear(container);
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base reads a recorded move out of a container from its model, so the moved child is not counted.
        // Drop `Mark(pass, left);` from RecordInsert and the count reads the live list, which still holds the child.
        [Test]
        public void Given_ARecordedMoveOutOfAContainerWithAModel_When_ItsRecordedCountIsRead_Then_TheMovedChildIsNotCounted()
        {
            // Arrange
            var moved = new VisualElement();
            var source = Holding(moved);
            var destination = Holding(new VisualElement());
            ChildListWrites.BeginRecording(_pass);

            // Act
            RecordedInsert(destination, 1, moved);

            // Assert
            Assert.That(ChildListWrites.ChildCount(source), Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base takes a recorded move's child out of the model of the container it leaves.
        // Drop `leftRows.Remove(child);` from RecordInsert and the model still holds the child.
        [Test]
        public void Given_ARecordedMoveOutOfAContainerWithAModel_When_TheModelIsRead_Then_ItNoLongerHoldsTheMovedChild()
        {
            // Arrange
            var moved = new VisualElement();
            var source = Holding(moved);
            var destination = Holding(new VisualElement());
            ChildListWrites.BeginRecording(_pass);

            // Act
            RecordedInsert(destination, 1, moved);

            // Assert
            Assert.That(RowModelAccess.Peek(source)!.TotalCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base removes a child at once for a move where nothing is recording.
        // Delete the closing `RemoveAt(parent, index);` call of RemoveAtForMove and the child stays.
        [Test]
        public void Given_AChildInAContainerWithNoRecording_When_ItIsRemovedForAMove_Then_TheChildIsGone()
        {
            // Arrange
            var container = Holding(new VisualElement());

            // Act
            ChildListWrites.RemoveAtForMove(container, 0);

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base reads a child whose removal is recorded as having no parent.
        // Drop `pass.PendingParent[element] = null;` from RecordRemoveAt and the live parent answers.
        [Test]
        public void Given_ARecordedRemovalOfAChild_When_ItsParentIsRead_Then_ItHasNone()
        {
            // Arrange
            var child = new VisualElement();
            var container = Holding(child);
            ChildListWrites.BeginRecording(_pass);

            // Act
            RecordedRemoveAt(container, 0);

            // Assert
            Assert.That(ChildListWrites.ParentOf(child), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base leaves a child whose recorded insert names an index past the end with
        // the parent it has. Change `var held = false;` in RecordInsert to true and the model names the container.
        [Test]
        public void Given_ARecordedInsertPastTheEndOfAContainer_When_TheChildsParentIsRead_Then_ItHasNone()
        {
            // Arrange
            var container = Holding(new VisualElement());
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_pass);
            RecordedInsert(container, 5, child);

            // Act
            var parent = ChildListWrites.ParentOf(child);
            try { ChildListWrites.EndRecording(_pass); }
            catch (ArgumentOutOfRangeException) { }

            // Assert
            Assert.That(parent, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base lets go of the containers a pass created when it ends, so none is
        // held after it. Drop `pass.Created.Clear();` from EndRecording and the set still holds the container.
        [Test]
        public void Given_AContainerAPassCreated_When_ThePassEnds_Then_NoCreatedContainerIsHeld()
        {
            // Arrange
            ChildListWrites.BeginRecording(_pass);
            ChildListWrites.MarkCreated(new VisualElement());

            // Act
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(_pass.Created.Count, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base drops the model of a container the recorded writes left empty.
        // Drop `PruneIfEmpty(container);` from the loop in Apply's finally and the empty model stays.
        [Test]
        public void Given_ARecordedRemovalOfTheOnlyChild_When_TheRecordedWritesApply_Then_TheContainerKeepsNoModel()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording(_pass);
            RecordedRemoveAt(container, 0);

            // Act
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(RowModelAccess.Peek(container), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base queues a container for verification again once the writes recorded into it have applied.
        // Drop `Touch(container);` from the loop in Apply's finally and the next verification skips it,
        // leaving the model at 2.
        [Test]
        public void Given_AVerificationBetweenARecordedInsertAndItsApplication_When_AForeignWriteLeftTheModelBehind_Then_TheNextVerificationRebuildsIt()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording(_pass);
            RecordedInsert(container, 1, new VisualElement());
            ChildListWrites.VerifyTouched();
            container.hierarchy.Insert(0, new VisualElement());
            ChildListWrites.EndRecording(_pass);
            LogAssert.Expect(LogType.Assert, new Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(RowModelAccess.Peek(container)!.TotalCount, Is.EqualTo(3));
        }
    }
}
