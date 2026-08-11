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
    /// Specifies that a <c>V.Component</c> written as a top-level child of a <c>V.Portal</c> is the same
    /// component instance at mount and at every later patch of that portal's children — the logical-tree
    /// contract <c>Documentation~/portals.md</c> opens with, applied to the one child position whose mount
    /// runs in the deferred drain rather than in the declaring component's own reconcile.
    /// <list type="bullet">
    /// <item>Its mount effect runs once across a patch, and its hook state carries two.</item>
    /// <item>The target holds one element for it, not one per patch.</item>
    /// <item>A component that then leaves the portal's children takes its element out of the target with
    /// it, rather than leaving one behind for the portal to keep.</item>
    /// <item>The fiber the deferred mount pushes to reach that agreement comes back off the stack.</item>
    /// <item>A fiber the reconcile carries to another container re-renders itself into the container it
    /// is in, not the one it was created in.</item>
    /// <item>Sharing that parent does not merge the child with a same-position component the declaring
    /// tree renders outside the portal: each keeps its own instance and its own state.</item>
    /// <item>The stamp the deferred mount writes onto the children it created reaches those and no other
    /// child of the declaring fiber, so a sibling's isolated re-render still sees its own Providers.</item>
    /// </list>
    /// A component that leaves the portal's children in the render that removes the portal is carried,
    /// state and all, which is the fifth item above. A portal that survives and re-homes its child, or
    /// whose target id is re-registered elsewhere, unmounts and remounts it instead —
    /// <see cref="PortalRegistryRetargetTests"/> owns what a portal close reaches, and the guide states
    /// the move contract.
    /// </summary>
    internal sealed class PortalChildFiberContinuityTests
    {
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            s_setups = 0;
            s_lastRenderedCount = -1;
            s_twinSetups = 0;
            s_twinSetters.Clear();
            s_contextSeen = null;
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

        private static StateUpdater<string> s_setMark;

        // Its own state names its element, so a reading of the container says both which container the
        // fiber renders into and whether it is still the fiber that held the state.
        [Component]
        private static VNode MarkedChild()
        {
            var (mark, setMark) = Hooks.UseState("a");
            s_setMark = setMark;
            return V.Div(name: mark);
        }

        // Phase 1 drops the portal and writes the component outside it in the SAME render, which is what
        // makes the inline walk carry the fiber rather than dispose and rebuild it — the distinction
        // PortalRegistryRetargetTests draws between a closing render and a portal that merely empties.
        [Component]
        private static VNode CarryingHost()
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

        [Test]
        public void Given_AFiberCarriedToAnotherContainer_When_ItReRendersItself_Then_ItWritesIntoTheContainerItIsIn()
        {
            // Arrange — the mark is set before the carry, so the reading after it distinguishes the carried
            // fiber from a rebuilt one, which would be back at "a".
            var container = new VisualElement();
            var target = new VisualElement();
            FiberPortalRegistry.Register("continuity-target", target);
            _mounted = V.Mount(container, V.Component(CarryingHost, key: "host"));
            var outside = container.Q<VisualElement>("outside");
            s_setMark.Invoke("b");
            _mounted.FlushStateForTest();
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            var afterCarry = Names(outside);

            // Act — the component re-renders itself, which reconciles into its own MountPoint.
            s_setMark.Invoke("c");
            _mounted.FlushStateForTest();

            // Assert — the post-carry reading is folded in because a rebuilt fiber would reach "c" in the
            // right container too, and the target because the wrong container is where "c" lands without
            // the fix rather than nowhere.
            Assert.That((afterCarry, Names(outside), Names(target)), Is.EqualTo(("b", "c", "")));
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
        // scope ComponentRegistry.ResolveInline hands out separates them.
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
    }
}
