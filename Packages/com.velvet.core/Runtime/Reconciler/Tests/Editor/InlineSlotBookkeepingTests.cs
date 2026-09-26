using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the lifetime of the bookkeeping behind an inline component's recorded slot start.
    /// <list type="bullet">
    /// <item>A container's tenancy holds the components mounted on it and lets go of one that unmounts;
    /// once the last one has gone, and once the reconciler owning the registry is disposed, the container has
    /// none.</item>
    /// <item>The list a walk records its placements in goes back to <see cref="ReconcilerBufferPool"/>
    /// when the walk ends, so a reconcile does not leave one behind for the collector on every pass.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class InlineSlotBookkeepingTests
    {
        private VisualElement _root;
        private static ComponentFiber s_kept;
        private static Action<int> s_setShown;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_kept = null;
            s_setShown = null;
        }

        [Component]
        private static VNode LeavingRender() => V.Label(name: "leaving", key: "leaving");

        [Component]
        private static VNode KeptRender()
        {
            s_kept = FiberAmbientStack.Current;
            return V.Label(name: "kept", key: "kept");
        }

        [Component]
        private static VNode OneLeavesOneStaysHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShown = setShown;
            return V.Div(name: "box", children: new VNode[]
            {
                shown == 1 ? V.Component(LeavingRender, key: "leaving") : null,
                V.Component(KeptRender, key: "kept"),
            });
        }

        [Component]
        private static VNode LastOneLeavesHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShown = setShown;
            return V.Div(name: "box", children: new VNode[]
            {
                shown == 1 ? V.Component(LeavingRender, key: "leaving") : null,
                V.Label(name: "row", key: "row"),
            });
        }

        [Component]
        private static VNode OneComponentHostRender()
            => V.Div(name: "box", children: new VNode[] { V.Component(KeptRender, key: "kept") });

        private static InlineTenancy TenancyOf(MountedTree mounted, VisualElement container)
            => mounted.Root.Reconciler.Context.ComponentRegistry.TenancyOf(container);

        [Test]
        public void Given_OneOfTwoComponentsOnAContainerUnmounts_When_ItsTenancyIsRead_Then_ItHoldsOnlyTheOther()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OneLeavesOneStaysHostRender, key: "host"));
            var box = _root.Q(name: "box");

            // Act
            s_setShown.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(TenancyOf(mounted, box)?.Fibers, Is.EquivalentTo(new[] { s_kept }));
        }

        [Test]
        public void Given_TheLastComponentOnAContainerUnmounts_When_ItsTenancyIsRead_Then_ThereIsNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(LastOneLeavesHostRender, key: "host"));
            var box = _root.Q(name: "box");

            // Act
            s_setShown.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(TenancyOf(mounted, box), Is.Null);
        }

        [Test]
        public void Given_ARootReconcilerDisposed_When_AContainersTenancyIsRead_Then_ThereIsNone()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OneComponentHostRender, key: "host"));
            var box = _root.Q(name: "box");

            // Act — the context is marked disposed before the registry is, so no unmount reconcile runs to
            // unregister the fibers one by one.
            mounted.Root.Reconciler.Dispose();

            // Assert
            Assert.That(TenancyOf(mounted, box), Is.Null);
        }

        [Test]
        public void Given_AWalkThatPlacedAComponent_When_ItEnds_Then_ItsPlacementListIsBackInThePool()
        {
            // Arrange — mount once so the pool holds the list that walk rented, take that one out and put it
            // back with a mark on it. Rent and Return both clear, so the mark survives only while nothing
            // takes the list, which is what separates a walk that returned it from no walk at all.
            var reconciler = new Reconciler();
            try
            {
                var pool = reconciler.Context.BufferPool;
                var mountedTree = new VNode[] { V.Component(KeptRender, key: "kept") };
                reconciler.Reconcile(_root, Array.Empty<VNode>(), mountedTree);
                var borrowed = pool.RentPlacementList();
                pool.ReturnPlacementList(borrowed);
                borrowed.Add((new ComponentFiber(), 0, 0));

                // Act — a walk over a component, which is what records a placement.
                reconciler.Reconcile(_root, mountedTree, new VNode[] { V.Component(KeptRender, key: "kept") });
                var left = borrowed.Count;

                // Assert — nothing is left in it, and it is what the next rent hands out.
                Assert.That(
                    (ReferenceEquals(pool.RentPlacementList(), borrowed), left),
                    Is.EqualTo((true, 0)));
            }
            finally
            {
                reconciler.Dispose();
            }
        }
    }
}
