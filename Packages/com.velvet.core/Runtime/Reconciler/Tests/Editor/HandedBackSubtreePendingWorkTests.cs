using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a host element the parent hands back by instance is entered. While a fiber below the owner has
    /// work pending, as React enters a subtree whose childLanes are set, every such element is entered except one
    /// built of nothing but elements and text, which no fiber can render. Once that work has settled the mark standing
    /// for it is clear, and no such element is entered however many fibers it holds.
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
        private static ComponentFiber s_heldLeavesFiber;

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
            s_heldLeavesFiber = null;
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
        private static VNode HeldLeaf() => V.Label(text: "leaf");

        [Component(Compiler = false)]
        private static VNode HeldLeaves()
        {
            s_heldLeavesFiber = FiberAmbientStack.Current;
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

        [Component]
        private static VNode PanelBesideAReaderOwner()
        {
            s_ownerFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setOrder = setTick;
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(name: "tick", text: "tick:" + tick),
                V.Div(name: "panel", children: new VNode[] { V.Text("one"), null, V.Div(children: new VNode[] { V.Label(text: "two") }) }),
                V.Div(name: "reading", children: new VNode[]
                {
                    V.Suspense(V.Label(name: "loading", text: "loading"), new VNode[] { V.Component(Reader, key: "reader") }),
                }),
            });
        }

        // Built once, so every render hands the same instance back; it has no children array at all.
        private static readonly ElementNode s_bareElement = new() { Name = "bare", Children = null };

        [Component]
        private static VNode BareElementBesideAReaderOwner()
        {
            s_ownerFiber = FiberAmbientStack.Current;
            var (tick, setTick) = Hooks.UseState(0);
            s_setOrder = setTick;
            return V.Div(name: "box", children: new VNode[]
            {
                V.Label(name: "tick", text: "tick:" + tick),
                s_bareElement,
                V.Div(name: "reading", children: new VNode[]
                {
                    V.Suspense(V.Label(name: "loading", text: "loading"), new VNode[] { V.Component(Reader, key: "reader") }),
                }),
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
        public void Given_AnElementHandedBackOverManyFibers_When_TheOwnerRendersAgain_Then_NothingInItIsReached(int leaves)
        {
            // Arrange
            s_heldLeaves = leaves;
            using var mounted = V.Mount(_root, V.Component(ManyFiberOwner, key: "owner"));
            var treeBefore = s_heldLeavesFiber.SourceTree;

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — a walk entering the held element reaches the component in it, which then names this render's
            // tree; a component that bails on the node it was handed is reached all the same
            Assert.That(s_heldLeavesFiber.SourceTree, Is.SameAs((object)treeBefore ?? new object()));
        }

        // Read by reflection so the fixture builds where the mark does not exist. A patch writes the node's name back
        // onto the element it enters, so a name set on the element itself survives only where it is not entered.
        [Test]
        public void Given_AHostPanelBesideAWaitingReader_When_TheOwnerRendersAgain_Then_ThePanelIsNotEntered()
        {
            // Arrange — the reader waits, so the owner stays marked through its render
            using var mounted = V.Mount(_root, V.Component(PanelBesideAReaderOwner, key: "owner"));
            s_setReaderKey.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
            var panel = _root.Q("panel");
            panel.name = "set-on-the-element";

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the mark travels with the name: an owner with nothing marked would skip the panel anyway
            Assert.That(Equals(PendingWorkBelow(s_ownerFiber), true) ? panel.name : null, Is.EqualTo("set-on-the-element"));
        }

        [Test]
        public void Given_AnElementWithNoChildrenArrayBesideAWaitingReader_When_TheOwnerRendersAgain_Then_TheRenderCommits()
        {
            // Arrange — the reader waits, so the owner stays marked and asks the bare element whether it holds a fiber
            using var mounted = V.Mount(_root, V.Component(BareElementBesideAReaderOwner, key: "owner"));
            s_setReaderKey.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the mark travels with the text: an owner with nothing marked would not ask
            Assert.That(Equals(PendingWorkBelow(s_ownerFiber), true) ? _root.Q<Label>("tick")?.text : null,
                Is.EqualTo("tick:1"));
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
