using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what happens to an already-mounted <c>V.Portal(targetId:)</c> when its id is registered
    /// again with a DIFFERENT element.
    /// <list type="bullet">
    /// <item>The children follow the id: they leave the element they were mounted into and appear in the
    /// replacement, after whatever that element already held.</item>
    /// <item>They arrive as a remount, not a reparent — the same route the element-valued form takes.</item>
    /// <item>The move is driven by the registration itself, without the declaring component having any
    /// reason of its own to re-render.</item>
    /// <item>The synthetic-bubbling bridge follows: the replaced element loses it unless another live
    /// portal still resolves to it.</item>
    /// <item>A portal whose slot range sat after the departing one on the replaced element keeps
    /// addressing its own children.</item>
    /// </list>
    /// Re-registering the SAME element, and unregistering the id outright, both leave a live portal alone —
    /// <see cref="PortalTests"/> owns the unregistration case.
    /// </summary>
    internal sealed class PortalRegistryRetargetTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _reconciler.Dispose();
            RuntimeStateProbe.ClearPortalRegistry();
        }

        // A Div rather than a Text: a Text mounts a pooled Label, and a remount returns it to a LIFO pool
        // that the very next rent pops, so an identity reading cannot tell a patch from a remount there.
        private static VNode[] Tree(string id, params string[] childNames) =>
            new VNode[]
            {
                V.Portal(id, children: childNames.Select(n => (VNode?)V.Div(name: n)).ToArray()),
            };

        private static void ExpectOverwriteWarning(string id) =>
            LogAssert.Expect(LogType.Warning, $"[FiberPortalRegistry] Id \"{id}\" is already registered. Overwriting.");

        private static string Names(VisualElement element) =>
            string.Join("|", element.Children().Select(c => c.name));

        [Test]
        public void Given_AMountedRegistryPortal_When_ItsIdIsReRegistered_Then_ThePatchMovesTheChildren()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("modal-root", original);
            var tree = Tree("modal-root", "content");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: the children mounted into the original");
            ExpectOverwriteWarning("modal-root");
            FiberPortalRegistry.Register("modal-root", replacement);

            // Act
            _reconciler.Reconcile(_root, tree, Tree("modal-root", "content"));

            // Assert
            Assert.That((original.childCount, Names(replacement)), Is.EqualTo((0, "content")));
        }

        [Test]
        public void Given_AMountedRegistryPortal_When_ItsIdIsReRegistered_Then_TheChildrenRemountRatherThanReparent()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("modal-root", original);
            var tree = Tree("modal-root", "content");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            var mountedElement = original.ElementAt(0);
            ExpectOverwriteWarning("modal-root");
            FiberPortalRegistry.Register("modal-root", replacement);

            // Act
            _reconciler.Reconcile(_root, tree, Tree("modal-root", "content"));

            // Assert — the count is folded in because an empty replacement satisfies the identity reading
            // on its own.
            var arrived = replacement.childCount == 1 ? replacement.ElementAt(0) : null;
            Assert.That((replacement.childCount, ReferenceEquals(arrived, mountedElement)),
                Is.EqualTo((1, false)));
        }

        [Test]
        public void Given_AReplacementHoldingItsOwnChildren_When_ThePortalFollows_Then_ItAppendsAfterThem()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            replacement.Add(new VisualElement { name = "caller-owned" });
            FiberPortalRegistry.Register("modal-root", original);
            var tree = Tree("modal-root", "content");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            ExpectOverwriteWarning("modal-root");
            FiberPortalRegistry.Register("modal-root", replacement);

            // Act
            _reconciler.Reconcile(_root, tree, Tree("modal-root", "content"));

            // Assert
            Assert.That(Names(replacement), Is.EqualTo("caller-owned|content"));
        }

        [Test]
        public void Given_ASecondPortalOnTheReplacedElement_When_TheFirstFollowsItsId_Then_ItsRangeStillAddressesItsOwnChildren()
        {
            // Arrange — two ids resolve to one element, so the survivor's slot range sits after the
            // departing one's and must collapse left when that range leaves.
            var shared = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("leaving", shared);
            FiberPortalRegistry.Register("staying", shared);
            var tree = new VNode[]
            {
                V.Portal("leaving", children: new VNode?[] { V.Div(name: "gone") }),
                V.Portal("staying", children: new VNode?[] { V.Div(name: "kept") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            ExpectOverwriteWarning("leaving");
            FiberPortalRegistry.Register("leaving", replacement);

            // Act — the survivor grows in the same pass that moves its neighbour off the element.
            _reconciler.Reconcile(_root, tree, new VNode[]
            {
                V.Portal("leaving", children: new VNode?[] { V.Div(name: "gone") }),
                V.Portal("staying", children: new VNode?[] { V.Div(name: "kept"), V.Div(name: "grown") }),
            });

            // Assert
            Assert.That(Names(shared), Is.EqualTo("kept|grown"));
        }

        [Test]
        public void Given_TheOnlyPortalOnAnElement_When_ItFollowsItsId_Then_TheReplacedElementLeavesTheBridgeTable()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("modal-root", original);
            var tree = Tree("modal-root", "content");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            ExpectOverwriteWarning("modal-root");
            FiberPortalRegistry.Register("modal-root", replacement);

            // Act
            _reconciler.Reconcile(_root, tree, Tree("modal-root", "content"));

            // Assert — the replacement's own entry is folded in, since a table that never gained one
            // satisfies the release reading on its own.
            var bridges = _reconciler.Context.SamePanelPortalBridges;
            Assert.That((bridges.ContainsKey(original), bridges.ContainsKey(replacement)),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ASecondPortalOnTheReplacedElement_When_TheFirstFollowsItsId_Then_TheBridgeStays()
        {
            // Arrange
            var shared = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("leaving", shared);
            FiberPortalRegistry.Register("staying", shared);
            var tree = new VNode[]
            {
                V.Portal("leaving", children: new VNode?[] { V.Div(name: "gone") }),
                V.Portal("staying", children: new VNode?[] { V.Div(name: "kept") }),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            ExpectOverwriteWarning("leaving");
            FiberPortalRegistry.Register("leaving", replacement);

            // Act — a fresh array of equivalent nodes, because the reconciler skips a reference-identical
            // VNode outright and neither portal would be patched at all.
            _reconciler.Reconcile(_root, tree, new VNode[]
            {
                V.Portal("leaving", children: new VNode?[] { V.Div(name: "gone") }),
                V.Portal("staying", children: new VNode?[] { V.Div(name: "kept") }),
            });

            // Assert — releasing the shared element's bridge here would strand the portal still mounted
            // into it with no logical-chain delivery.
            Assert.That(_reconciler.Context.SamePanelPortalBridges.ContainsKey(shared), Is.True);
        }

        [Component]
        private static VNode SteadyPortalHost() =>
            V.Portal("notify-target", children: new VNode?[] { V.Div(name: "content") });

        [Test]
        public void Given_AHostWithNoReasonToReRender_When_TheIdIsReRegistered_Then_TheChildrenStillFollow()
        {
            // Arrange — the declaring component has no state and no props, so nothing in a render observes
            // the registry: the resolution happens at mount and the swap has to reach the portal some
            // other way.
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("notify-target", original);
            _mounted = V.Mount(new VisualElement(), V.Component(SteadyPortalHost, key: "host"));
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: the children mounted into the original");
            ExpectOverwriteWarning("notify-target");

            // Act
            FiberPortalRegistry.Register("notify-target", replacement);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((original.childCount, Names(replacement)), Is.EqualTo((0, "content")));
        }

        [Test]
        public void Given_TheIdUnregisteredFirst_When_AReplacementRegisters_Then_TheChildrenStillFollow()
        {
            // Arrange — a screen that unregisters as it tears down leaves the registry with no previous
            // element to compare the next registration against, while the portal still holds one.
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("notify-target", original);
            _mounted = V.Mount(new VisualElement(), V.Component(SteadyPortalHost, key: "host"));
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: the children mounted into the original");
            FiberPortalRegistry.Unregister("notify-target");

            // Act
            FiberPortalRegistry.Register("notify-target", replacement);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((original.childCount, Names(replacement)), Is.EqualTo((0, "content")));
        }

        // A captured UseState value is a shape the auto-memoization weaver can key a cache on, so a
        // re-render with that value unchanged is served from the cache — and the reconciler skips a
        // reference-identical VNode without ever patching it. SteadyPortalHost above is the opposite
        // shape: with no hook to key on, the weaver leaves the body alone.
        [Component]
        private static VNode MemoizedPortalHost()
        {
            var (label, _) = Hooks.UseState("content");
            return V.Portal("notify-target", children: new VNode?[] { V.Div(name: label) });
        }

        [Test]
        public void Given_AnAutoMemoizedHost_When_TheIdIsReRegistered_Then_TheChildrenStillFollow()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("notify-target", original);
            _mounted = V.Mount(new VisualElement(), V.Component(MemoizedPortalHost, key: "host"));
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: the children mounted into the original");
            ExpectOverwriteWarning("notify-target");

            // Act
            FiberPortalRegistry.Register("notify-target", replacement);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((original.childCount, Names(replacement)), Is.EqualTo((0, "content")));
        }

        private static StateUpdater<bool> s_setGrown;

        [Component]
        private static VNode GrowingPortalHost()
        {
            var (grown, setGrown) = Hooks.UseState(false);
            s_setGrown = setGrown;
            return V.Portal("notify-target", children: grown
                ? new VNode?[] { V.Div(name: "one"), V.Div(name: "two") }
                : new VNode?[] { V.Div(name: "one") });
        }

        [Test]
        public void Given_APortalThatGrowsAfterTheReRegistration_When_ItPatches_Then_ItsWholeRangeIsOnTheReplacement()
        {
            // Arrange — a growth landing while the portal still addressed the old element would split
            // the range across both.
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("notify-target", original);
            _mounted = V.Mount(new VisualElement(), V.Component(GrowingPortalHost, key: "host"));
            Assume.That(original.childCount, Is.EqualTo(1), "Precondition: the child mounted into the original");
            ExpectOverwriteWarning("notify-target");
            FiberPortalRegistry.Register("notify-target", replacement);

            // Act
            s_setGrown.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That((original.childCount, Names(replacement)), Is.EqualTo((0, "one|two")));
        }
    }
}
