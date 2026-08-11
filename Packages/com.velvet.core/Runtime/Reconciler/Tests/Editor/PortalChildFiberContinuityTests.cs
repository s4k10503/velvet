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
    /// instance and its own state, and the registry keys of the two differ in the portal scope alone.
    /// </item>
    /// <item>The stamp the deferred mount writes onto the children it created reaches those and no other
    /// child of the declaring fiber, so a sibling's isolated re-render still sees its own Providers.</item>
    /// <item>A consumer a component level below the portal's own children, where the scope has been
    /// dropped again, is still found by the spine on its isolated re-render and keeps its Providers.
    /// </item>
    /// </list>
    /// A component that crosses the portal boundary in either direction mounts fresh on the far side, as
    /// a component changing parent does anywhere else and as the guide states of a move; the fiber the
    /// side it left held is disposed with its cleanups. <see cref="PortalRegistryRetargetTests"/> owns
    /// what a portal close reaches.
    /// <para>
    /// A component the reconcile carries between two containers is a separate position on this fixture:
    /// two same-identity unkeyed occurrences in different containers of one declaring component still
    /// resolve to one instance, which is an open defect, and what is pinned here is only that the one
    /// instance's own re-render lands in the container it last occupied.
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
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
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

        [Component]
        private static VNode TwoContainerHost()
            => V.Div(name: "outside", children: new VNode?[]
            {
                V.Div(name: "left", children: new VNode?[] { V.Component(MarkedChild) }),
                V.Div(name: "right", children: new VNode?[] { V.Component(MarkedChild) }),
            });

        [Test]
        public void Given_OneFiberSharedByTwoContainers_When_ItReRendersItself_Then_ItWritesIntoTheContainerItLastOccupied()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(TwoContainerHost, key: "host"));
            _mounted.FlushEffectsForTest();

            // Act — the shared instance re-renders on its own, which reconciles into its own MountPoint.
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();

            // Assert — the left container is folded in because the two occurrences sharing one instance is
            // what puts a container on each side of the reading: an instance that had reached only one of
            // them would leave the other empty rather than holding the value it rendered before.
            Assert.That(
                (Names(container.Q<VisualElement>("left")), Names(container.Q<VisualElement>("right"))),
                Is.EqualTo(("a", "b")));
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

        // Both occurrences are unkeyed, first of their identity in their own reconcile scope, and rendered
        // by this one fiber — so they agree on the whole of the tree-position key, and only the portal
        // scope separates them.
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

        // The four members of the key one inline fiber is registered under, read off the registry's own
        // reverse index rather than recomputed, and located by the container the fiber renders into.
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
                    type.GetField("Item3")!.GetValue(key), type.GetField("Item4")!.GetValue(key));
            }
            throw new InvalidOperationException("no inline fiber renders into " + mountPoint.name);
        }

        [Test]
        public void Given_APortalChildComponentNestedUnderAHostElement_When_TheSameShapeIsRenderedOutsideThePortal_Then_TheirRegistryKeysDifferOnlyInTheScope()
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

            // Assert — the three other members being equal is what says the two shapes collide this deep
            // at all; a scope naming the placeholder on one side and nothing on the other is the whole of
            // what holds them apart.
            Assert.That(
                (ReferenceEquals(inside.Parent, outside.Parent), Equals(inside.PositionKey, outside.PositionKey),
                    ReferenceEquals(inside.Identity, outside.Identity), inside.Scope, outside.Scope),
                Is.EqualTo((true, true, true, (object?)placeholder, (object?)null)));
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
    }
}
