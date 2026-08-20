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
    /// Specifies that the host container a <c>V.Component</c> is written into is part of which instance it
    /// is, so two containers are two positions in the sense <c>Documentation~/react-migration.md</c> states.
    /// <list type="bullet">
    /// <item>Two sibling containers of one declaring component, each holding the same component, mount two
    /// instances with their own state — whether the two occurrences are unkeyed or share one explicit
    /// <c>key:</c>, since a key separates siblings of one container rather than containers.</item>
    /// <item>Each instance's own updates reach the container it was written into, so neither container
    /// becomes output that no later render repairs.</item>
    /// <item>Reordering keyed containers moves each instance with its container, without remounting it.
    /// </item>
    /// <item>What separates the two is the container alone: their registry keys agree on the parent fiber,
    /// the position key and the identity, which is why the walk position the expansion carries cannot tell
    /// them apart.</item>
    /// <item>A container leaving the tree in the render that writes the component into another one leaves a
    /// fresh instance behind there and runs the departing instance's cleanup.</item>
    /// <item>A container returned to the primitive-element pool and rented again carries no registry entry
    /// into its next use.</item>
    /// <item>What the separation does NOT reach: where two instances also share a position key, an
    /// instance's own isolated re-render reads the Providers of whichever container the committed tree
    /// reaches first, because the spine that rebuilds its context cannot tell the containers apart. That
    /// is a value from elsewhere where the two Providers carry the same context, and the context default
    /// where the other container's carries a different one — so an instance can lose the Provider it is
    /// written inside. A <c>V.Motion</c>'s active label travels the same path and reads the same way. All
    /// three are pinned here.</item>
    /// </list>
    /// <see cref="PortalChildFiberContinuityTests"/> owns the portal-scope member of the same key, and
    /// holds the shape where a shared container leaves that member the only thing separating two
    /// occurrences.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentContainerIdentityTests
    {
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            s_setups = 0;
            s_cleanups = 0;
            s_setters.Clear();
            s_marks.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static int s_setups;
        private static int s_cleanups;
        private static readonly List<StateUpdater<int>> s_setters = new();
        private static readonly List<StateUpdater<string>> s_marks = new();
        private static StateUpdater<bool> s_setSwapped;
        private static StateUpdater<int> s_setPhase;
        private static StateUpdater<bool> s_setShown;
        private static readonly ComponentContext<string> Theme = ComponentContext<string>.Create("default");
        // A second context, so the two containers can hold Providers at one position while only one of
        // them supplies what Badge reads.
        private static readonly ComponentContext<string> Accent = ComponentContext<string>.Create("accent");

        private static string Names(VisualElement? element) =>
            element == null ? "<absent>" : string.Join("|", element.Children().Select(c => c.name));

        private static string TextIn(VisualElement? container) =>
            container?.Q<Label>("out")?.text ?? "<absent>";

        [Component]
        private static VNode Counter()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setters.Add(setCount);
            Hooks.UseLayoutEffect(() => { s_setups++; return (Action)(() => s_cleanups++); }, Array.Empty<object>());
            return V.Div(name: "counter", children: new VNode?[]
            {
                V.Button(name: "inc", onClick: () => setCount.Invoke(c => c + 1)),
                V.Label(name: "out", text: count.ToString()),
            });
        }

        [Component]
        private static VNode TwoContainerHost()
            => V.Div(name: "shell", children: new VNode?[]
            {
                V.Div(name: "left", children: new VNode?[] { V.Component(Counter) }),
                V.Div(name: "right", children: new VNode?[] { V.Component(Counter) }),
            });

        private (VisualElement Left, VisualElement Right) MountTwoContainers(VNode host)
        {
            var container = new VisualElement();
            _mounted = V.Mount(container, host);
            _mounted.FlushEffectsForTest();
            return (container.Q<VisualElement>("left"), container.Q<VisualElement>("right"));
        }

        [Test]
        public void Given_TwoSiblingContainersHoldingTheSameUnkeyedComponent_When_TheyMount_Then_EachContainerGetsItsOwnInstance()
        {
            // Arrange & Act
            var (left, right) = MountTwoContainers(V.Component(TwoContainerHost, key: "host"));

            // Assert — both containers' output is folded in because one instance rendering twice fills
            // them both at mount too; the layout-effect count is what says there are two instances behind
            // them. One instance answering for both renders twice in the pass, and a layout effect whose
            // dependency list is stable across those two renders then runs no setup at all rather than
            // one: FiberRenderer clears the pending list at the head of every render and
            // HookSlotRegistrar re-adds a slot only when its deps differ. A null or changing list is
            // re-added and does run once, which is what says the count here is that skip and not a
            // fiber reaching the effect drain twice.
            Assert.That((s_setups, TextIn(left), TextIn(right)), Is.EqualTo((2, "0", "0")));
        }

        [Test]
        public void Given_TwoSiblingContainersHoldingTheSameUnkeyedComponent_When_TheFirstContainersButtonIsClicked_Then_OnlyThatContainersOutputChanges()
        {
            // Arrange
            var (left, right) = MountTwoContainers(V.Component(TwoContainerHost, key: "host"));

            // Act — the first container's button, which is the one whose reading differs from what a
            // single instance answering for both containers would produce.
            left.Q<Button>("inc").SimulateClick();

            // Assert
            Assert.That((TextIn(left), TextIn(right)), Is.EqualTo(("1", "0")));
        }

        [Component]
        private static VNode SameKeyHost()
            => V.Div(name: "shell", children: new VNode?[]
            {
                V.Div(name: "left", children: new VNode?[] { V.Component(Counter, key: "same") }),
                V.Div(name: "right", children: new VNode?[] { V.Component(Counter, key: "same") }),
            });

        [Test]
        public void Given_TwoSiblingContainersHoldingTheSameComponentUnderOneExplicitKey_When_TheFirstContainersButtonIsClicked_Then_OnlyThatContainersOutputChanges()
        {
            // Arrange
            var (left, right) = MountTwoContainers(V.Component(SameKeyHost, key: "host"));

            // Act
            left.Q<Button>("inc").SimulateClick();

            // Assert — the mount-effect count is folded in so this reads two instances rather than one
            // instance whose single re-render happened to reach the first container.
            Assert.That((s_setups, TextIn(left), TextIn(right)), Is.EqualTo((2, "1", "0")));
        }

        [Component]
        private static VNode Marked(string tag)
        {
            var (mark, setMark) = Hooks.UseState(tag);
            s_marks.Add(setMark);
            Hooks.UseLayoutEffect(() => { s_setups++; return (Action)(() => s_cleanups++); }, Array.Empty<object>());
            return V.Label(name: mark, text: mark);
        }

        [Component]
        private static VNode ReorderHost()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            s_setSwapped = setSwapped;
            var first = V.Div(name: "ca", key: "ka", children: new VNode?[] { V.Component(Marked, "a") });
            var second = V.Div(name: "cb", key: "kb", children: new VNode?[] { V.Component(Marked, "b") });
            return V.Div(name: "shell", children: swapped
                ? new VNode?[] { second, first }
                : new VNode?[] { first, second });
        }

        [Test]
        public void Given_TwoKeyedContainersEachHoldingAnUnkeyedComponent_When_TheContainersAreReordered_Then_EachInstanceMovesWithItsContainer()
        {
            // Arrange — each instance is renamed away from its initial mark so the reorder can be attributed
            // to an instance rather than to the tag its declaration passes.
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ReorderHost, key: "host"));
            _mounted.FlushEffectsForTest();
            for (var i = 0; i < s_marks.Count; i++) s_marks[i].Invoke(i == 0 ? "first" : "second");
            _mounted.FlushStateForTest();

            // Act
            s_setSwapped.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the shell's order says the containers really swapped, and the mount-effect count says
            // neither instance was rebuilt to get there.
            Assert.That(
                (s_setups, Names(container.Q<VisualElement>("shell")),
                    Names(container.Q<VisualElement>("ca")), Names(container.Q<VisualElement>("cb"))),
                Is.EqualTo((2, "cb|ca", "first", "second")));
        }

        // Every member of one inline fiber's registry key except the identity, read off the registry's own
        // reverse index; the identity selects the rows instead. One row per fiber, in no particular order.
        private static List<(object? Parent, object? PortalScope, string Container, object? PositionKey)>
            InlineKeysFor(MountedTree mounted, string identityName)
        {
            var registry = mounted.Root.Reconciler.Context.ComponentRegistry;
            var field = registry.GetType().GetField(
                "_inlineFiberToKey", BindingFlags.NonPublic | BindingFlags.Instance);
            var rows = new List<(object?, object?, string, object?)>();
            foreach (DictionaryEntry entry in (IDictionary)field!.GetValue(registry)!)
            {
                var key = entry.Value!;
                var type = key.GetType();
                var identity = type.GetField("Item5")!.GetValue(key);
                if (identity?.ToString()?.Contains(identityName) != true) continue;
                rows.Add((type.GetField("Item1")!.GetValue(key),
                    type.GetField("Item2")!.GetValue(key),
                    ((VisualElement?)type.GetField("Item3")!.GetValue(key))?.name ?? "<null>",
                    type.GetField("Item4")!.GetValue(key)));
            }
            return rows;
        }

        [Test]
        public void Given_TwoSiblingContainersHoldingTheSameUnkeyedComponent_When_TheirRegistryKeysAreRead_Then_OnlyTheContainerSeparatesThem()
        {
            // Arrange
            MountTwoContainers(V.Component(TwoContainerHost, key: "host"));

            // Act
            var rows = InlineKeysFor(_mounted!, "Counter");

            // Assert — the three agreeing members are what say the walk position cannot separate these two
            // call sites: every other member of the key is equal, and the containers are not.
            Assert.That(
                (rows.Count,
                    rows.Select(r => r.Parent).Distinct().Count(),
                    rows.Select(r => r.PositionKey?.ToString()).Distinct().Count(),
                    rows.Select(r => r.PortalScope).Distinct().Count(),
                    string.Join("|", rows.Select(r => r.Container).OrderBy(n => n, StringComparer.Ordinal))),
                Is.EqualTo((2, 1, 1, 1, "left|right")));
        }

        // Phase 1 writes the component into the container the reconcile reaches FIRST and drops the one it
        // was in, so the arriving instance is mounted before the departing container leaves the tree. A
        // teardown reaches a fiber by the container it renders into, so the order decides whether the
        // departing subtree can take the arriving instance with it.
        [Component]
        private static VNode MoveThenTeardownHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(name: "outside", children: new VNode?[]
            {
                V.Div(name: "left", children: phase == 1
                    ? new VNode?[] { V.Component(Marked, "a") }
                    : Array.Empty<VNode?>()),
                phase == 0
                    ? V.Div(name: "right", children: new VNode?[] { V.Component(Marked, "a") })
                    : null,
            });
        }

        [Test]
        public void Given_AComponentWrittenIntoAnotherContainer_When_TheOneItLeftIsTornDownInTheSameRender_Then_TheArrivingInstanceIsFreshAndSurvivesTheTeardown()
        {
            // Arrange — the mark is changed before the move, so an element still named "b" afterwards would
            // be the departing instance carried across rather than a fresh mount.
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(MoveThenTeardownHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_marks[s_marks.Count - 1].Invoke("b");
            _mounted.FlushStateForTest();

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — the arriving container is folded in because a cleanup count of one is also what a
            // render that tore both instances down reports.
            Assert.That(
                (s_cleanups, Names(container.Q<VisualElement>("left"))),
                Is.EqualTo((1, "a")));
        }

        [Component]
        private static VNode Badge()
        {
            var theme = Hooks.UseContext(Theme);
            var (n, setN) = Hooks.UseState(0);
            s_setters.Add(setN);
            return V.Label(name: "out", text: $"{theme}:{n}");
        }

        [Component]
        private static VNode ProviderPerContainerHost()
            => V.Div(name: "shell", children: new VNode?[]
            {
                V.Div(name: "left", children: new VNode?[]
                {
                    V.Provider(Theme, "L", new VNode[] { V.Component(Badge) }),
                }),
                V.Div(name: "right", children: new VNode?[]
                {
                    V.Provider(Theme, "R", new VNode[] { V.Component(Badge) }),
                }),
            });

        // The separation this fixture pins reaches the reconcile and not the context spine, and the "L" in
        // the expectation below is that gap rather than the value this position should read. The spine
        // searches an ancestor's committed tree for the fiber it is re-rendering and resolves each
        // candidate under that fiber's own container, so both containers answer and it stops at the first
        // — FiberContextSpine's SpineWalk owns what closing it would cost. Whichever container the
        // committed tree reaches first is the one whose Providers every instance of that position then
        // reads; the instances are still separate, which is what the left container's own value says.
        [Test]
        public void Given_TwoContainersEachWithItsOwnProvider_When_TheSecondInstanceReRendersAlone_Then_ItReadsTheFirstContainersProvider()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ProviderPerContainerHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var left = container.Q<VisualElement>("left");
            var right = container.Q<VisualElement>("right");
            var atMount = (TextIn(left), TextIn(right));

            // Act — each instance re-renders alone, the second one first.
            s_setters[1].Invoke(7);
            _mounted.FlushStateForTest();
            s_setters[0].Invoke(5);
            _mounted.FlushStateForTest();

            // Assert — the mount reading is folded in rather than assumed, because it is what says the
            // wrong value below belongs to the spine rather than to the walk that commits these two; and
            // the first container's own re-render, because it reads correctly either way and a single
            // shared instance would leave it at its mount text instead of carrying 5.
            Assert.That(
                (atMount.Item1, atMount.Item2, TextIn(left), TextIn(right)),
                Is.EqualTo(("L:0", "R:0", "L:5", "L:7")));
        }

        // The mirror of the case above: the two Providers carry DIFFERENT contexts, so the first
        // container's supplies nothing the second container's instance reads. That instance is inside the
        // Theme Provider and reads it at mount, and reads the context default once it re-renders alone.
        // Both Providers sit at one position, which is what leaves the first container's instance
        // answering to the second's key; a Provider only one of them carried would put the two instances
        // at positions that no longer collide, and the spine would reach the right one.
        [Component]
        private static VNode ProvidersOfTwoContextsPerContainerHost()
            => V.Div(name: "shell", children: new VNode?[]
            {
                V.Div(name: "left", children: new VNode?[]
                {
                    V.Provider(Accent, "A", new VNode[] { V.Component(Badge) }),
                }),
                V.Div(name: "right", children: new VNode?[]
                {
                    V.Provider(Theme, "R", new VNode[] { V.Component(Badge) }),
                }),
            });

        // GREEN_ON_BASE(characterization): the base reads the same here, since its position key collides
        // for two components at one index of two containers whether or not a Provider encloses them. This
        // shape is what keeps the reading once the key stops colliding on the Provider level alone.
        [Test]
        public void Given_TwoContainersWhoseProvidersCarryDifferentContexts_When_TheSecondInstanceReRendersAlone_Then_ItReadsTheContextDefault()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ProvidersOfTwoContextsPerContainerHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var left = container.Q<VisualElement>("left");
            var right = container.Q<VisualElement>("right");
            var atMount = (TextIn(left), TextIn(right));

            // Act
            s_setters[0].Invoke(5);
            _mounted.FlushStateForTest();
            s_setters[1].Invoke(6);
            _mounted.FlushStateForTest();

            // Assert — the mount reading is folded in because reading "R" there is what makes the value
            // below a loss rather than a position that never had a Provider, and the first container's own
            // value because a single instance answering for both would leave it at its mount text.
            Assert.That(
                (atMount.Item1, atMount.Item2, TextIn(left), TextIn(right)),
                Is.EqualTo(("default:0", "R:0", "default:5", "default:6")));
        }

        // MotionContext.ActiveLabel reaches a descendant as a Provider does — FiberContextSpine's
        // PushMotionSubtree re-pushes it for an isolated re-render — so a Motion container is the same
        // shape as the two above with the label in place of the context value, and the consequence is
        // visible rather than only readable: the descendant animates to the wrong label.
        [Component]
        private static VNode LabelReader()
        {
            var label = Hooks.UseContext(MotionContext.ActiveLabel);
            var (n, setN) = Hooks.UseState(0);
            s_setters.Add(setN);
            return V.Label(name: "out", text: $"{label ?? "<null>"}:{n}");
        }

        [Component]
        private static VNode TwoMotionsHost()
            => V.Div(name: "shell", children: new VNode?[]
            {
                V.Motion(name: "left", animate: "alpha", children: new VNode?[] { V.Component(LabelReader) }),
                V.Motion(name: "right", animate: "beta", children: new VNode?[] { V.Component(LabelReader) }),
            });

        [Test]
        public void Given_TwoSiblingMotionContainersWithDifferentLabels_When_TheSecondsDescendantReRendersAlone_Then_ItReadsTheFirstsLabel()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(TwoMotionsHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var left = container.Q<VisualElement>("left");
            var right = container.Q<VisualElement>("right");
            var atMount = (TextIn(left), TextIn(right));

            // Act
            s_setters[1].Invoke(7);
            _mounted.FlushStateForTest();
            s_setters[0].Invoke(5);
            _mounted.FlushStateForTest();

            // Assert — the mount reading is folded in because reading "beta" there is what makes the value
            // below the wrong label rather than one this position never had, and the first container's own
            // value because a single instance answering for both would leave it at its mount text.
            Assert.That(
                (atMount.Item1, atMount.Item2, TextIn(left), TextIn(right)),
                Is.EqualTo(("alpha:0", "beta:0", "alpha:5", "alpha:7")));
        }

        // The occupant emits no poolable primitive of its own, so the only element this shape returns to
        // FiberPrimitiveElementPool is the container — which is what makes the identity reading below name
        // the container rather than whichever descendant the pool happened to hand back first.
        [Component]
        private static VNode PooledOccupant()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setters.Add(setN);
            Hooks.UseLayoutEffect(() => { s_setups++; return (Action)(() => s_cleanups++); }, Array.Empty<object>());
            return V.Div(name: "occupant-" + n);
        }

        [Component]
        private static VNode PooledContainerHost()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "outside", children: new VNode?[]
            {
                shown ? V.Button(name: "box", children: new VNode?[] { V.Component(PooledOccupant) }) : null,
            });
        }

        // GREEN_ON_BASE(characterization): the base has no container member for a pooled element to carry
        // into its next use, so it reads the same there. What it pins is that adding one did not give the
        // pool a way to hand a live registry entry to whatever rents the element next.
        [Test]
        public void Given_AContainerReturnedToThePool_When_ItIsRentedAgainForTheSamePosition_Then_ItsNextOccupantIsAFreshInstance()
        {
            // Arrange — the occupant is moved off its initial state, so an element still named for that
            // state after the round trip would be the previous instance resolved through the pooled element.
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(PooledContainerHost, key: "host"));
            _mounted.FlushEffectsForTest();
            var firstBox = container.Q<VisualElement>("box");
            s_setters[s_setters.Count - 1].Invoke(9);
            _mounted.FlushStateForTest();

            // Act — the container leaves the tree and comes back in the next render.
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            s_setShown.Invoke(true);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert — whether the pool handed back the same element is folded in rather than assumed: a
            // rented-fresh element would satisfy the rest of the reading without the reuse this case exists
            // to cover.
            var secondBox = container.Q<VisualElement>("box");
            Assert.That(
                (ReferenceEquals(firstBox, secondBox), s_setups, Names(secondBox)),
                Is.EqualTo((true, 2, "occupant-0")));
        }
    }
}
