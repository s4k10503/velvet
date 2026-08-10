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
    /// Specifies what a registration on a <c>V.Portal(targetId:)</c>'s id does to the portals already
    /// mounted on it — both entrances to <c>FiberNodePatcher.ResolvePortalTarget</c>'s shared mounting
    /// tail: an id registered again with a DIFFERENT element, and an id registered for the first time
    /// under a portal that mounted while it was unregistered.
    /// <list type="bullet">
    /// <item>The children follow the id: they leave the element they were mounted into and appear in the
    /// replacement, after whatever that element already held.</item>
    /// <item>They arrive as a remount, not a reparent — the same route the element-valued form takes.</item>
    /// <item>The move is driven by the registration itself, without the declaring component having any
    /// reason of its own to re-render — including the first registration, which a portal that warned at
    /// mount has nothing else to wake it.</item>
    /// <item>Either entrance addresses its slot range from the end of the target's own children rather
    /// than from slot 0, so the container keeps them while the portal is mounted and after it goes.</item>
    /// <item>The synthetic-bubbling bridge follows: the replaced element loses it unless another live
    /// portal still resolves to it.</item>
    /// <item>A portal whose slot range sat after the departing one on the replaced element keeps
    /// addressing its own children.</item>
    /// <item>A <c>V.Component</c> written directly under the portal is disposed when the children leave
    /// the container — the element-sibling case the containment-based fiber sweep cannot reach.</item>
    /// <item>That disposal follows the portal the component was written under: on a container two
    /// portals share, whichever closes takes its own component and leaves the other's alone, in both
    /// orders and after either range has moved the other. A component rendering nothing at all is
    /// disposed on the same terms — as the portal's only child, and as a trailing one adding no slot
    /// to a range its siblings gave.</item>
    /// <item>A component that leaves the portal's children for a position outside it is disposed with
    /// them there and then, and the portal closing later takes nothing further — the limit that makes
    /// the placeholder stamp safe to write once.</item>
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
            s_siblingRegistered = null;
            s_childCleanups = 0;
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

        [Test]
        public void Given_ATargetRegisteredAfterTheMountHoldingItsOwnChildren_When_ThePortalHeals_Then_ItAppendsAfterThem()
        {
            // Arrange — the other entrance to the same tail: a mount while the id was unregistered records
            // no target at all, so the first patch after the registration is where its slot range has to be
            // addressed at the end of whatever the container already holds.
            var target = new VisualElement();
            target.Add(new Label { name = "backdrop" });
            var tree = Tree("modal-root", "modal");
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"modal-root\" is not registered. Children will not be rendered.");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            FiberPortalRegistry.Register("modal-root", target);

            // Act
            _reconciler.Reconcile(_root, tree, Tree("modal-root", "modal"));

            // Assert
            Assert.That(Names(target), Is.EqualTo("backdrop|modal"));
        }

        [Test]
        public void Given_APortalHealedOntoATargetHoldingItsOwnChildren_When_ItUnmounts_Then_TheContainerKeepsThem()
        {
            // Arrange — the unmount tears out the range the heal recorded, so a heal that patched its
            // content onto the container's own child instead of appending leaves that child behind,
            // carrying the portal's content, with nothing recorded to remove.
            var target = new VisualElement();
            target.Add(new Label { name = "backdrop" });
            var tree = Tree("modal-root", "modal");
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"modal-root\" is not registered. Children will not be rendered.");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), tree);
            FiberPortalRegistry.Register("modal-root", target);
            var healed = Tree("modal-root", "modal");
            _reconciler.Reconcile(_root, tree, healed);

            // Act
            _reconciler.Reconcile(_root, healed, Array.Empty<VNode>());

            // Assert
            Assert.That(Names(target), Is.EqualTo("backdrop"));
        }

        private static VisualElement? s_siblingRegistered;

        [Component]
        private static VNode SiblingRegistersHost() =>
            V.Div(children: new VNode?[]
            {
                V.Portal("notify-target", children: new VNode?[] { V.Div(name: "content") }),
                V.Div(refCallback: el =>
                {
                    s_siblingRegistered = el;
                    FiberPortalRegistry.Register("notify-target", el);
                    return () => FiberPortalRegistry.Unregister("notify-target");
                }),
            });

        [Test]
        public void Given_APortalMountedBeforeItsIdExisted_When_ASiblingRefCallbackRegistersIt_Then_TheChildrenStillFollow()
        {
            // Arrange — CreateElement reaches the portal before the sibling, so the mount warns and records
            // no target; the registration the sibling's ref then makes is the only thing that can reach it.
            LogAssert.Expect(LogType.Warning,
                "[Portal] Target \"notify-target\" is not registered. Children will not be rendered.");

            // Act
            _mounted = V.Mount(new VisualElement(), V.Component(SiblingRegistersHost, key: "host"));
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_siblingRegistered == null ? "<never registered>" : Names(s_siblingRegistered),
                Is.EqualTo("content"));
        }

        private static int s_childCleanups;

        [Component]
        private static VNode PortalChildWithEffect()
        {
            Hooks.UseEffect(() => (Action)(() => s_childCleanups++), Array.Empty<object>());
            return V.Div(name: "content");
        }

        [Component]
        private static VNode ComponentChildPortalHost() =>
            V.Portal("notify-target", children: new VNode?[] { V.Component(PortalChildWithEffect, key: "c") });

        [Test]
        public void Given_ATopLevelComponentChild_When_TheIdIsReRegistered_Then_ItsEffectCleanupRuns()
        {
            // Arrange
            var original = new VisualElement();
            var replacement = new VisualElement();
            FiberPortalRegistry.Register("notify-target", original);
            _mounted = V.Mount(new VisualElement(), V.Component(ComponentChildPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_childCleanups = 0;
            ExpectOverwriteWarning("notify-target");

            // Act
            FiberPortalRegistry.Register("notify-target", replacement);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childCleanups, Is.EqualTo(1));
        }

        private static StateUpdater<bool> s_setDropped;

        [Component]
        private static VNode DroppablePortalHost()
        {
            var (dropped, setDropped) = Hooks.UseState(false);
            s_setDropped = setDropped;
            return V.Div(children: new VNode?[]
            {
                dropped
                    ? null
                    : V.Portal("notify-target",
                        children: new VNode?[] { V.Component(PortalChildWithEffect, key: "c") }),
            });
        }

        [Test]
        public void Given_ATopLevelComponentChild_When_ThePortalUnmounts_Then_ItsEffectCleanupRuns()
        {
            // Arrange — the same disposal the move relies on, reached by the ordinary teardown: this one
            // needs no registration at all and is what a closing modal runs.
            var target = new VisualElement();
            FiberPortalRegistry.Register("notify-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(DroppablePortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_childCleanups = 0;

            // Act
            s_setDropped.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childCleanups, Is.EqualTo(1));
        }

        [Component]
        private static VNode PortalChildRenderingNothing()
        {
            Hooks.UseEffect(() => (Action)(() => s_childCleanups++), Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        private static StateUpdater<bool> s_setEmptyDropped;

        [Component]
        private static VNode EmptyChildPortalHost()
        {
            var (dropped, setDropped) = Hooks.UseState(false);
            s_setEmptyDropped = setDropped;
            return V.Div(children: new VNode?[]
            {
                dropped
                    ? null
                    : V.Portal("notify-target",
                        children: new VNode?[] { V.Component(PortalChildRenderingNothing, key: "c") }),
            });
        }

        [Test]
        public void Given_APortalWhoseOnlyChildRendersNothing_When_ItUnmounts_Then_ThatChildIsStillDisposed()
        {
            // Arrange — a component between two states of its own is an ordinary shape, and this one leaves
            // the portal occupying no slots at all: a teardown that decides what to dispose from the size of
            // the range it is tearing out has nothing to select on.
            var target = new VisualElement();
            FiberPortalRegistry.Register("notify-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(EmptyChildPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_childCleanups = 0;

            // Act
            s_setEmptyDropped.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childCleanups, Is.EqualTo(1));
        }

        private static StateUpdater<bool> s_setTrailingDropped;

        [Component]
        private static VNode TrailingEmptyChildPortalHost()
        {
            var (dropped, setDropped) = Hooks.UseState(false);
            s_setTrailingDropped = setDropped;
            return V.Div(children: new VNode?[]
            {
                dropped
                    ? null
                    : V.Portal("notify-target", children: new VNode?[]
                    {
                        V.Div(name: "content"),
                        V.Component(PortalChildRenderingNothing, key: "c"),
                    }),
            });
        }

        [Test]
        public void Given_APortalWhoseTrailingChildRendersNothing_When_ItUnmounts_Then_ThatChildIsStillDisposed()
        {
            // Arrange — the sibling ahead of it gives the portal a range, and the trailing component adds no
            // slot of its own, so it sits on that range's exclusive end while the same component written
            // first in the list sits inside it.
            var target = new VisualElement();
            FiberPortalRegistry.Register("notify-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(TrailingEmptyChildPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_childCleanups = 0;

            // Act
            s_setTrailingDropped.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childCleanups, Is.EqualTo(1));
        }

        private static StateUpdater<int> s_setRehomePhase;

        // The component is written into the portal's children by a PATCH rather than by the mount, which is
        // what gives it the same registry key its position outside the portal takes: both register under the
        // fiber declaring the portal, and an explicit key makes the position within that fiber irrelevant.
        [Component]
        private static VNode ReHomingPortalHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setRehomePhase = setPhase;
            return V.Div(children: new VNode?[]
            {
                phase < 3
                    ? V.Portal("notify-target", children: phase == 1
                        ? new VNode?[] { V.Component(PortalChildWithEffect, key: "c") }
                        : Array.Empty<VNode?>())
                    : null,
                phase >= 2 ? V.Component(PortalChildWithEffect, key: "c") : null,
            });
        }

        [Test]
        public void Given_AComponentReHomedOutOfAPortalsChildren_When_ThatPortalCloses_Then_TheCloseTakesNothingFurther()
        {
            // Arrange — leaving the portal's children already disposed the fiber that was written under it
            // (the cleanup this counts), so what the close must not do is reach the component now rendering
            // outside it.
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("notify-target", target);
            _mounted = V.Mount(container, V.Component(ReHomingPortalHost, key: "host"));
            s_setRehomePhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            s_setRehomePhase.Invoke(2);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            var cleanupsAfterReHome = s_childCleanups;

            // Act
            s_setRehomePhase.Invoke(3);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the element reading is folded in because a component that never reached its new
            // position would satisfy the count on its own.
            Assert.That((s_childCleanups - cleanupsAfterReHome, container.Query<VisualElement>("content").ToList().Count),
                Is.EqualTo((0, 1)));
        }

        private static StateUpdater<bool> s_setToastGrown;
        private static StateUpdater<bool> s_setToastOpen;
        private static StateUpdater<bool> s_setModalOpen;

        // Two components portal into one target, each holding its own open/size state, so either can
        // re-render without the other having any reason to. The toast side carries no component child:
        // its range moves the modal's without any fiber of its own taking part.
        [Component]
        private static VNode ToastPortalHost()
        {
            var (grown, setGrown) = Hooks.UseState(false);
            var (open, setOpen) = Hooks.UseState(true);
            s_setToastGrown = setGrown;
            s_setToastOpen = setOpen;
            return V.Div(children: new VNode?[]
            {
                open
                    ? V.Portal("notify-target", children: grown
                        ? new VNode?[] { V.Div(name: "toast-one"), V.Div(name: "toast-two") }
                        : new VNode?[] { V.Div(name: "toast-one") })
                    : null,
            });
        }

        [Component]
        private static VNode ModalPortalHost()
        {
            var (open, setOpen) = Hooks.UseState(true);
            s_setModalOpen = setOpen;
            return V.Div(children: new VNode?[]
            {
                open
                    ? V.Portal("notify-target",
                        children: new VNode?[] { V.Component(PortalChildWithEffect, key: "c") })
                    : null,
            });
        }

        [Component]
        private static VNode TwoPortalOverlayApp() =>
            V.Div(children: new VNode?[]
            {
                V.Component(ToastPortalHost, key: "toast"),
                V.Component(ModalPortalHost, key: "modal"),
            });

        private MountedTree MountTwoPortalOverlay(VisualElement target)
        {
            FiberPortalRegistry.Register("notify-target", target);
            var mounted = V.Mount(new VisualElement(), V.Component(TwoPortalOverlayApp, key: "app"));
            mounted.FlushEffectsForTest();
            s_childCleanups = 0;
            return mounted;
        }

        [Test]
        public void Given_ANeighbouringPortalThatGrewFirst_When_ItCloses_Then_TheOtherPortalsComponentChildSurvives()
        {
            // Arrange — the toast grows on its own state, which moves where the modal's child sits on the
            // shared target without the modal re-rendering.
            var target = new VisualElement();
            _mounted = MountTwoPortalOverlay(target);
            s_setToastGrown.Invoke(true);
            _mounted.FlushStateForTest();

            // Act — closing the toast tears out its own range only.
            s_setToastOpen.Invoke(false);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the modal is still open, so nothing of its has left.
            Assert.That(s_childCleanups, Is.EqualTo(0));
        }

        [Test]
        public void Given_ANeighbouringPortalThatClosedFirst_When_TheSecondCloses_Then_ItsComponentChildIsDisposed()
        {
            // Arrange — the mirror of the growth: the toast leaving collapses the modal's range left, and
            // the modal closing afterwards has to tear out the child at wherever it now sits.
            var target = new VisualElement();
            _mounted = MountTwoPortalOverlay(target);
            s_setToastOpen.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            s_setModalOpen.Invoke(false);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childCleanups, Is.EqualTo(1));
        }
    }
}
