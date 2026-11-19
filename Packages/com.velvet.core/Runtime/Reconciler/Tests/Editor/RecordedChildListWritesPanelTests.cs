using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The writes made while <see cref="ChildListWrites"/> applies the recorded ones: a child that moves between
    /// containers on a panel sends its detach event from inside the call that applies the move, and a handler of it
    /// writes or announces a change there.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedChildListWritesPanelTests : PanelTestBase
    {
        private static readonly Type Writes = typeof(ChildListWrites);

        private VisualElement _source = null!;
        private VisualElement _destination = null!;
        private VisualElement _moving = null!;

        public override void SetUp()
        {
            base.SetUp();
            ChildListWrites.ViaQueue = 0;
            ChildListWrites.EndRecording();
            ClearStatic("s_nested");
            var root = _window.rootVisualElement;
            _source = new VisualElement();
            _destination = new VisualElement();
            _moving = new VisualElement();
            ChildListWrites.Add(root, _source);
            ChildListWrites.Add(root, _destination);
            ChildListWrites.Add(_source, _moving);
        }

        public override void TearDown()
        {
            ChildListWrites.ViaQueue = 0;
            ChildListWrites.EndRecording();
            base.TearDown();
        }

        private static void ClearStatic(string name)
        {
            var set = Writes.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            set.GetType().GetMethod("Clear")!.Invoke(set, null);
        }

        private static int NestedCount()
        {
            var set = Writes.GetField("s_nested", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return (int)set.GetType().GetProperty("Count")!.GetValue(set)!;
        }

        // Records the move of _moving into _destination, which applies where EndRecording is called.
        private void RecordTheMove()
        {
            ChildListWrites.BeginRecording();
            ChildListWrites.ViaQueue++;
            try { ChildListWrites.Insert(_destination, 0, _moving); }
            finally { ChildListWrites.ViaQueue--; }
        }

        [Test]
        public void Given_AHandlerThatRemovesAChildFromTheMovesDetach_When_TheRecordedMoveApplies_Then_TheChildIsRemoved()
        {
            // Arrange
            var doomed = new VisualElement();
            ChildListWrites.Add(_destination, doomed);
            _moving.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.Remove(_destination, doomed));
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(doomed.hierarchy.parent, Is.Null);
        }

        [Test]
        public void Given_AHandlerThatRemovesAChildItsContainersModelDoesNotHold_When_ItsCountIsReadInTheHandler_Then_TheLiveListAnswers()
        {
            // Arrange
            var bystander = new VisualElement();
            ChildListWrites.Add(bystander, new VisualElement());
            bystander.Add(new VisualElement());
            var foreign = bystander[1];
            var counted = -1;
            _moving.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChildListWrites.Remove(bystander, foreign);
                counted = ChildListWrites.ChildCount(bystander);
            });
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(counted, Is.EqualTo(2));
        }

        private VisualElement BystanderWhoseMiddleDriftedAndIsAnnouncedFromTheMovesDetach()
        {
            var bystander = new VisualElement();
            var middle = new VisualElement();
            ChildListWrites.Add(bystander, new VisualElement());
            ChildListWrites.Add(bystander, new VisualElement());
            ChildListWrites.Add(bystander, middle);
            ChildListWrites.Add(bystander, new VisualElement());
            bystander.hierarchy.Insert(1, middle);
            _moving.RegisterCallback<DetachFromPanelEvent>(_ => ChildListWrites.NoteHierarchyChange(bystander));
            return bystander;
        }

        [Test]
        public void Given_AnAnnouncedChangeInsideTheApplicationThatTheProbesDoNotSee_When_TheRecordedMoveApplies_Then_TheModelIsRebuilt()
        {
            // Arrange
            var bystander = BystanderWhoseMiddleDriftedAndIsAnnouncedFromTheMovesDetach();
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(ContainerRowsTests.CompareOrder(bystander, RowModelAccess.Peek(bystander)!), Is.Null);
        }

        [Test]
        public void Given_AnAnnouncedChangeInsideTheApplication_When_TheRecordedMoveApplies_Then_NoContainerIsLeftQueuedForComparison()
        {
            // Arrange
            BystanderWhoseMiddleDriftedAndIsAnnouncedFromTheMovesDetach();
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(NestedCount(), Is.EqualTo(0));
        }
    }
}
