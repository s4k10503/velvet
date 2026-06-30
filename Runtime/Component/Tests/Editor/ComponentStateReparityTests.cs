using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// "Preserving and resetting state" behavior:
    /// component state is tied to its position in the tree, identified by element type + key.
    /// <list type="bullet">
    /// <item>Same component at the same position+key PRESERVES state across a parent re-render.</item>
    /// <item>Changing the HOST ELEMENT type at a position (Div -> Button) RESETS the nested subtree's state
    /// (a type change remounts what sits at that position).</item>
    /// <item>The SAME component at the same position but a DIFFERENT key RESETS state (key is identity).</item>
    /// <item>A conditional slot that swaps to a DIFFERENT component type unmounts the old and mounts the new fresh.</item>
    /// </list>
    /// All are driven through a real discrete click (<see cref="Button.SimulateClick"/>), which commits
    /// synchronously, so no manual drain is needed. GWT, one assert per case. (These pin RENDER-side parity; the
    /// orthogonal concern that a torn-down inline child's fiber is also DISPOSED — not leaked — is covered by
    /// <c>InlineFiberTeardownLeakTests</c>.)
    /// </summary>
    [TestFixture]
    internal sealed class ComponentStateReparityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_aMountCount = 0;
            s_bMountCount = 0;
        }

        private static int s_aMountCount;
        private static int s_bMountCount;

        // --- Case 1: same component + stable key preserves state across a parent re-render ---

        [Component]
        private static VNode PreserveChild()
        {
            var (n, setN) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "child-inc", onClick: () => setN.Invoke(v => v + 1)),
                V.Label(name: "child-out", text: n.ToString()),
            });
        }

        [Component]
        private static VNode PreserveParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "parent-tick", onClick: () => setTick.Invoke(t => t + 1)),
                V.Label(name: "parent-out", text: tick.ToString()),
                V.Component(PreserveChild, key: "child"),
            });
        }

        [Test]
        public void Given_AChildWithAdvancedState_When_TheParentReRendersWithTheChildAtTheSameKey_Then_TheChildsStateIsPreserved()
        {
            // Arrange — a child advanced to 1.
            using var mounted = V.Mount(_root, V.Component(PreserveParent, key: "preserve"));
            _root.Q<Button>("child-inc").SimulateClick();
            Assume.That(_root.Q<Label>("child-out").text, Is.EqualTo("1"), "Precondition: child advanced to 1");

            // Act — the parent re-renders (its own unrelated state changes), keeping the child at the same key.
            _root.Q<Button>("parent-tick").SimulateClick();

            // Assert — the child fiber was reused, so its state survives the parent re-render.
            Assert.That(_root.Q<Label>("child-out").text, Is.EqualTo("1"),
                "A component at a stable position+key keeps its state across a parent re-render.");
        }

        // --- Case 2 (DIVERGENCE, recorded): host element type change resets a nested component ---

        [Component]
        private static VNode TypeSwapNestedChild()
        {
            var (n, setN) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "nested-inc", onClick: () => setN.Invoke(v => v + 1)),
                V.Label(name: "nested-out", text: n.ToString()),
            });
        }

        [Component]
        private static VNode TypeSwapResetParent()
        {
            var (asButton, setAsButton) = Hooks.UseState(false);
            var child = V.Component(TypeSwapNestedChild, key: "nested");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "type-swap", onClick: () => setAsButton.Invoke(b => !b)),
                asButton
                    ? V.Button(key: "host", children: new VNode[] { child })
                    : V.Div(key: "host", children: new VNode[] { child }),
            });
        }

        [Test]
        public void Given_ANestedComponentWithAdvancedState_When_TheHostElementTypeChanges_Then_TheNestedComponentRemountsWithFreshState()
        {
            // Arrange — a nested child (under a Div host) advanced to 1.
            using var mounted = V.Mount(_root, V.Component(TypeSwapResetParent, key: "type-reset"));
            _root.Q<Button>("nested-inc").SimulateClick();
            Assume.That(_root.Q<Label>("nested-out").text, Is.EqualTo("1"), "Precondition: nested child advanced to 1");

            // Act — the host element type flips Div -> Button at the same key (a type change at this position).
            _root.Q<Button>("type-swap").SimulateClick();

            // Assert — a subtree resets when the element type at a position changes: the nested child remounts
            // fresh, so its state returns to 0.
            Assert.That(_root.Q<Label>("nested-out").text, Is.EqualTo("0"),
                "A host element type change at the same position remounts the nested component with fresh state.");
        }

        // --- Case 3: changing a component's key resets its state ---

        [Component]
        private static VNode KeyedChild()
        {
            var (n, setN) = Hooks.UseState(0);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "keyed-inc", onClick: () => setN.Invoke(v => v + 1)),
                V.Label(name: "keyed-out", text: n.ToString()),
            });
        }

        [Component]
        private static VNode KeySwapParent()
        {
            var (key, setKey) = Hooks.UseState("key-a");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "swap-key", onClick: () => setKey.Invoke(_ => "key-b")),
                V.Component(KeyedChild, key: key),
            });
        }

        [Test]
        public void Given_AComponentWithAdvancedState_When_ItsKeyChanges_Then_ItRemountsWithFreshState()
        {
            // Arrange — a keyed child advanced to 1.
            using var mounted = V.Mount(_root, V.Component(KeySwapParent, key: "key-swap"));
            _root.Q<Button>("keyed-inc").SimulateClick();
            Assume.That(_root.Q<Label>("keyed-out").text, Is.EqualTo("1"), "Precondition: child advanced to 1");

            // Act — the child's key changes (key-a -> key-b) at the same position.
            _root.Q<Button>("swap-key").SimulateClick();

            // Assert — the key change is an identity change: the child remounts fresh, resetting state to 0.
            Assert.That(_root.Q<Label>("keyed-out").text, Is.EqualTo("0"),
                "Changing a component's key unmounts the old instance and mounts a fresh one (state resets).");
        }

        // --- Case 4 (case 5 in the audit): conditional swap to a different component mounts it fresh ---

        [Component]
        private static VNode ConditionalA()
        {
            s_aMountCount++;
            return V.Label(name: "cond-a", text: "A");
        }

        [Component]
        private static VNode ConditionalB()
        {
            s_bMountCount++;
            return V.Label(name: "cond-b", text: "B");
        }

        [Component]
        private static VNode ConditionalSwapParent()
        {
            var (showA, setShowA) = Hooks.UseState(true);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "flip", onClick: () => setShowA.Invoke(s => !s)),
                showA ? V.Component(ConditionalA) : V.Component(ConditionalB),
            });
        }

        [Test]
        public void Given_AConditionalSlotShowingOneComponent_When_ItFlipsToADifferentComponent_Then_TheNewComponentMountsFresh()
        {
            // Arrange — the slot shows component A (mounted once, B never).
            using var mounted = V.Mount(_root, V.Component(ConditionalSwapParent, key: "cond-swap"));
            Assume.That((s_aMountCount, s_bMountCount), Is.EqualTo((1, 0)), "Precondition: A mounted once, B not yet");

            // Act — the condition flips, replacing A with the different component B at the same slot.
            _root.Q<Button>("flip").SimulateClick();

            // Assert — B mounts exactly once (a fresh mount, not a patch/reuse of A's fiber).
            Assert.That(s_bMountCount, Is.EqualTo(1),
                "A different component type at the same conditional slot mounts fresh rather than reusing the old fiber.");
        }
    }
}
