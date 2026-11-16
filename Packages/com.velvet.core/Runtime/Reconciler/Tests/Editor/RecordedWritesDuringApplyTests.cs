using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a child-list write made by a callback while a pass's recorded writes apply is recorded behind
    /// them, so the list holds what it did until the apply reaches the write.
    /// </summary>
    internal sealed class RecordedWritesDuringApplyTests : PanelTestBase
    {
        private Reconciler _reconciler = null!;

        public override void SetUp()
        {
            base.SetUp();
            _reconciler = new Reconciler();
        }

        public override void TearDown()
        {
            ChildListWrites.EndRecording(_reconciler.Context.Pass);
            _reconciler?.Dispose();
            base.TearDown();
        }

        // The first element's detach, which the apply fires, runs the write under test and reads the host's list; the
        // read is taken as a count, with -1 for a callback that never ran.
        private int ChildCountAfterTheWriteInTheApply(System.Action<VisualElement, VisualElement> write, out VisualElement host)
        {
            var parent = host = new VisualElement();
            _window.rootVisualElement.Add(parent);
            var first = new VisualElement();
            var label = new Label("kept text");
            parent.Add(first);
            parent.Add(label);
            var seen = -1;
            first.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                write(parent, label);
                seen = parent.childCount;
            });
            ChildListWrites.BeginRecording(_reconciler.Context.Pass);
            _reconciler.Context.HostCommit.Remove(parent, first);
            ChildListWrites.EndRecording(_reconciler.Context.Pass);
            return seen;
        }

        [Test]
        public void Given_AHierarchyRemoveMadeWhileTheRecordedWritesApply_When_TheCallbackReadsTheList_Then_TheChildIsStillThere()
        {
            // Arrange
            System.Action<VisualElement, VisualElement> write = (host, label) => ChildListWrites.HierarchyRemove(host, label);

            // Act
            var seen = ChildCountAfterTheWriteInTheApply(write, out _);

            // Assert
            Assert.That(seen, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base removes the child at once, so the list has lost it where the pass ends as well.
        [Test]
        public void Given_AHierarchyRemoveMadeWhileTheRecordedWritesApply_When_TheApplyEnds_Then_TheChildIsGone()
        {
            // Arrange
            System.Action<VisualElement, VisualElement> write = (host, label) => ChildListWrites.HierarchyRemove(host, label);

            // Act
            ChildCountAfterTheWriteInTheApply(write, out var host);

            // Assert
            Assert.That(host.childCount, Is.Zero);
        }

        [Test]
        public void Given_AClearMadeWhileTheRecordedWritesApply_When_TheCallbackReadsTheList_Then_TheChildIsStillThere()
        {
            // Arrange
            System.Action<VisualElement, VisualElement> write = (host, _) => ChildListWrites.Clear(host);

            // Act
            var seen = ChildCountAfterTheWriteInTheApply(write, out _);

            // Assert
            Assert.That(seen, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base clears at once, so the list is empty where the pass ends as well.
        [Test]
        public void Given_AClearMadeWhileTheRecordedWritesApply_When_TheApplyEnds_Then_TheListIsEmpty()
        {
            // Arrange
            System.Action<VisualElement, VisualElement> write = (host, _) => ChildListWrites.Clear(host);

            // Act
            ChildCountAfterTheWriteInTheApply(write, out var host);

            // Assert
            Assert.That(host.childCount, Is.Zero);
        }
    }
}
