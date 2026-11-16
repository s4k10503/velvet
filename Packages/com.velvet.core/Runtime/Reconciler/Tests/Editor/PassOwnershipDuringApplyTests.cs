using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass that begins and ends while another's recorded writes apply puts its own writes on the live
    /// list where it ends, and leaves the writes still recorded to the pass applying them.
    /// </summary>
    internal sealed class PassOwnershipDuringApplyTests : PanelTestBase
    {
        private Reconciler _applying = null!;
        private Reconciler _nested = null!;

        public override void SetUp()
        {
            base.SetUp();
            _applying = new Reconciler();
            _nested = new Reconciler();
        }

        public override void TearDown()
        {
            ChildListWrites.EndRecording();
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
                ChildListWrites.EndRecording();
                liveInsideTheApply = target.childCount;
            });
            ChildListWrites.BeginRecording(_applying.Context.Pass);
            _applying.Context.HostCommit.Remove(host, first);

            // Act
            ChildListWrites.EndRecording();

            // Assert
            Assert.That(liveInsideTheApply, Is.EqualTo(1));
        }
    }
}
