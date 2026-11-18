using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a removal made while a pass's recorded writes apply is recorded behind them and its element
    /// returned to its pool only after it has left, as a removal made in the walk is.
    /// </summary>
    internal sealed class RemovalDuringApplyTests : PanelTestBase
    {
        private Reconciler _reconciler = null!;

        public override void SetUp()
        {
            base.SetUp();
            VNodePoolTestAccess.ClearLabelPoolForTest();
            _reconciler = new Reconciler();
        }

        public override void TearDown()
        {
            ChildListWrites.EndRecording(_reconciler.Context.Pass);
            _reconciler?.Dispose();
            base.TearDown();
        }

        // GREEN_ON_BASE(characterization): the base takes the label out as the callback reaches it and resets it
        // after, so its detach is heard before the reset there too; what the case holds is that order once the removal
        // is recorded behind the apply and the pool return is queued for after it.
        [Test]
        public void Given_ARemovalMadeWhileTheRecordedWritesApply_When_TheRemovedLabelLeavesThePanel_Then_ItHasNotBeenReset()
        {
            // Arrange — the first element's detach, which the apply fires, removes the label through the queue.
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            var first = new VisualElement();
            var label = new Label("kept text");
            host.Add(first);
            host.Add(label);
            var seen = "unset";
            label.RegisterCallback<DetachFromPanelEvent>(_ => seen = label.text);
            first.RegisterCallback<DetachFromPanelEvent>(_ => _reconciler.Context.HostCommit.Remove(host, label));
            ChildListWrites.BeginRecording(_reconciler.Context.Pass);
            _reconciler.Context.HostCommit.Remove(host, first);

            // Act
            ChildListWrites.EndRecording(_reconciler.Context.Pass);

            // Assert
            Assert.That(seen, Is.EqualTo("kept text"));
        }
    }
}
