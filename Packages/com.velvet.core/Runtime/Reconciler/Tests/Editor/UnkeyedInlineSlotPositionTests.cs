using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that an unkeyed inline component's instance follows the slot it occupies among its siblings,
    /// which is what a sibling that renders <c>null</c> costing its slot is one case of
    /// (<see cref="ConditionalSiblingSlotKeyTests"/> holds that one). Keying the slot by how many
    /// components of one identity the walk had passed instead tied it to what the walk had already seen:
    /// two different components swapping places kept each other's state rather than remounting, a fragment
    /// gaining a child handed the newcomer the next sibling's instance, and a component inside a
    /// <c>V.Suspense</c>'s children collided with the one at the same index of the body around it — which
    /// the duplicate-key guard answered by warning and dropping one of the two.
    /// </summary>
    [TestFixture]
    internal sealed class UnkeyedInlineSlotPositionTests
    {
        private static readonly List<string> s_setups = new();
        private static readonly List<string> s_cleanups = new();

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setups.Clear();
            s_cleanups.Clear();
        }

        private static VNode Body(string initial, string tag)
        {
            var (name, setName) = Hooks.UseState(initial);
            Hooks.UseEffect(() =>
            {
                s_setups.Add(initial);
                return (Action)(() => s_cleanups.Add(initial));
            }, Array.Empty<object>());
            return V.Button(name: "marked-" + initial, text: tag + name,
                onClick: () => setName.Invoke(name + "-renamed"));
        }

        [Component]
        private static VNode Marked(string initial) => Body(initial, string.Empty);

        [Component]
        private static VNode Other(string initial) => Body(initial, "B/");

        [Component]
        private static VNode SwapHost()
        {
            var (swap, setSwap) = Hooks.UseState(false);
            return V.Div(name: "root", children: new VNode?[]
            {
                V.Button(name: "swap", onClick: () => setSwap.Invoke(true)),
                V.Div(name: "host", children: swap
                    ? new VNode?[] { V.Component<string>(Other, "b1"), V.Component<string>(Marked, "a1") }
                    : new VNode?[] { V.Component<string>(Marked, "a1"), V.Component<string>(Other, "b1") }),
            });
        }

        [Component]
        private static VNode FragmentHost()
        {
            var (grow, setGrow) = Hooks.UseState(false);
            return V.Div(name: "root", children: new VNode?[]
            {
                V.Button(name: "grow", onClick: () => setGrow.Invoke(true)),
                V.Div(name: "host", children: new VNode?[]
                {
                    V.Fragment(grow
                        ? new VNode?[] { V.Component<string>(Marked, "f1"), V.Component<string>(Marked, "f2") }
                        : new VNode?[] { V.Component<string>(Marked, "f1") }),
                    V.Component<string>(Marked, "outer"),
                }),
            });
        }

        [Component]
        private static VNode SuspenseHost()
            => V.Div(name: "host", children: new VNode?[]
            {
                V.Component<string>(Marked, "plain"),
                V.Suspense(V.Label(name: "fallback"), new VNode?[] { V.Component<string>(Marked, "susp") }),
            });

        private string HostText() => string.Join("|",
            _root.Q<VisualElement>("host").Children().Select(child => ((Button)child).text));

        [Test]
        public void Given_TwoDifferentComponentsAmongSiblings_When_TheirOrderSwaps_Then_BothUnmount()
        {
            // Arrange — both mounted at their original slots.
            using var mounted = V.Mount(_root, V.Component(SwapHost, key: "host"));
            mounted.FlushEffectsForTest();
            var mountedInstances = string.Join(",", s_setups);

            // Act — the two swap places, so each lands on a slot the other's identity held.
            _root.Q<Button>("swap").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — a slot whose identity changed is a remount, so both of the old instances left. The
            // mount reading is folded in rather than assumed, so a tree that mounted one of the two
            // reports a failure here instead of the Inconclusive an assumption would.
            Assert.That(
                (mountedInstances, string.Join(",", s_cleanups)),
                Is.EqualTo(("a1,b1", "a1,b1")));
        }

        [Test]
        public void Given_TwoDifferentComponentsAmongSiblings_When_TheirOrderSwaps_Then_NeitherKeepsItsFormerState()
        {
            // Arrange — both mounted, and each has renamed its own state.
            using var mounted = V.Mount(_root, V.Component(SwapHost, key: "host"));
            mounted.FlushEffectsForTest();
            _root.Q<Button>("marked-a1").SimulateClick();
            _root.Q<Button>("marked-b1").SimulateClick();
            var beforeSwap = HostText();

            // Act
            _root.Q<Button>("swap").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — both remounted, so both show the state a fresh mount starts from. The arranged
            // reading is folded in: without the renames the fresh state is the state either outcome
            // shows, so this case would pass whatever the swap did.
            Assert.That(
                (beforeSwap, HostText()),
                Is.EqualTo(("a1-renamed|B/b1-renamed", "B/b1|a1")));
        }

        [Test]
        public void Given_AFragmentSiblingGainsAChild_When_ItRerenders_Then_TheFollowingSiblingKeepsItsState()
        {
            // Arrange — a fragment holding one instance, a sibling after it that renamed its own state.
            using var mounted = V.Mount(_root, V.Component(FragmentHost, key: "host"));
            mounted.FlushEffectsForTest();
            _root.Q<Button>("marked-outer").SimulateClick();
            var beforeGrowth = HostText();

            // Act — the fragment gains a second child of the same identity.
            _root.Q<Button>("grow").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the newcomer mounts in the fragment's own second slot; the sibling after the
            // fragment keeps both its slot and its state. The arranged reading is folded in: without the
            // rename the newcomer and the sibling render the same text either way.
            Assert.That(
                (beforeGrowth, HostText()),
                Is.EqualTo(("f1|outer-renamed", "f1|f2|outer-renamed")));
        }

        [Test]
        public void Given_AComponentInsideSuspenseAndOneBesideIt_When_Mounted_Then_TheyAreSeparateInstances()
        {
            // Arrange & Act — both sit at index 0 of their own child array, one of them the Suspense's.
            using var mounted = V.Mount(_root, V.Component(SuspenseHost, key: "host"));
            mounted.FlushEffectsForTest();

            // Assert — two slots, two instances: neither resolved to the other's fiber and got dropped as
            // a duplicate.
            Assert.That(HostText(), Is.EqualTo("plain|susp"));
        }
    }
}
