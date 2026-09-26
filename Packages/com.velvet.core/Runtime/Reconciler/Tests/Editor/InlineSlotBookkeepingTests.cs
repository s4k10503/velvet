using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    /// <item>A walk that puts components under two parents in order leaves each parent's child chain holding
    /// its own children, and returns every list, set and bucket table it rented for that.</item>
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
            s_setTick = null;
            s_secondHolder = null;
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

            // Act — the context is marked disposed before the registry is, so each unmount's reconcile returns
            // before it can unregister the fibers below it one by one.
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

        private static Action<int> s_setTick;
        private static ComponentFiber s_secondHolder;

        [Component]
        private static VNode LeafARender() => V.Label(name: "a", key: "a");

        [Component]
        private static VNode LeafBRender() => V.Label(name: "b", key: "b");

        [Component]
        private static VNode FirstHolderRender()
            => V.Fragment(children: new VNode[] { V.Component(LeafARender, key: "a"), V.Component(LeafBRender, key: "b") });

        [Component]
        private static VNode SecondHolderRender()
        {
            s_secondHolder = FiberAmbientStack.Current;
            return V.Fragment(children: new VNode[] { V.Component(LeafARender, key: "a"), V.Component(LeafBRender, key: "b") });
        }

        // One walk places two components under each holder and the two holders under the host.
        [Component]
        private static VNode TwoHoldersHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "box-" + tick, children: new VNode[]
            {
                V.Component(FirstHolderRender, key: "first"),
                V.Component(SecondHolderRender, key: "second"),
            });
        }

        private static string ChildRenderNames(ComponentFiber parent)
        {
            var names = new List<string>();
            for (var child = parent.Child; child != null && names.Count < 8; child = child.Sibling)
            {
                names.Add(Hooks.ComponentName(child));
            }
            return string.Join(",", names);
        }

        private static IEnumerable PoolOf(ReconcilerBufferPool pool, string field)
        {
            var clearable = typeof(ReconcilerBufferPool)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pool);
            return (IEnumerable)clearable.GetType()
                .GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(clearable);
        }

        private static int Count(IEnumerable items)
        {
            var count = 0;
            foreach (var _ in items) count++;
            return count;
        }

        // Fills each pool to its cap, so a render that rents from one and returns less leaves it short.
        private static void FillPools(ReconcilerBufferPool pool)
        {
            for (var i = 0; i < 16; i++)
            {
                pool.ReturnFiberList(new List<ComponentFiber>());
                pool.ReturnFiberSet(new HashSet<ComponentFiber>());
                pool.ReturnFiberBuckets(new Dictionary<ComponentFiber, List<ComponentFiber>>());
            }
        }

        [Test]
        public void Given_AWalkPlacingTwoComponentsUnderEachOfTwoHolders_When_ItCommits_Then_TheSecondHoldersChainHoldsItsOwnChildren()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoHoldersHostRender, key: "host"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ChildRenderNames(s_secondHolder), Is.EqualTo("InlineSlotBookkeepingTests.LeafARender,InlineSlotBookkeepingTests.LeafBRender"));
        }

        [TestCase("_fiberListPool")]
        [TestCase("_fiberSetPool")]
        [TestCase("_fiberBucketsPool")]
        public void Given_FullPools_When_AWalkPutsComponentsUnderTwoHoldersInOrder_Then_ThePoolIsStillFull(string field)
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoHoldersHostRender, key: "host"));
            var pool = mounted.Root.Reconciler.Context.BufferPool;
            FillPools(pool);
            var full = Count(PoolOf(pool, field));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — full is folded in: a pool that never filled could not come back short.
            Assert.That((full, Count(PoolOf(pool, field))), Is.EqualTo((8, 8)));
        }
    }
}
