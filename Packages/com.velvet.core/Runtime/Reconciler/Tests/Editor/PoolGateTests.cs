using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pool gate: while it is raised a row a pass removes stays out of its pool, and the row is pooled
    /// where the last raise falls, unless the context that removed it has been disposed by then.
    /// </summary>
    [TestFixture]
    internal sealed class PoolGateTests
    {
        private int _raised;

        [SetUp]
        public void SetUp()
        {
            _raised = 0;
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        [TearDown]
        public void TearDown()
        {
            while (_raised > 0) Lower();
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        private void Raise()
        {
            FiberElementCleaner.RaisePoolGate();
            _raised++;
        }

        private void Lower()
        {
            _raised--;
            FiberElementCleaner.LowerPoolGate();
        }

        private static VNode Branch(string name, params VNode[] children)
            => V.Div(key: name, name: name, children: children);

        // Mounts a left branch holding one label and removes the label in a second pass.
        private static (VisualElement Left, Label Removed) RemoveTheLabel(Reconciler reconciler, VisualElement root)
        {
            var oldTree = new[] { Branch("left", V.Text("old")) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var left = root.Q<VisualElement>("left");
            var removed = (Label)left[0];
            reconciler.Reconcile(root, oldTree, new[] { Branch("left") });
            return (left, removed);
        }

        [Test]
        public void Given_TheGateRaised_When_APassRemovesALabel_Then_NothingIsPooled()
        {
            // Arrange
            using var reconciler = new ReconcilerHandle();
            Raise();

            // Act
            RemoveTheLabel(reconciler.Value, reconciler.Root);

            // Assert
            Assert.That(VNodePoolTestAccess.LabelPoolCountForTest, Is.Zero);
        }

        [Test]
        public void Given_ALabelRemovedWhileTheGateWasRaised_When_TheGateFalls_Then_ItIsTheNextOneRented()
        {
            // Arrange
            using var reconciler = new ReconcilerHandle();
            Raise();
            var (_, removed) = RemoveTheLabel(reconciler.Value, reconciler.Root);

            // Act
            Lower();
            var rented = VNodePool.RentLabel("next");

            // Assert
            Assert.That(ReferenceEquals(rented, removed), Is.True);
        }

        [Test]
        public void Given_ALabelRemovedWhileTheGateWasRaisedTwice_When_OneRaiseFalls_Then_NothingIsPooled()
        {
            // Arrange
            using var reconciler = new ReconcilerHandle();
            Raise();
            Raise();
            RemoveTheLabel(reconciler.Value, reconciler.Root);

            // Act
            Lower();

            // Assert
            Assert.That(VNodePoolTestAccess.LabelPoolCountForTest, Is.Zero);
        }

        [Test]
        public void Given_ALabelHeldByTheGate_When_ItsReconcilerIsDisposedAndTheGateFalls_Then_NothingIsPooled()
        {
            // Arrange
            var reconciler = new ReconcilerHandle();
            Raise();
            RemoveTheLabel(reconciler.Value, reconciler.Root);
            reconciler.Dispose();

            // Act
            Lower();

            // Assert
            Assert.That(VNodePoolTestAccess.LabelPoolCountForTest, Is.Zero);
        }

        private sealed class ReconcilerHandle : IDisposable
        {
            private bool _disposed;

            public Reconciler Value { get; } = new();
            public VisualElement Root { get; } = new();

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                ChildListWrites.EndRecording();
                Value.Dispose();
            }
        }
    }
}
