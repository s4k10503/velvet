using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Parity coverage for the <c>cond ? node : null</c> conditional-render idiom at the reconciler boundary,
    /// driven through a REAL discrete click (<see cref="Button.SimulateClick"/>) so the toggle commits inside the
    /// event boundary the way production takes. A <c>null</c> child renders as nothing: it produces no host node and
    /// leaves no placeholder, and flipping the condition mounts/unmounts only that child while its siblings keep
    /// their instances. These pin: a false <c>null</c> child creates no host element and is mounted from nothing
    /// when the condition turns true; and a KEYED child nulled out among KEYED siblings is the only one removed,
    /// with the neighbours reused (same instance). Identity preservation across a middle removal is the keyed
    /// guarantee — unkeyed siblings reconcile by position and would re-purpose the trailing node instead, which is
    /// the very lesson keys exist to fix. GWT, one assert per case. If a case goes RED, Velvet diverges from the expected
    /// semantics (e.g. a false <c>null</c> child leaving an empty placeholder element, or a keyed sibling being re-created
    /// instead of preserved).
    /// </summary>
    [TestFixture]
    internal sealed class ConditionalNullChildParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setShow = default;
        }

        private static StateUpdater<bool> s_setShow;

        // A parent that always renders one Label and conditionally renders a second via `show ? node : null`.

        [Component]
        private static VNode OptionalTailParent()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_setShow = setShow;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Label(name: "always", text: "A"),
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show ? V.Label(name: "optional", text: "B") : null,
            });
        }

        [Test]
        public void Given_ConditionalNullChild_When_ConditionFlipsToTrue_Then_ChildMountsFromNothing()
        {
            // Arrange — a parent whose conditional child is currently `null` (show=false), so no host node exists for it.
            using var mounted = V.Mount(_root, V.Component(OptionalTailParent, key: "parent"));
            Assume.That(_root.Q<Label>("optional"), Is.Null, "Precondition: the null child created no host node");

            // Act — a click flips the condition to true.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — the previously-null child is mounted from nothing.
            Assert.IsNotNull(_root.Q<Label>("optional"));
        }

        // A parent whose three KEYED label siblings include a conditional middle child. Keys give the siblings a
        // stable identity, so removing the middle one preserves the neighbours' instances — the guarantee unkeyed
        // positional reconciliation does not provide (there the trailing node is re-purposed instead).

        [Component]
        private static VNode SandwichParent()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                V.Fragment(new VNode[]
                {
                    V.Label(key: "a", name: "a", text: "A"),
                    show ? V.Label(key: "b", name: "b", text: "B") : null,
                    V.Label(key: "c", name: "c", text: "C"),
                }),
            });
        }

        [Test]
        public void Given_ConditionalChildAmongSiblings_When_ConditionFlipsToFalse_Then_OnlyThatChildUnmounts()
        {
            // Arrange — a parent rendering siblings A, B, C with B as the conditional child (show=true), recording A's and C's instances.
            using var mounted = V.Mount(_root, V.Component(SandwichParent, key: "parent"));
            var aBefore = _root.Q<Label>("a");
            var cBefore = _root.Q<Label>("c");
            Assume.That(_root.Q<Label>("b"), Is.Not.Null, "Precondition: the conditional child B is mounted");

            // Act — a click nulls out B (show=false).
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — B is gone and only B is gone: the surviving Label siblings are exactly the original A and C instances.
            Assert.That(
                _root.Query<Label>().ToList(),
                Is.EqualTo(new[] { aBefore, cBefore }),
                "B removed; A and C preserved as the same instances");
        }
    }
}
