using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that patching a label's text allocates nothing per label: retexting twice as many labels costs the
    /// same GC.Alloc blocks, so whatever a reconcile allocates once cancels out.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class RetextAllocationTests
    {
        private readonly List<Reconciler> _reconcilers = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var reconciler in _reconcilers)
            {
                reconciler.Dispose();
            }
            _reconcilers.Clear();
        }

        // Each run retexts every label and back, so the next run patches the tree the first mount built.
        private Action Retext(int count)
        {
            var reconciler = new Reconciler();
            _reconcilers.Add(reconciler);
            var root = new VisualElement();
            var first = Labels(count, "a");
            var second = Labels(count, "b");
            reconciler.Reconcile(root, Array.Empty<VNode>(), first);
            void Run()
            {
                reconciler.Reconcile(root, first, second);
                reconciler.Reconcile(root, second, first);
            }
            for (var i = 0; i < 8; i++)
            {
                Run();
            }
            return Run;
        }

        private static VNode[] Labels(int count, string text)
        {
            var labels = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                labels[i] = V.Label(text: text + i);
            }
            return labels;
        }

        [Test]
        public void Given_LabelsWhoseTextChanges_When_TwiceAsManyArePatched_Then_NothingMoreIsAllocated()
        {
            // Arrange
            var few = Retext(16);
            var many = Retext(32);

            // Act
            var extra = GCAllocationProbe.MedianBlocksDuring(many) - GCAllocationProbe.MedianBlocksDuring(few);

            // Assert — the canary rides along, because a zero says nothing if the probe measured nothing at all.
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? extra : -1, Is.Zero);
        }
    }
}
