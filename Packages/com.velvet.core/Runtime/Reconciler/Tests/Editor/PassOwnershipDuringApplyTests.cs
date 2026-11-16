using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass that begins and ends while another's recorded writes apply puts its own writes on the live
    /// list where it ends, and that the writes still recorded stay with the pass applying them: they are not applied
    /// early, and the pending count of a container they touch survives the other pass's end.
    /// </summary>
    internal sealed class PassOwnershipDuringApplyTests : PanelTestBase
    {
        private Reconciler _applying = null!;
        private Reconciler _nested = null!;
        private VisualElement _shared = null!;

        public override void SetUp()
        {
            base.SetUp();
            _applying = new Reconciler();
            _nested = new Reconciler();
        }

        public override void TearDown()
        {
            ChildListWrites.EndRecording(_applying.Context.Pass);
            // The other pass's insert moved the live index the recorded removal names, so the model of the shared
            // container is left unlike its list; it goes before a later pass verifies the containers written.
            if (_shared != null) ChildListWrites.Drop(_shared);
            _applying?.Dispose();
            _nested?.Dispose();
            base.TearDown();
        }

        [Test]
        public void Given_APassBeginningAndEndingInsideAnApply_When_ItEnds_Then_ItsInsertIsOnTheLiveListAtOnce()
        {
            // Arrange — the first element's detach, which the apply fires, runs a pass of another context.
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            var first = new VisualElement();
            host.Add(first);
            var target = new VisualElement();
            var liveInsideTheApply = -1;
            first.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChildListWrites.BeginRecording(_nested.Context.Pass);
                _nested.Context.HostCommit.Insert(target, 0, new VisualElement());
                ChildListWrites.EndRecording(_nested.Context.Pass);
                liveInsideTheApply = target.childCount;
            });
            ChildListWrites.BeginRecording(_applying.Context.Pass);
            _applying.Context.HostCommit.Remove(host, first);

            // Act
            ChildListWrites.EndRecording(_applying.Context.Pass);

            // Assert
            Assert.That(liveInsideTheApply, Is.EqualTo(1));
        }

        private (VisualElement host, VisualElement first, VisualElement shared, VisualElement removed) ArrangeARecordedRemoval()
        {
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            var first = new VisualElement();
            host.Add(first);
            var shared = new VisualElement();
            shared.Add(new VisualElement());
            var removed = new VisualElement();
            shared.Add(removed);
            _shared = shared;
            return (host, first, shared, removed);
        }

        [Test]
        public void Given_APassBeginningInsideAnApply_When_ItBegins_Then_TheWritesStillRecordedAreNotAppliedEarly()
        {
            // Arrange
            var (host, first, shared, removed) = ArrangeARecordedRemoval();
            var liveWhenItBegan = -1;
            first.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChildListWrites.BeginRecording(_nested.Context.Pass);
                liveWhenItBegan = shared.childCount;
                ChildListWrites.EndRecording(_nested.Context.Pass);
            });
            ChildListWrites.BeginRecording(_applying.Context.Pass);
            _applying.Context.HostCommit.Remove(host, first);
            _applying.Context.HostCommit.RemoveRow(shared, removed);

            // Act
            ChildListWrites.EndRecording(_applying.Context.Pass);

            // Assert
            Assert.That(liveWhenItBegan, Is.EqualTo(2));
        }

        [Test]
        public void Given_APassEndingInsideAnApplyOverAContainerTheApplyStillHasPending_When_ItEnds_Then_TheContainerReadsFromItsModel()
        {
            // Arrange
            var (host, first, shared, removed) = ArrangeARecordedRemoval();
            var readInsideTheApply = -1;
            first.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ChildListWrites.BeginRecording(_nested.Context.Pass);
                _nested.Context.HostCommit.Insert(shared, 0, new VisualElement());
                ChildListWrites.EndRecording(_nested.Context.Pass);
                readInsideTheApply = ChildListWrites.HierarchyCount(shared);
            });
            ChildListWrites.BeginRecording(_applying.Context.Pass);
            _applying.Context.HostCommit.Remove(host, first);
            _applying.Context.HostCommit.RemoveRow(shared, removed);

            // Act
            ChildListWrites.EndRecording(_applying.Context.Pass);

            // Assert
            Assert.That(readInsideTheApply, Is.EqualTo(2));
        }
    }
}
