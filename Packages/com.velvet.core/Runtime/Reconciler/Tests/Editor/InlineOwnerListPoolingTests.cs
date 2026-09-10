// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the list a container's old-side expansion fills with the fiber each leaf was emitted
    /// under goes back to <see cref="ReconcilerBufferPool"/> when the pass that rented it ends, so a
    /// reconcile does not leave one behind for the collector on every pass.
    /// </summary>
    [TestFixture]
    internal sealed class InlineOwnerListPoolingTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
            _reconciler = null!;
            _root = null!;
        }

        [Test]
        public void Given_APassThatFilledTheOwnerList_When_ItEnds_Then_TheListIsBackInThePool()
        {
            // Arrange — mount once so the pool holds the list that pass rented, take that one out and put
            // it back with a mark on it. Rent and Return both clear, so the mark survives only while
            // nothing takes the list, which is what separates a pass that returned it from no pass at all.
            var pool = _reconciler.Context.BufferPool;
            var mounted = new VNode[] { V.Label(text: "a") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), mounted);
            var borrowed = pool.RentFiberOwnerList();
            pool.ReturnFiberOwnerList(borrowed);
            borrowed.Add(null);

            // Act — a pass with an old side to expand, which is what puts an owner in the list.
            _reconciler.Reconcile(_root, mounted, new VNode[] { V.Label(text: "b") });
            var left = borrowed.Count;

            // Assert — nothing is left in it, and it is what the next rent hands out.
            Assert.That(
                (ReferenceEquals(pool.RentFiberOwnerList(), borrowed), left),
                Is.EqualTo((true, 0)));
        }
    }
}
