using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a <c>V.Component</c> written inside a <c>V.Portal</c> is the same component instance
    /// at mount and at every later patch of that portal's children — the logical-tree contract
    /// <c>Documentation~/portals.md</c> opens with, applied to the child positions whose mount runs in the
    /// deferred drain rather than in the declaring component's own reconcile.
    /// <list type="bullet">
    /// <item>Its mount effect runs once across a patch, and its hook state carries two.</item>
    /// <item>The target holds one element for it, not one per patch.</item>
    /// <item>A component that then leaves the portal's children takes its element out of the target with
    /// it, rather than leaving one behind for the portal to keep.</item>
    /// <item>The fiber the deferred mount pushes to reach that agreement comes back off the stack.</item>
    /// <item>Sharing that parent does not merge the child with a same-position component the declaring
    /// tree renders outside the portal — in either order of arrival, and whether the portal's occurrence
    /// arrives with the portal's own mount or by a later patch of its children: each keeps its own
    /// instance and its own state, and the two keys agree on parent, position and identity while only
    /// one names the portal. That member is what holds them apart where the container cannot: a portal
    /// whose target is the container its declaring component's own output lands in.</item>
    /// <item>The stamp the deferred mount writes onto the children it created reaches those and no other
    /// child of the declaring fiber, so a sibling's isolated re-render still sees its own Providers.</item>
    /// <item>A consumer a component level below the portal's own children, where the scope has been
    /// dropped again, is still found by the spine on its isolated re-render and keeps its Providers.
    /// </item>
    /// <item>A render that repeats away from the portal's own reconcile keys the same way both times: a
    /// <c>V.VirtualList</c> item, whose renderer runs from the scroll viewport's geometry as readily as
    /// from a patch of the portal's children, and an <c>V.Outlet</c> route Component, whose body mounts
    /// inside the portal's reconcile and re-renders alone afterwards.</item>
    /// </list>
    /// A component that crosses the portal boundary in either direction mounts fresh on the far side, as
    /// a component changing parent does anywhere else and as the guide states of a move; the fiber the
    /// side it left held is disposed with its cleanups. <see cref="PortalRegistryRetargetTests"/> owns
    /// what a portal close reaches.
    /// <para>
    /// Two containers are two positions whether or not a portal is involved, so what the container member
    /// of that key separates is <see cref="ComponentContainerIdentityTests"/>'s subject rather than this
    /// fixture's.
    /// </para>
    /// </summary>
    internal sealed class PortalChildFiberContinuityTests
    {
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            s_setups = 0;
            s_lastRenderedCount = -1;
            s_markSetups = 0;
            s_markCleanups = 0;
            s_twinSetups = 0;
            s_twinSetters.Clear();
            s_contextSeen = null;
            s_deepSeen = null;
            s_innerSetups = 0;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            Router.Current?.Dispose();
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private static int s_setups;
        private static int s_lastRenderedCount;
        private static StateUpdater<int> s_setCount;
        private static StateUpdater<int> s_setSibling;
        private static StateUpdater<int> s_setPhase;

        private static string Names(VisualElement element) =>
            string.Join("|", element.Children().Select(c => c.name));

        [Component]
        private static VNode PortalChild()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            s_lastRenderedCount = count;
            Hooks.UseLayoutEffect(() => { s_setups++; return (Action)(() => { }); }, Array.Empty<object>());
            return V.Div(name: "content");
        }

        // The sibling carries the state that drives the patch, so every reading below can name a term that
        // exists only once the portal's children have actually been patched.
        [Component]
        private static VNode PortalHost()
        {
            var (sibling, setSibling) = Hooks.UseState(0);
            s_setSibling = setSibling;
            return V.Portal("continuity-target", children: new VNode?[]
            {
                V.Component(PortalChild, key: "c"),
                V.Div(name: "sib-" + sibling),
            });
        }

        private VisualElement MountHostAndPatch()
        {
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(PortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setSibling.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            return target;
        }

        [Test]
        public void Given_AComponentChildOfAPortal_When_ThePortalsChildrenPatch_Then_ItsMountEffectDoesNotRunAgain()
        {
            // Arrange & Act
            var target = MountHostAndPatch();

            // Assert — the sibling name is folded in because a patch that never reached the portal's
            // children leaves the setup count at 1 on its own.
            Assert.That((s_setups, Names(target)), Is.EqualTo((1, "content|sib-1")));
        }

        [Test]
        public void Given_AComponentChildOfAPortal_When_ThePortalsChildrenPatch_Then_TheTargetKeepsOneElementForIt()
        {
            // Arrange & Act
            var target = MountHostAndPatch();

            // Assert — a second element for the same component would sit between the two named here, so
            // the reading is the whole slot range rather than a count.
            Assert.That(Names(target), Is.EqualTo("content|sib-1"));
        }

        // Two patches rather than one: the instance a single patch builds to replace the lost one
        // registers where every later patch looks, so its state survives from there and one patch cannot
        // tell "kept its state" from "lost it once, then kept the replacement's".
        [Test]
        public void Given_APortalChildComponentHoldingState_When_ThePortalsChildrenPatchTwice_Then_ItKeepsThatState()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(PortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setCount.Invoke(7);
            _mounted.FlushStateForTest();

            // Act
            s_setSibling.Invoke(1);
            _mounted.FlushStateForTest();
            s_setSibling.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert — the sibling name is folded in because the last render of a component that never
            // lost its state and of one that was never re-rendered at all both report 7.
            Assert.That((s_lastRenderedCount, Names(target)), Is.EqualTo((7, "content|sib-2")));
        }

        private static int FiberStackDepth(MountedTree mounted)
        {
            var stack = mounted.Root.Reconciler.Context.FiberStack;
            var field = typeof(FiberStack).GetField("_stack", BindingFlags.NonPublic | BindingFlags.Instance);
            return ((ICollection)field!.GetValue(stack)!).Count;
        }

        // GREEN_ON_BASE(characterization): this pins a balance the base does not have — the base's drain
        // pushes nothing, so its stack is even for want of a push rather than by unwinding one. What the
        // case exists to catch is the matching Pop going missing, which mutation_check.py measured as
        // leaving the whole EditMode suite green.
        [Test]
        public void Given_APortalDrainedUnderItsDeclaringComponent_When_ThePassEnds_Then_TheFiberStackIsUnwound()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);

            // Act
            _mounted = V.Mount(new VisualElement(), V.Component(PortalHost, key: "host"));

            // Assert — the target's contents are folded in because a tree whose portal never drained
            // leaves the stack just as unwound.
            Assert.That((FiberStackDepth(_mounted), Names(target)), Is.EqualTo((0, "content|sib-0")));
        }

        [Component]
        private static VNode ReHomingHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(children: new VNode?[]
            {
                V.Portal("continuity-target", children: phase == 0
                    ? new VNode?[] { V.Component(PortalChild, key: "c") }
                    : Array.Empty<VNode?>()),
                phase == 1 ? V.Component(PortalChild, key: "c") : null,
            });
        }

        [Test]
        public void Given_APortalChildComponent_When_ItReHomesOutOfThePortalsChildren_Then_TheTargetKeepsNoElementForIt()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(ReHomingHost, key: "host"));
            _mounted.FlushEffectsForTest();

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the arrival outside is folded in because a component that never reached its new
            // position empties the target just as thoroughly.
            Assert.That(
                (target.childCount, container.Query<VisualElement>("content").ToList().Count),
                Is.EqualTo((0, 1)));
        }

        private static StateUpdater<string> s_setMark;
        private static int s_markSetups;
        private static int s_markCleanups;

        // Its own state names its element, so a reading of a container says both which container the
        // fiber renders into and whether it is still the fiber that held the state.
        [Component]
        private static VNode MarkedChild()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_setMark = setMark;
            Hooks.UseLayoutEffect(
                () => { s_markSetups++; return (Action)(() => { s_markCleanups++; }); }, Array.Empty<object>());
            return V.Div(name: mark);
        }

        // Phase 1 drops the portal and writes the component outside it in the SAME render, which is the
        // render a carry would happen in if the two positions shared a registry key.
        [Component]
        private static VNode ClosingPortalHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "outside", children: new VNode?[]
            {
                phase == 0
                    ? V.Portal("continuity-target", children: new VNode?[] { V.Component(MarkedChild, key: "c") })
                    : null,
                phase == 1 ? V.Component(MarkedChild, key: "c") : null,
            });
        }

        // GREEN_ON_BASE(characterization): a portal child and a position outside the portal are separate
        // positions on the base too, so the fresh mount is what it already does. Keying the drained child
        // on the declaring fiber puts the two within one key of each other, and this pins that the portal
        // scope keeps the move a move rather than turning it into a carry of state, effects and all.
        [Test]
        public void Given_APortalChildComponent_When_ThePortalClosesAndItIsWrittenOutsideInTheSameRender_Then_ItMountsFreshThere()
        {
            // Arrange — the mark is set before the close, so a fiber that carried would read "b" at the
            // new position instead of the initial value.
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(ClosingPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the cleanup count is folded in because an instance carried across would also be
            // the one holding the mark, and only a cleanup says the one the portal held was retired.
            Assert.That(
                (Names(container.Q<VisualElement>("outside")), s_markCleanups), Is.EqualTo(("a", 1)));
        }

        // The portal is live from the mount and gains the keyed component by a PATCH, which is the entrance
        // that has always reconciled a portal's children with the declaring fiber current — so this is the
        // shape where the two occurrences shared a whole key before the scope was part of it.
        [Component]
        private static VNode PatchedIntoPortalHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "outside", children: new VNode?[]
            {
                V.Portal("continuity-target", children: phase == 0
                    ? new VNode?[] { V.Div(name: "sib") }
                    : new VNode?[] { V.Div(name: "sib"), V.Component(MarkedChild, key: "c") }),
                V.Component(MarkedChild, key: "c"),
            });
        }

        [Test]
        public void Given_AKeyedComponentOutsideAPortal_When_TheSameKeyIsPatchedIntoThePortalsChildren_Then_EachPositionKeepsItsOwnInstance()
        {
            // Arrange — the one outside holds the mark, so a portal child that took its fiber over would
            // read "b" in the target and empty the position outside.
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(PatchedIntoPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the container outside is folded in because the position there is what the portal's
            // child took the instance from, so a target reading alone cannot say the two are apart. The
            // leading empty name outside is the portal's placeholder.
            Assert.That(
                (Names(target), Names(container.Q<VisualElement>("outside"))),
                Is.EqualTo(("sib|a", "|b")));
        }

        [Component]
        private static VNode OpeningPortalHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "outside", children: new VNode?[]
            {
                phase == 1
                    ? V.Portal("continuity-target", children: new VNode?[] { V.Component(MarkedChild, key: "c") })
                    : null,
                phase == 0 ? V.Component(MarkedChild, key: "c") : null,
            });
        }

        // GREEN_ON_BASE(characterization): the same fresh mount the base already gives this direction, and
        // the sibling case above gives the other one. Both are pinned because the portals guide states the
        // two together, and a key that reached across the boundary would break whichever direction it
        // reached in.
        [Test]
        public void Given_AComponentOutsideAPortal_When_APortalOpensAndItIsWrittenInsideInTheSameRender_Then_ItMountsFreshThere()
        {
            // Arrange — the mark is set before the move, so a fiber that carried would read "b" inside the
            // portal instead of the initial value.
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(OpeningPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the cleanup count is folded in for the reason its opposite direction gives.
            Assert.That((Names(target), s_markCleanups), Is.EqualTo(("a", 1)));
        }

        // The in-tree occurrence appears one render AFTER the portal's, which is the order in which the
        // portal's child is the one already holding the shared spelling of the key.
        [Component]
        private static VNode LateSiblingHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "outside", children: new VNode?[]
            {
                V.Portal("continuity-target", children: new VNode?[]
                {
                    V.Component(MarkedChild),
                    V.Div(name: "sib-" + phase),
                }),
                phase >= 1 ? V.Component(MarkedChild) : null,
            });
        }

        private (VisualElement Outside, VisualElement Target) MountLateSiblingHostThrough(int phase)
        {
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(LateSiblingHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();
            for (var p = 1; p <= phase; p++)
            {
                s_setPhase.Invoke(p);
                _mounted.FlushStateForTest();
                _mounted.FlushEffectsForTest();
            }
            return (container.Q<VisualElement>("outside"), target);
        }

        [Test]
        public void Given_APortalChildComponent_When_ASamePositionComponentAppearsOutsideThePortal_Then_ThePortalsChildKeepsItsOwnInstance()
        {
            // Arrange & Act
            var (outside, target) = MountLateSiblingHostThrough(1);

            // Assert — the container outside is folded in because a component that never reached its
            // position there leaves the portal's child reading "b" just as a separate instance does. The
            // leading empty name there is the portal's placeholder.
            Assert.That((Names(target), Names(outside)), Is.EqualTo(("b|sib-1", "|a")));
        }

        [Test]
        public void Given_APortalChildAndASamePositionComponentOutsideIt_When_ThePortalPatchesAgain_Then_TheTargetHoldsOneElementForThatChild()
        {
            // Arrange & Act
            var (outside, target) = MountLateSiblingHostThrough(2);

            // Assert — a second element built for the portal's child would sit inside this range, and the
            // sibling name says the second patch reached the portal's children at all.
            Assert.That((Names(target), Names(outside)), Is.EqualTo(("b|sib-2", "|a")));
        }

        private static int s_twinSetups;
        private static readonly List<StateUpdater<string>> s_twinSetters = new();

        // Its own state names its element, and every setter is collected in render order so a test can
        // drive one occurrence and read the other. The one outside the portal renders first.
        [Component]
        private static VNode Twin()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_twinSetters.Add(setMark);
            Hooks.UseLayoutEffect(() => { s_twinSetups++; return (Action)(() => { }); }, Array.Empty<object>());
            return V.Div(name: "twin-" + mark);
        }

        // The portal targets the very container this component's own output lands in, so the two
        // occurrences agree on the container as well as on the rest of the tree-position key — and the
        // portal scope is the one member of that key left to hold them apart.
        [Component]
        private static VNode SharedTargetTwinHost()
            => V.Fragment(new VNode?[]
            {
                V.Component(Twin),
                V.Portal("shared-container", children: new VNode?[] { V.Component(Twin) }),
            });

        // GREEN_ON_BASE(characterization): the portal scope is what separates these two on the base as
        // well. What it pins is that a container member cannot take that job over, so removing the scope
        // as redundant would merge them.
        [Test]
        public void Given_APortalTargetingTheContainerItsDeclaringComponentRendersInto_When_TheSameComponentIsWrittenBothSides_Then_EachRunsItsOwnMountEffect()
        {
            // Arrange — the mount container is the portal target, so both occurrences land in it.
            var container = new VisualElement();
            FiberPortalRegistry.Register("shared-container", container);

            // Act
            _mounted = V.Mount(container, V.Component(SharedTargetTwinHost, key: "host"));
            _mounted.FlushEffectsForTest();

            // Assert — the count of elements the component emitted is folded in so the second run is
            // attributed to a second instance rather than to one instance whose effect ran twice. It
            // counts rather than spelling the container's children out, because what a portal puts
            // beside them is FiberNodeFactory's business rather than this case's.
            Assert.That(
                (s_twinSetups, container.Children().Count(c => c.name == "twin-a")),
                Is.EqualTo((2, 2)));
        }

        // Both occurrences are unkeyed, first of their identity in their own reconcile scope, and rendered
        // by this one fiber — so they agree on the whole of the tree-position key apart from the container,
        // and the portal scope separates them where that would not.
        [Component]
        private static VNode TwinHost()
            => V.Div(name: "twin-host", children: new VNode?[]
            {
                V.Div(name: "inline-slot", children: new VNode?[] { V.Component(Twin) }),
                V.Portal("continuity-target", children: new VNode?[] { V.Component(Twin), V.Div(name: "sib") }),
            });

        private (VisualElement InlineSlot, VisualElement Target) MountTwins()
        {
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(TwinHost, key: "host"));
            _mounted.FlushEffectsForTest();
            return (container.Q<VisualElement>("inline-slot"), target);
        }

        // GREEN_ON_BASE(characterization): the base gives the two occurrences separate registry parents by
        // accident — the drained one lands on the reconcile root — so it separates them without meaning to.
        // Keying the drained child on the declaring fiber removes that accident, and this pins that what
        // replaces it separates them on purpose.
        [Test]
        public void Given_OneComponentBothInAPortalAndOutsideIt_When_TheyMount_Then_EachRunsItsOwnMountEffect()
        {
            // Arrange & Act
            var (_, target) = MountTwins();

            // Assert — the target's contents are folded in so the second run is attributed: a count of two
            // says two mount effects ran, not that one of them belongs to a child that reached the target.
            Assert.That((s_twinSetups, Names(target)), Is.EqualTo((2, "twin-a|sib")));
        }

        // GREEN_ON_BASE(characterization): separate on the base for the accidental reason its sibling case
        // above states, so this reads the same there and pins the same replacement.
        [Test]
        public void Given_OneComponentBothInAPortalAndOutsideIt_When_TheOutsideOneSetsItsState_Then_ThePortalsKeepsItsOwn()
        {
            // Arrange
            var (inlineSlot, target) = MountTwins();

            // Act
            s_twinSetters[0].Invoke("b");
            _mounted!.FlushStateForTest();

            // Assert — the container outside is folded in because a setter that reached nothing at all
            // leaves the portal's child reading "a" just as a separate instance does.
            Assert.That((Names(inlineSlot), Names(target)), Is.EqualTo(("twin-b", "twin-a|sib")));
        }

        // A plain host element between the Portal and the component pushes no fiber, so this component's
        // registry parent is still the declaring fiber and the shape outside the portal keys identically.
        [Component]
        private static VNode HostNestedTwinHost()
            => V.Div(name: "twin-host", children: new VNode?[]
            {
                V.Div(name: "inline-wrap", children: new VNode?[] { V.Component(Twin) }),
                V.Portal("continuity-target", children: new VNode?[]
                {
                    V.Div(name: "portal-wrap", children: new VNode?[] { V.Component(Twin) }),
                }),
            });

        // Four of the five members of the key one inline fiber is registered under, read off the
        // registry's own reverse index rather than recomputed. The fifth is the container, which locates
        // the row here rather than being read from it — ComponentContainerIdentityTests owns what it
        // separates.
        private static (object? Parent, object? Scope, object? PositionKey, object? Identity) InlineKeyForOutputIn(
            MountedTree mounted, VisualElement mountPoint)
        {
            var registry = mounted.Root.Reconciler.Context.ComponentRegistry;
            var field = registry.GetType().GetField(
                "_inlineFiberToKey", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (DictionaryEntry entry in (IDictionary)field!.GetValue(registry)!)
            {
                if (!ReferenceEquals(((ComponentFiber)entry.Key).MountPoint, mountPoint)) continue;
                var key = entry.Value!;
                var type = key.GetType();
                return (type.GetField("Item1")!.GetValue(key), type.GetField("Item2")!.GetValue(key),
                    type.GetField("Item4")!.GetValue(key), type.GetField("Item5")!.GetValue(key));
            }
            throw new InvalidOperationException("no inline fiber renders into " + mountPoint.name);
        }

        [Test]
        public void Given_APortalChildComponentNestedUnderAHostElement_When_TheSameShapeIsRenderedOutsideThePortal_Then_TheyAgreeOnParentPositionAndIdentityAndOnlyOneNamesThePortal()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(HostNestedTwinHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var placeholder = _mounted.Root.Reconciler.Context.PortalState.Keys.Single();

            // Act
            var inside = InlineKeyForOutputIn(_mounted, target.Q<VisualElement>("portal-wrap"));
            var outside = InlineKeyForOutputIn(_mounted, container.Q<VisualElement>("inline-wrap"));

            // Assert — the three agreeing members are what say the two shapes collide this deep at all,
            // and a scope naming the placeholder on one side and nothing on the other is what the portal
            // contributes to holding them apart.
            Assert.That(
                (ReferenceEquals(inside.Parent, outside.Parent), Equals(inside.PositionKey, outside.PositionKey),
                    ReferenceEquals(inside.Identity, outside.Identity), inside.Scope, outside.Scope),
                Is.EqualTo((true, true, true, (object?)placeholder, (object?)null)));
        }

        // A V.Provider is inline-expanded and pushes no fiber either, so what the scope reaches is not the
        // list of node types the host-element case above happens to name.
        [Component]
        private static VNode ProviderNestedTwinHost()
            => V.Div(name: "twin-host", children: new VNode?[]
            {
                V.Div(name: "inline-wrap", children: new VNode?[]
                {
                    V.Provider(ScopeContext, "inside", new VNode[] { V.Component(Twin) }),
                }),
                V.Portal("continuity-target", children: new VNode?[]
                {
                    V.Provider(ScopeContext, "inside", new VNode[] { V.Component(Twin) }),
                }),
            });

        [Test]
        public void Given_APortalChildComponentNestedUnderAProvider_When_TheSameShapeIsRenderedOutsideThePortal_Then_TheyAgreeOnParentPositionAndIdentityAndOnlyOneNamesThePortal()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(ProviderNestedTwinHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var placeholder = _mounted.Root.Reconciler.Context.PortalState.Keys.Single();

            // Act — the Provider emits no element, so the portal's occurrence renders straight into the
            // target rather than into a wrapper of its own.
            var inside = InlineKeyForOutputIn(_mounted, target);
            var outside = InlineKeyForOutputIn(_mounted, container.Q<VisualElement>("inline-wrap"));

            // Assert — the three agreeing members are what say the two shapes collide this deep at all,
            // and a scope naming the placeholder on one side and nothing on the other is what the portal
            // contributes to holding them apart.
            Assert.That(
                (ReferenceEquals(inside.Parent, outside.Parent), Equals(inside.PositionKey, outside.PositionKey),
                    ReferenceEquals(inside.Identity, outside.Identity), inside.Scope, outside.Scope),
                Is.EqualTo((true, true, true, (object?)placeholder, (object?)null)));
        }

        private static StateUpdater<string> s_setResidueMark;
        private static StateUpdater<int> s_setResidueTick;

        [Component]
        private static VNode ResidueLeaf()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_setResidueMark = setMark;
            return V.Div(name: "leaf-" + mark);
        }

        // A component level between the declarer and the leaf, so the leaf's own position is expanded one
        // fiber deeper than the declarer's re-render starts — which is the depth the drain entered its
        // scope at, the declaring fiber having been pushed over the pass's own.
        [Component]
        private static VNode ResidueMiddle() => V.Div(name: "middle", children: new VNode?[]
        {
            V.Component(ResidueLeaf),
        });

        [Component]
        private static VNode ResidueDeclarer()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setResidueTick = setTick;
            return V.Div(name: "declarer-" + tick, children: new VNode?[]
            {
                V.Portal("continuity-target", children: new VNode?[] { V.Component(PlainPortalChild) }),
                V.Component(ResidueMiddle),
            });
        }

        // GREEN_ON_BASE(characterization): the base has no portal scope to leave behind, so it keeps this
        // instance for want of a scope rather than by restoring one. What this pins is the restore: with
        // the drain's ExitPortalChildKeyScope removed, the scope it set outlives the pass, and the next
        // render reaching the same fiber depth keys a component nowhere near the portal as one of its
        // children — measured as a second fiber built beside the first, the container reading
        // "leaf-a|leaf-b" rather than "leaf-b".
        [Test]
        public void Given_APortalDrainedUnderItsDeclaringComponent_When_ALaterRenderReachesThatSameDepth_Then_AComponentOutsideThePortalKeepsItsInstance()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(ResidueDeclarer, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setResidueMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act — the declarer re-renders alone, which expands the middle component's own children at
            // the depth the drain recorded.
            s_setResidueTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the declarer's name carries the tick because a render that never reached the leaf
            // leaves it reading "b" just as a kept instance does.
            Assert.That(
                (Names(container.Q<VisualElement>("middle")), Names(container)),
                Is.EqualTo(("leaf-b", "declarer-1")));
        }

        private static readonly ComponentContext<string> ScopeContext = ComponentContext<string>.Create("outside");
        private static string s_contextSeen;
        private static StateUpdater<int> s_setSiblingTick;

        [Component]
        private static VNode PlainPortalChild() => V.Div(name: "plain");

        [Component]
        private static VNode ContextReadingSibling()
        {
            s_contextSeen = Hooks.UseContext(ScopeContext);
            var (tick, setTick) = Hooks.UseState(0);
            s_setSiblingTick = setTick;
            return V.Div(name: "sibling-" + tick);
        }

        // The sibling is an ordinary direct child fiber of the same declaring fiber the deferred mount
        // anchors this portal's children on, so the mount's own "which of these did I just create" diff is
        // the only thing keeping the portal's DetachedMountContext off it. Carrying that stamp would send
        // the sibling's isolated re-render down the spine's detached arm, which rebuilds context from the
        // snapshot taken at the portal's position — above the Provider written below it.
        [Component]
        private static VNode PortalAndProviderHost()
            => V.Div(name: "scope-host", children: new VNode?[]
            {
                V.Portal("continuity-target", children: new VNode?[] { V.Component(PlainPortalChild) }),
                V.Provider(ScopeContext, "inside", new VNode[] { V.Component(ContextReadingSibling, key: "s") }),
            });

        // GREEN_ON_BASE(characterization): the base anchors a drained portal child on the reconcile root,
        // where this declaring fiber's other children are not, so its stamp cannot reach them and the case
        // is green there for want of the anchor rather than by the diff this pins. The anchor being the
        // declaring fiber is what makes that diff load-bearing, and removing either half of it — the
        // before-set add, or the skip that reads it — leaves every other case in this fixture green.
        [Test]
        public void Given_APortalBesideAContextConsumingSibling_When_TheSiblingReRendersAlone_Then_ItStillReadsTheProviderAroundIt()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(PortalAndProviderHost, key: "host"));
            _mounted.FlushEffectsForTest();

            // Act
            s_setSiblingTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the sibling's own element is folded in because the value read at mount is "inside"
            // either way, so only a name carrying the new tick says the re-render happened at all.
            Assert.That(
                (s_contextSeen, Names(container.Q<VisualElement>("scope-host"))),
                Is.EqualTo(("inside", "|sibling-1")));
        }

        private static string s_deepSeen;
        private static StateUpdater<int> s_setDeepTick;

        [Component]
        private static VNode DeepConsumer()
        {
            s_deepSeen = Hooks.UseContext(ScopeContext);
            var (tick, setTick) = Hooks.UseState(0);
            s_setDeepTick = setTick;
            return V.Div(name: "deep-" + tick);
        }

        // A component level between the portal and the consumer, so the consumer's registry parent is a
        // fiber inside the portal rather than the declaring one — the level at which the portal scope has
        // been dropped again, and the spine has to look the consumer up without it.
        [Component]
        private static VNode DeepMiddle()
            => V.Provider(ScopeContext, "inside", new VNode[] { V.Component(DeepConsumer) });

        [Component]
        private static VNode DeepPortalHost()
            => V.Div(name: "deep-host", children: new VNode?[]
            {
                V.Portal("continuity-target", children: new VNode?[] { V.Component(DeepMiddle) }),
            });

        // GREEN_ON_BASE(characterization): the base has no portal scope in the key at all, so its spine
        // asks the one question there is and this reads the same there. The scope makes the question two,
        // and asking the wrong one of a consumer this deep loses the Provider — measured, with the whole
        // of PortalContextInheritanceTests still green, because its consumers are the portal's own
        // children rather than a level below one.
        [Test]
        public void Given_AConsumerBelowAComponentInsideAPortal_When_ItReRendersAlone_Then_ItStillReadsTheProviderAroundIt()
        {
            // Arrange
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(DeepPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();

            // Act
            s_setDeepTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the element name is folded in because the value read at mount is "inside" either
            // way, so only a name carrying the new tick says the re-render happened at all.
            Assert.That((s_deepSeen, Names(target)), Is.EqualTo(("inside", "deep-1")));
        }

        private static StateUpdater<string> s_setRowMark;

        [Component]
        private static VNode Row()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_setRowMark = setMark;
            return V.Div(name: "row-" + mark);
        }

        // The list's items render from the scroll viewport's geometry, outside every reconcile, and again
        // from a patch of this portal's children — the two drives this case holds against each other. The
        // item root is named for the host's state so a reading of it says which of the two produced it.
        [Component]
        private static VNode VirtualListPortalHost()
        {
            var (sibling, setSibling) = Hooks.UseState(0);
            s_setSibling = setSibling;
            return V.Portal("continuity-target", children: new VNode?[]
            {
                V.VirtualList(
                    items: new[] { "only" },
                    keySelector: item => item,
                    itemHeight: 50f,
                    renderer: item => V.Div(name: "item-" + sibling, children: new VNode?[] { V.Component(Row) }),
                    overscan: 0),
            });
        }

        // GREEN_ON_BASE(characterization): the base keys an item's components the one way there is, so the
        // two drives agree there without being held to it. The portal scope makes them two keys, and this
        // is what says the item render is not one of the levels that carries it.
        [Test]
        public void Given_AComponentInsideAVirtualListInAPortal_When_ThePortalsChildrenPatch_Then_TheItemKeepsTheInstanceItsGeometryRenderBuilt()
        {
            // Arrange — the viewport height a geometry pass would have left, so the patch below re-renders
            // the range instead of returning at the controller's unknown-viewport gate.
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(new VisualElement(), V.Component(VirtualListPortalHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var scrollView = target.Q<ScrollView>();
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, 200f);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            _mounted.FlushEffectsForTest();
            s_setRowMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setSibling.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the item root's own name is what says the patch re-rendered the range at all, so
            // the child name beside it is a reading of the instance that render resolved to rather than
            // of one nothing touched. A second row beside the first is the replacement instance.
            var itemRoot = scrollView.contentContainer.ElementAt(1).ElementAt(0);
            Assert.That((itemRoot.name, Names(itemRoot)), Is.EqualTo(("item-1", "row-b")));
        }

        private static int s_innerSetups;
        private static StateUpdater<string> s_setInnerMark;
        private static StateUpdater<int> s_setRouteTick;

        [Component]
        private static VNode RouteInner()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_setInnerMark = setMark;
            Hooks.UseLayoutEffect(() => { s_innerSetups++; return (Action)(() => { }); }, Array.Empty<object>());
            return V.Div(name: "inner-" + mark);
        }

        // Wrapper-mounted on the Outlet's container rather than inline-expanded, so its body's own
        // reconcile runs inside FiberRenderer.RenderAndReconcile — once from the portal's deferred mount,
        // and every time after that from its own state, with no portal reconcile anywhere on the stack.
        [Component]
        private static VNode RouteBody()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setRouteTick = setTick;
            return V.Div(name: "route-" + tick, children: new VNode?[] { V.Component(RouteInner) });
        }

        [Component]
        private static VNode OutletPortalHost()
            => V.Portal("continuity-target", children: new VNode?[]
            {
                V.Div(name: "outlet-wrap", children: new VNode?[] { V.Outlet() }),
            });

        private void MountOutletPortalHostWith(Router router)
        {
            _mounted = V.Mount(new VisualElement(),
                V.Provider(RouterContext.Location, router.CurrentLocation,
                    children: new VNode[]
                    {
                        V.Provider(RouterContext.LoaderData, router.CurrentLoaderData,
                            children: new VNode[]
                            {
                                V.Provider(RouterContext.Errors, router.CurrentLoaderErrors,
                                    children: new VNode[]
                                    {
                                        V.Component(OutletPortalHost, key: "host"),
                                    }),
                            }),
                    }));
        }

        // GREEN_ON_BASE(characterization): the base has one key for the route body's children whichever
        // pass reaches them, so the mount and the isolated re-render agree there for want of a second key
        // rather than by anything holding them to it.
        [Test]
        public void Given_AnOutletRouteInsideAPortal_When_TheRouteComponentReRendersAlone_Then_ItsOwnChildKeepsItsInstance()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            var router = new Router(new[]
            {
                new RouteDefinition { Path = "home", Element = V.Component(RouteBody, key: "route") },
            });
            router.NavigateAsync("/home").GetAwaiter().GetResult();
            MountOutletPortalHostWith(router);
            _mounted!.FlushEffectsForTest();
            s_setInnerMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setRouteTick.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the route element is located by the tick it renders, so a reading at all says the
            // re-render happened; its children are folded in beside the mount count because a second
            // instance shows as either a reset mark or a second element next to the first.
            var route = target.Q<VisualElement>("route-1");
            Assert.That(
                (s_innerSetups, route == null ? "<no route-1>" : Names(route)),
                Is.EqualTo((1, "inner-b")));
        }
    }
}
