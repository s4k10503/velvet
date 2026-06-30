using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// "Preserving and resetting state" semantics for the KEYED-SIBLING reconcile path:
    /// component state is tied to its position in
    /// the tree, identified by element type + key. The existing suites cover the neighbouring cases — host
    /// instance reuse / DOM moves on reorder (host elements only), and single-component same-key preserve /
    /// key-change reset / type-change reset — but never the two seams pinned here, both of which sit on top of
    /// keyed component reconciliation:
    /// <list type="bullet">
    /// <item>A REORDER of stateful keyed components reuses each key's fiber and carries its hook state to the
    /// new position (only the order changes; no key's count is lost or swapped onto another key).</item>
    /// <item>When one of several keyed siblings has its key changed while the others are left in place, only the
    /// re-keyed sibling unmounts and remounts fresh; the key-stable siblings keep their state.</item>
    /// </list>
    /// All are driven through a real discrete click (<see cref="Button.SimulateClick"/>), which commits
    /// synchronously, so no manual drain is needed. GWT, one assert per case. These record the expected behaviour as
    /// the expected value, so if either turns RED it is a Velvet divergence (state leaking across a key on
    /// reorder, or a key-stable sibling getting reset alongside a re-keyed neighbour).
    /// </summary>
    [TestFixture]
    internal sealed class KeyedComponentStateParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        // A stateful child whose props carry a stable display name, so each keyed instance exposes a
        // uniquely-named button/label that survives reordering (the name follows the fiber, the key follows the
        // identity). Its own UseState count lives on its ComponentFiber.

        private sealed record ChildProps(string Name);

        [Component]
        private static VNode NamedCounter(ChildProps props)
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: $"inc-{props.Name}", onClick: () => setCount.Invoke(c => c + 1)),
                V.Label(name: $"out-{props.Name}", text: count.ToString()),
            });
        }

        // --- Gap 1: a reorder reuses each key's fiber and keeps its hook state, moving only the position ---

        [Component]
        private static VNode ReorderParent()
        {
            var (reordered, setReordered) = Hooks.UseState(false);
            var a = V.Component(NamedCounter, new ChildProps("a"), key: "a");
            var b = V.Component(NamedCounter, new ChildProps("b"), key: "b");
            var c = V.Component(NamedCounter, new ChildProps("c"), key: "c");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "reorder", onClick: () => setReordered.Invoke(_ => true)),
                reordered
                    ? V.Fragment(new VNode[] { c, a, b })
                    : V.Fragment(new VNode[] { a, b, c }),
            });
        }

        [Test]
        public void Given_KeyedStatefulComponents_When_Reordered_Then_EachKeepsItsOwnHookState()
        {
            // Arrange — three keyed counters advanced independently to a=3, b=7, c=0.
            using var mounted = V.Mount(_root, V.Component(ReorderParent, key: "reorder-parent"));
            for (var i = 0; i < 3; i++) _root.Q<Button>("inc-a").SimulateClick();
            for (var i = 0; i < 7; i++) _root.Q<Button>("inc-b").SimulateClick();
            Assume.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text, _root.Q<Label>("out-c").text),
                Is.EqualTo(("3", "7", "0")),
                "Precondition: each counter advanced independently before reordering");

            // Act — the parent re-renders the siblings in a new order [c, a, b].
            _root.Q<Button>("reorder").SimulateClick();

            // Assert — each key reused its fiber, so its count follows it to the new position (none lost or swapped).
            Assert.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text, _root.Q<Label>("out-c").text),
                Is.EqualTo(("3", "7", "0")),
                "A reorder carries each keyed component's hook state to its new position.");
        }

        // --- Gap 2: re-keying one of several siblings remounts only that one; key-stable siblings persist ---

        [Component]
        private static VNode SelectiveReKeyParent()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            var first = V.Component(NamedCounter, new ChildProps("a"), key: "a");
            var second = swapped
                ? V.Component(NamedCounter, new ChildProps("z"), key: "z")
                : V.Component(NamedCounter, new ChildProps("b"), key: "b");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "rekey", onClick: () => setSwapped.Invoke(_ => true)),
                V.Fragment(new VNode[] { first, second }),
            });
        }

        [Test]
        public void Given_TwoKeyedSiblings_When_OneKeyChanges_Then_OnlyThatSiblingRemountsFresh()
        {
            // Arrange — two keyed siblings advanced to a=5, b=9.
            using var mounted = V.Mount(_root, V.Component(SelectiveReKeyParent, key: "rekey-parent"));
            for (var i = 0; i < 5; i++) _root.Q<Button>("inc-a").SimulateClick();
            for (var i = 0; i < 9; i++) _root.Q<Button>("inc-b").SimulateClick();
            Assume.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-b").text),
                Is.EqualTo(("5", "9")),
                "Precondition: both siblings advanced before the re-key");

            // Act — only the second sibling's key changes (b -> z); the first sibling's key (a) is left in place.
            _root.Q<Button>("rekey").SimulateClick();

            // Assert — the key-stable sibling keeps its state (a=5) while only the re-keyed one remounts fresh (z=0).
            Assert.That(
                (_root.Q<Label>("out-a").text, _root.Q<Label>("out-z").text),
                Is.EqualTo(("5", "0")),
                "Re-keying one sibling resets only that sibling; key-stable siblings preserve their state.");
        }
    }
}
