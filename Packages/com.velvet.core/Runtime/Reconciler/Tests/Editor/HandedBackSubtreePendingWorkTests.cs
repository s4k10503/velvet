using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a host element the parent hands back by instance is still entered where a component inside it
    /// has work pending, as React enters a subtree whose childLanes are set, that it is left unentered however many
    /// fibers it holds where none has, and that the mark standing for that is clear once the work has settled, so an
    /// unchanged subtree costs the walk one read.
    /// </summary>
    [TestFixture]
    internal sealed class HandedBackSubtreePendingWorkTests
    {
        private static StateUpdater<int> s_setOrder;
        private static StateUpdater<int> s_setReaderKey;
        private static StateUpdater<int> s_setLeafTick;
        private static VelvetTaskCompletionSource<int> s_source;
        private static ComponentFiber s_ownerFiber;
        private static int s_heldLeaves;
        private static int s_heldLeafRenders;

        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setOrder = default;
            s_setReaderKey = default;
            s_setLeafTick = default;
            s_source = null;
            s_ownerFiber = null;
            s_heldLeaves = 0;
            s_heldLeafRenders = 0;
        }

        private static object PendingWorkBelow(ComponentFiber fiber)
            => typeof(ComponentFiber)
                .GetProperty("PendingWorkBelow", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(fiber);

        [Component(Compiler = false)]
        private static VNode Reader()
        {
            var (key, setKey) = Hooks.UseState(0);
            s_setReaderKey = setKey;
            var value = Hooks.Use<int>(_ => key == 0
                ? VelvetTask.FromResult(0)
                : (s_source = new VelvetTaskCompletionSource<int>()).Task, key);
            return V.Label(name: "reader", text: "value:" + value);
        }

        // The two keyed rows are built ahead of the order, so the compiler hands both back while the order moves them.
        [Component]
        private static VNode ReorderingOwner()
        {
            s_ownerFiber = FiberAmbientStack.Current;
            var (order, setOrder) = Hooks.UseState(0);
            s_setOrder = setOrder;
            var a = V.Div(name: "a", key: "a", children: new VNode[] { V.Component(Reader, key: "reader") });
            var b = V.Div(name: "b", key: "b");
            return V.Suspense(V.Label(name: "loading", text: "loading"), new VNode[]
            {
                V.Div(name: "list", children: order == 0 ? new VNode[] { a, b } : new VNode[] { b, a }),
            });
        }

        [Component(Compiler = false)]
        private static VNode Leaf()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setLeafTick = setTick;
            return V.Label(name: "leaf", text: "leaf:" + tick);
        }

        [Component]
        private static VNode SettlingOwner()
        {
            s_ownerFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setOrder = setTick;
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(name: "tick", text: "tick:" + tick),
                V.Div(name: "held", children: new VNode[] { V.Component(Leaf, key: "leaf") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode HeldLeaf()
        {
            s_heldLeafRenders++;
            return V.Label(text: "leaf");
        }

        [Component(Compiler = false)]
        private static VNode HeldLeaves()
        {
            var children = new VNode[s_heldLeaves];
            for (var i = 0; i < children.Length; i++)
            {
                children[i] = V.Component(HeldLeaf, key: "leaf" + i);
            }
            return V.Div(name: "leaves", children: children);
        }

        [Component]
        private static VNode ManyFiberOwner()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOrder = setTick;
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(name: "tick", text: "tick:" + tick),
                V.Div(name: "held", children: new VNode[] { V.Component(HeldLeaves, key: "leaves") }),
            });
        }

        // GREEN_ON_BASE(characterization): the merge base builds the row again on every render, so the reader is reached
        // there whatever the keyed pass does with a row handed back by instance.
        [Test]
        public void Given_AKeyedRowHoldingAWaitingReader_When_TheOwnerReordersItUnderASuspense_Then_TheFallbackStays()
        {
            // Arrange — the reader's own pass suspends, and the owner's render it asks for shows the fallback
            using var mounted = V.Mount(_root, V.Component(ReorderingOwner, key: "owner"));
            s_setReaderKey.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Act — the owner's pass alone, so a render of the reader asked for elsewhere cannot hide what it revealed
            s_setOrder.Invoke(1);
            FiberWorkLoop.FlushState(s_ownerFiber);

            // Assert
            Assert.That(_root.Q<Label>("loading")?.text, Is.EqualTo("loading"));
        }

        [TestCase(200)]
        [TestCase(400)]
        public void Given_AnElementHandedBackOverManyFibers_When_TheOwnerRendersAgain_Then_NoneOfThemRendersAgain(int leaves)
        {
            // Arrange
            s_heldLeaves = leaves;
            using var mounted = V.Mount(_root, V.Component(ManyFiberOwner, key: "owner"));

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_heldLeafRenders, Is.EqualTo(leaves));
        }

        // Read by reflection so the fixture builds where the mark does not exist.
        [Test]
        public void Given_ALeafUpdateThatHasRendered_When_TheOwnerRendersAgain_Then_NoPendingWorkIsMarkedBelowIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SettlingOwner, key: "owner"));
            s_setLeafTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(PendingWorkBelow(s_ownerFiber), Is.EqualTo(false));
        }
    }
}
