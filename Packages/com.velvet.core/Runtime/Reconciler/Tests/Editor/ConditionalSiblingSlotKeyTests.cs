using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which instance survives when a conditional sibling stops rendering. <c>cond ? node : null</c> is
    /// this framework's "render nothing", so a dropped sibling costs the slot it held: a following
    /// same-identity occurrence keeps its own slot and its own state, and the dropped one unmounts. Keying
    /// the slot by how many components of that identity the walk had passed shifted the ones after it onto
    /// their predecessors' keys instead, so the second instance re-bound onto the first's fiber and rendered
    /// the first one's state under the second one's props — with nothing remounting, and the unmount landing
    /// on the instance that should have survived. Wrapping each of the two in an element of its own is a
    /// different diff and still re-binds, unless the wrappers carry a <c>key:</c>; both readings are here.
    /// </summary>
    [TestFixture]
    internal sealed class ConditionalSiblingSlotKeyTests
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

        [Component]
        private static VNode Marked(string initial)
        {
            var (name, setName) = Hooks.UseState(initial);
            Hooks.UseEffect(() =>
            {
                s_setups.Add(initial);
                return (Action)(() => s_cleanups.Add(initial));
            }, Array.Empty<object>());
            return V.Button(name: "marked-" + initial, text: name,
                onClick: () => setName.Invoke(name + "-renamed"));
        }

        [Component]
        private static VNode Host()
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(name: "root", children: new VNode?[]
            {
                V.Button(name: "drop", onClick: () => setShow.Invoke(false)),
                V.Div(name: "host", children: new VNode?[]
                {
                    show ? V.Component<string>(Marked, "first") : null,
                    V.Component<string>(Marked, "second"),
                }),
            });
        }

        // The rendered state of every instance still in the host, in slot order — a count alone passes
        // against a survivor holding the wrong instance's state, and a state alone against a survivor
        // that kept a sibling alongside it.
        private string HostText() => string.Join("|",
            _root.Q<VisualElement>("host").Children().Select(child => ((Button)child).text));

        [Test]
        public void Given_TwoSameIdentitySiblings_When_TheFirstStopsRendering_Then_TheSecondKeepsItsOwnState()
        {
            // Arrange — both instances mounted, then the second renames its own state.
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            _root.Q<Button>("marked-second").SimulateClick();
            var beforeDrop = HostText();

            // Act — the first sibling turns to null.
            _root.Q<Button>("drop").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the survivor is the second instance, still holding what it renamed itself to. The
            // arranged reading is folded in rather than assumed: a rename that reached the wrong instance
            // would leave this case reporting on a tree it never set up.
            Assert.That((beforeDrop, HostText()), Is.EqualTo(("first|second-renamed", "second-renamed")));
        }

        [Test]
        public void Given_TwoSameIdentitySiblings_When_TheFirstStopsRendering_Then_TheFirstIsTheOneThatUnmounts()
        {
            // Arrange — both instances mounted, each having recorded its own effect setup.
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            mounted.FlushEffectsForTest();
            var mountedInstances = string.Join(",", s_setups);

            // Act
            _root.Q<Button>("drop").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the dropped sibling's own effect cleanup ran, and nobody else's. The mount reading
            // is folded in rather than assumed: a tree that mounted one instance would report the same
            // single cleanup.
            Assert.That(
                (mountedInstances, string.Join(",", s_cleanups)),
                Is.EqualTo(("first,second", "first")));
        }

        [Component]
        private static VNode WrappedHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(name: "root", children: new VNode?[]
            {
                V.Button(name: "drop", onClick: () => setShow.Invoke(false)),
                V.Div(name: "host", children: new VNode?[]
                {
                    show
                        ? V.Div(name: "w1", children: new VNode?[] { V.Component<string>(Marked, "first") })
                        : null,
                    V.Div(name: "w2", children: new VNode?[] { V.Component<string>(Marked, "second") }),
                }),
            });
        }

        [Component]
        private static VNode KeyedWrappedHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(name: "root", children: new VNode?[]
            {
                V.Button(name: "drop", onClick: () => setShow.Invoke(false)),
                V.Div(name: "host", children: new VNode?[]
                {
                    show
                        ? V.Div(name: "w1", key: "w1",
                            children: new VNode?[] { V.Component<string>(Marked, "first") })
                        : null,
                    V.Div(name: "w2", key: "w2",
                        children: new VNode?[] { V.Component<string>(Marked, "second") }),
                }),
            });
        }

        // Each surviving wrapper's own name paired with the state of the instance under it, so a reading
        // names both which wrapper the diff kept and whose state came with it.
        private string WrappedHostText() => string.Join("|",
            _root.Q<VisualElement>("host").Children()
                .Select(wrapper => wrapper.name + "(" + ((Button)wrapper[0]).text + ")"));

        // GREEN_ON_BASE(characterization): the element diff matches unkeyed siblings by position once the
        // null has been dropped from the child list, which the component slot key does not reach.
        [Test]
        public void Given_AConditionalSiblingWrappedInAnUnkeyedElement_When_ItStopsRendering_Then_TheSurvivorReBindsWithThatElement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(WrappedHost, key: "host"));
            mounted.FlushEffectsForTest();
            _root.Q<Button>("marked-second").SimulateClick();
            var beforeDrop = WrappedHostText();

            // Act
            _root.Q<Button>("drop").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert — the surviving wrapper took the departing one's element, and the component under it
            // came along: the second wrapper's props over the first wrapper's instance. The arranged
            // reading is folded in for the reason the case above states.
            Assert.That(
                (beforeDrop, WrappedHostText()),
                Is.EqualTo(("w1(first)|w2(second-renamed)", "w2(first)")));
        }

        // GREEN_ON_BASE(characterization): keyed element siblings already matched by key on the base; this
        // is the remedy the migration guide offers for the case above.
        [Test]
        public void Given_AConditionalSiblingWrappedInAKeyedElement_When_ItStopsRendering_Then_TheSurvivorKeepsItsOwnInstance()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(KeyedWrappedHost, key: "host"));
            mounted.FlushEffectsForTest();
            _root.Q<Button>("marked-second").SimulateClick();
            var beforeDrop = WrappedHostText();

            // Act
            _root.Q<Button>("drop").SimulateClick();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(
                (beforeDrop, WrappedHostText()),
                Is.EqualTo(("w1(first)|w2(second-renamed)", "w2(second-renamed)")));
        }

    }
}
