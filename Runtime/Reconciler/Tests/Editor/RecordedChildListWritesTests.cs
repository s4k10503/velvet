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

        [SetUp]
        public void SetUp()
        {
            ChildListWrites.ViaQueue = 0;
            ChildListWrites.EndRecording();
            ClearStatic("s_touched");
            ClearStatic("s_nested");
        }

        [TearDown]
        public void TearDown()
        {
            ChildListWrites.ViaQueue = 0;
            ChildListWrites.EndRecording();
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
            ChildListWrites.ViaQueue++;
            try { ChildListWrites.Insert(container, index, child); }
            finally { ChildListWrites.ViaQueue--; }
        }

        private static void RecordedRemoveAt(VisualElement container, int index)
        {
            ChildListWrites.ViaQueue++;
            try { ChildListWrites.RemoveAtForMove(container, index); }
            finally { ChildListWrites.ViaQueue--; }
        }

        // GREEN_ON_BASE(characterization): the base applies the recorded writes into a container before a removal from it.
        // Drop `FlushIfPending(container, child);` from Remove and the removal runs ahead of the insert.
        [Test]
        public void Given_ARecordedInsertIntoAContainer_When_AChildOfItIsRemoved_Then_TheRecordedInsertHasReachedTheLiveList()
        {
            // Arrange
            var kept = new VisualElement();
            var container = Holding(kept);
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 1, new VisualElement());

            // Act
            ChildListWrites.Remove(container, kept);

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base applies the recorded writes into a container before a removal through its hierarchy.
        // Drop `FlushIfPending(parent, child);` from HierarchyRemove and the removal runs ahead of the insert.
        [Test]
        public void Given_ARecordedInsertIntoAContainer_When_AChildOfItIsRemovedFromItsHierarchy_Then_TheRecordedInsertHasReachedTheLiveList()
        {
            // Arrange
            var kept = new VisualElement();
            var container = Holding(kept);
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 1, new VisualElement());

            // Act
            ChildListWrites.HierarchyRemove(container, kept);

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base applies the recorded writes into a container before it is cleared.
        // Drop `FlushIfPending(container, null);` from Clear and the insert lands after the clear.
        [Test]
        public void Given_ARecordedInsertAheadOfAChild_When_TheContainerIsCleared_Then_TheInsertedChildIsNotLeftBehind()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 0, new VisualElement());

            // Act
            ChildListWrites.Clear(container);
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base applies the recorded writes into a container before it is sorted.
        // Drop `FlushIfPending(parent, null);` from HierarchySort and the insert lands after the sort.
        [Test]
        public void Given_ARecordedInsertOfAChildThatSortsFirst_When_TheContainerIsSorted_Then_TheInsertedChildIsFirst()
        {
            // Arrange
            var inserted = new VisualElement { name = "a" };
            var container = Holding(new VisualElement { name = "b" });
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 1, inserted);

            // Act
            ChildListWrites.HierarchySort(container, (x, y) => string.CompareOrdinal(x.name, y.name));
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(container[0], Is.SameAs(inserted));
        }

        // GREEN_ON_BASE(characterization): the base applies a recording's writes when another pass begins recording.
        // Drop `if (s_recording) ApplyRecorded();` from BeginRecording and the insert stays recorded.
        [Test]
        public void Given_ARecordingWithAnInsertNotYetApplied_When_AnotherPassBeginsRecording_Then_TheInsertHasReachedTheLiveList()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 1, new VisualElement());

            // Act
            ChildListWrites.BeginRecording();

            // Assert
            Assert.That(container.hierarchy.childCount, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base forgets the containers a pass created when the next pass begins recording.
        // Drop `s_created.Clear();` from BeginRecording and the insert is made at once.
        [Test]
        public void Given_AContainerTheFirstPassCreated_When_ASecondPassBeginsRecording_Then_AnInsertIntoItIsRecorded()
        {
            // Arrange
            var created = new VisualElement();
            ChildListWrites.BeginRecording();
            ChildListWrites.MarkCreated(created);
            ChildListWrites.BeginRecording();

            // Act
            RecordedInsert(created, 0, new VisualElement());

            // Assert
            Assert.That(created.hierarchy.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base reads a recorded move out of a container from its model, so the moved child is not counted.
        // Drop `Mark(left);` from RecordInsert and the count reads the live list, which still holds the child.
        [Test]
        public void Given_ARecordedMoveOutOfAContainerWithAModel_When_ItsRecordedCountIsRead_Then_TheMovedChildIsNotCounted()
        {
            // Arrange
            var moved = new VisualElement();
            var source = Holding(moved);
            var destination = Holding(new VisualElement());
            ChildListWrites.BeginRecording();

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
            ChildListWrites.BeginRecording();

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
        // Drop `s_pendingParent[element] = null;` from RecordRemoveAt and the live parent answers.
        [Test]
        public void Given_ARecordedRemovalOfAChild_When_ItsParentIsRead_Then_ItHasNone()
        {
            // Arrange
            var child = new VisualElement();
            var container = Holding(child);
            ChildListWrites.BeginRecording();

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
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 5, child);

            // Act
            var parent = ChildListWrites.ParentOf(child);
            try { ChildListWrites.EndRecording(); }
            catch (ArgumentOutOfRangeException) { }

            // Assert
            Assert.That(parent, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base lets a write made while the recorded ones apply nest without
        // applying them again. Delete `if (s_applying) return;` from ApplyRecorded and the nested apply puts the first
        // insert back in front.
        [Test]
        public void Given_AWriteThatAppliesTheRecordedWritesWhileTheyApply_When_TheyApply_Then_TheyLandInTheOrderMade()
        {
            // Arrange
            var container = new ApplyingContainer();
            var first = new VisualElement();
            var second = new VisualElement();
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 0, first);
            RecordedInsert(container, 0, second);
            container.ApplyOnNextRead = true;

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(container.hierarchy[0], Is.SameAs(second));
        }

        // The container's content read is where the first live insert of the apply passes through.
        private sealed class ApplyingContainer : VisualElement
        {
            internal bool ApplyOnNextRead;

            public override VisualElement contentContainer
            {
                get
                {
                    if (ApplyOnNextRead)
                    {
                        ApplyOnNextRead = false;
                        ChildListWrites.ApplyRecorded();
                    }
                    return this;
                }
            }
        }

        // GREEN_ON_BASE(characterization): the base lets go of the containers a pass created when it ends, so none is
        // held after it. Drop `s_created.Clear();` from EndRecording and the set still holds the container.
        [Test]
        public void Given_AContainerAPassCreated_When_ThePassEnds_Then_NoCreatedContainerIsHeld()
        {
            // Arrange
            ChildListWrites.BeginRecording();
            ChildListWrites.MarkCreated(new VisualElement());

            // Act
            ChildListWrites.EndRecording();

            // Assert
            var created = Writes.GetField("s_created", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Assert.That(created.GetType().GetProperty("Count")!.GetValue(created), Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base drops the model of a container the recorded writes left empty.
        // Drop `PruneIfEmpty(container);` from the loop in ApplyRecorded's finally and the empty model stays.
        [Test]
        public void Given_ARecordedRemovalOfTheOnlyChild_When_TheRecordedWritesApply_Then_TheContainerKeepsNoModel()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording();
            RecordedRemoveAt(container, 0);

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(RowModelAccess.Peek(container), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base queues a container for verification again once the writes recorded into it have applied.
        // Drop `Touch(container);` from the loop in ApplyRecorded's finally and the next verification skips it,
        // leaving the model at 2.
        [Test]
        public void Given_AVerificationBetweenARecordedInsertAndItsApplication_When_AForeignWriteLeftTheModelBehind_Then_TheNextVerificationRebuildsIt()
        {
            // Arrange
            var container = Holding(new VisualElement());
            ChildListWrites.BeginRecording();
            RecordedInsert(container, 1, new VisualElement());
            ChildListWrites.VerifyTouched();
            container.hierarchy.Insert(0, new VisualElement());
            ChildListWrites.EndRecording();
            LogAssert.Expect(LogType.Assert, new Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(RowModelAccess.Peek(container)!.TotalCount, Is.EqualTo(3));
        }
    }
}
