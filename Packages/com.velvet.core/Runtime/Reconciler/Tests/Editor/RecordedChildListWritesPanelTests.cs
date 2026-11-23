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
        private ReconcilePass _pass = null!;

        public override void SetUp()
        {
            base.SetUp();
            _pass = new ReconcilePass();
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
            for (var i = _pass.Depth; i > 0; i--) ChildListWrites.EndRecording(_pass);
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
            ChildListWrites.BeginRecording(_pass);
            using (ChildListWrites.QueueWrite()) ChildListWrites.Insert(_destination, 0, _moving);
        }

        // GREEN_ON_BASE(characterization): the base takes out a child a handler removes while the recorded writes apply.
        // Drop `op.Child.RemoveFromHierarchy();` from the RemoveChild branch in ApplyOp and the child stays.
        [Test]
        public void Given_AHandlerThatRemovesAChildFromTheMovesDetach_When_TheRecordedMoveApplies_Then_TheChildIsRemoved()
        {
            // Arrange
            var doomed = new VisualElement();
            ChildListWrites.Add(_destination, doomed);
            // The window closing in TearDown detaches the moved child again, so the handler acts only while doomed is still held.
            _moving.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (doomed.hierarchy.parent == _destination) ChildListWrites.Remove(_destination, doomed);
            });
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(doomed.hierarchy.parent, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base answers a count from the live list when a removal the model does not hold is recorded.
        // Drop `&& rows.Contains(child)` from RecordRemoveChild and the case no longer reads the live count of 2.
        [Test]
        public void Given_AHandlerThatRemovesAChildItsContainersModelDoesNotHold_When_ItsCountIsReadInTheHandler_Then_TheLiveListAnswers()
        {
            // Arrange
            var bystander = new VisualElement();
            ChildListWrites.Add(bystander, new VisualElement());
            bystander.Add(new VisualElement());
            var foreign = bystander[1];
            var counted = -1;
            // The window closing in TearDown detaches the moved child again, so the handler acts only while foreign is still held.
            _moving.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (foreign.hierarchy.parent != bystander) return;
                ChildListWrites.Remove(bystander, foreign);
                counted = ChildListWrites.ChildCount(bystander);
            });
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording(_pass);

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

        // GREEN_ON_BASE(characterization): the base rebuilds the model of a container whose change is announced while the recorded writes apply.
        // Drop `foreach (var nested in s_nested) Settle(nested, full: true);` from Apply and the model keeps
        // the order it had.
        [Test]
        public void Given_AnAnnouncedChangeInsideTheApplicationThatTheProbesDoNotSee_When_TheRecordedMoveApplies_Then_TheModelIsRebuilt()
        {
            // Arrange
            var bystander = BystanderWhoseMiddleDriftedAndIsAnnouncedFromTheMovesDetach();
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(ContainerRowsTests.CompareOrder(bystander, RowModelAccess.Peek(bystander)!), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base leaves no container queued for comparison once the recorded writes have applied.
        // Drop `s_nested.Clear();` from Apply and the announced container stays queued.
        [Test]
        public void Given_AnAnnouncedChangeInsideTheApplication_When_TheRecordedMoveApplies_Then_NoContainerIsLeftQueuedForComparison()
        {
            // Arrange
            BystanderWhoseMiddleDriftedAndIsAnnouncedFromTheMovesDetach();
            RecordTheMove();

            // Act
            ChildListWrites.EndRecording(_pass);

            // Assert
            Assert.That(NestedCount(), Is.EqualTo(0));
        }
    }
}
