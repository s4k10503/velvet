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
