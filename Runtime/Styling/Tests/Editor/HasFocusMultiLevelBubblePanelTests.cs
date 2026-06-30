using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Multi-level bubble fidelity for the focus-within form <c>has-[:focus]:</c>. Real focus moving to a
    /// GRANDCHILD makes the panel's focus controller dispatch a <see cref="FocusInEvent"/> that bubbles
    /// grandchild → child → parent, so EVERY ancestor whose manipulator listens reacts in one focus change —
    /// the chained propagation a single-element off-panel <c>SimulateBubbledEvent</c> (one currentTarget at a
    /// time) cannot reproduce. So these mount in a real <see cref="UnityEditor.EditorWindow"/> panel and drive focus with
    /// the real controller (<c>Focus()</c>), unlike the off-panel <c>HasVariantTests</c>. Each test gates on the
    /// environment actually granting focus (batchmode does not always hand an off-screen window real focus), so
    /// it goes Inconclusive rather than falsely failing where focus cannot be taken. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class HasFocusMultiLevelBubblePanelTests : PanelTestBase
    {
        // outer ⊃ middle ⊃ {leaf, leaf2}, with has-[:focus]: on both ancestors (the token is consumed as a
        // variant, so the ancestors are queried by name and assert via their payload class bg-outer / bg-middle).
        // `leaf2` is a second focusable INSIDE the subtree (for the internal focus-hop case) and `outside` is a
        // focusable sibling OUTSIDE the subtree (to move focus away and exercise the bubbling clear).
        private (VisualElement outer, VisualElement middle, Button leaf, Button leaf2, Button outside) MountChain()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "container", children: new VNode[]
            {
                V.Div(name: "outer", className: "has-[:focus]:bg-outer", children: new VNode[]
                {
                    V.Div(name: "middle", className: "has-[:focus]:bg-middle", children: new VNode[]
                    {
                        V.Button(name: "leaf"),
                        V.Button(name: "leaf2"),
                    }),
                }),
                V.Button(name: "outside"),
            }));
            var root = _window.rootVisualElement;
            return (root.Q<VisualElement>("outer"), root.Q<VisualElement>("middle"),
                root.Q<Button>("leaf"), root.Q<Button>("leaf2"), root.Q<Button>("outside"));
        }

        // Drives focus onto the element as deterministically as batchmode allows: focus the window, call
        // Focus(), then force the panel update that commits the pending focus change (mirrors the repo's
        // FocusLossDuringCommitTests.MountAndFocus). Returns whether the environment actually granted the focus.
        private bool DriveFocus(Button target)
        {
            _window.Focus();
            target.Focus();
            ForcePanelUpdate(target.panel);
            return ReferenceEquals(target.focusController?.focusedElement, target);
        }

        [Test]
        public void Given_NestedHasFocusAncestors_When_AGrandchildGainsFocus_Then_TheTopAncestorLightsUp()
        {
            // Arrange — outer ⊃ middle ⊃ leaf, both ancestors carrying has-[:focus]:, none lit.
            var (outer, _, leaf, _, _) = MountChain();
            Assume.That(outer.ClassListContains("bg-outer"), Is.False, "Precondition: the top ancestor is unlit");

            // Act — real focus moves to the deepest leaf (gated: skip where the environment denies focus).
            Assume.That(DriveFocus(leaf), Is.True, "Precondition: the environment granted focus to the leaf");

            // Assert — the focus-in bubbled the full chain (leaf → middle → outer), lighting the TOP ancestor.
            Assert.IsTrue(outer.ClassListContains("bg-outer"));
        }

        [Test]
        public void Given_NestedHasFocusAncestors_When_AGrandchildGainsFocus_Then_TheIntermediateAncestorLightsUp()
        {
            // Arrange — same chain; the intermediate ancestor is unlit.
            var (_, middle, leaf, _, _) = MountChain();
            Assume.That(middle.ClassListContains("bg-middle"), Is.False, "Precondition: the intermediate ancestor is unlit");

            // Act — the same real focus into the leaf.
            Assume.That(DriveFocus(leaf), Is.True, "Precondition: the environment granted focus to the leaf");

            // Assert — the intermediate level on the bubble path also reacts to the one focus change.
            Assert.IsTrue(middle.ClassListContains("bg-middle"));
        }

        [Test]
        public void Given_BothAncestorsLitByGrandchildFocus_When_FocusLeavesTheSubtree_Then_TheTopAncestorClears()
        {
            // Arrange — the grandchild is focused, so the whole ancestor chain is lit.
            var (outer, _, leaf, _, outside) = MountChain();
            Assume.That(DriveFocus(leaf), Is.True, "Precondition: the environment granted focus to the leaf");
            Assume.That(outer.ClassListContains("bg-outer"), Is.True, "Precondition: the top ancestor is lit while focused");

            // Act — focus moves to a sibling outside the subtree (the leaf's focus-out names no in-subtree successor).
            DriveFocus(outside);

            // Assert — the bubbling focus-out clears the top ancestor too.
            Assert.IsFalse(outer.ClassListContains("bg-outer"));
        }

        [Test]
        public void Given_BothAncestorsLitByGrandchildFocus_When_FocusLeavesTheSubtree_Then_TheIntermediateAncestorClears()
        {
            // Arrange — the grandchild is focused, so the whole ancestor chain is lit.
            var (_, middle, leaf, _, outside) = MountChain();
            Assume.That(DriveFocus(leaf), Is.True, "Precondition: the environment granted focus to the leaf");
            Assume.That(middle.ClassListContains("bg-middle"), Is.True, "Precondition: the intermediate ancestor is lit while focused");

            // Act — focus moves outside the subtree.
            DriveFocus(outside);

            // Assert — the bubbling focus-out clears the intermediate ancestor too.
            Assert.IsFalse(middle.ClassListContains("bg-middle"));
        }

        [Test]
        public void Given_BothAncestorsLitByGrandchildFocus_When_FocusHopsToASiblingStillInsideTheSubtree_Then_TheTopAncestorStaysLit()
        {
            // Arrange — leaf focused, the chain lit; a second focusable lives inside the same subtree.
            var (outer, _, leaf, leaf2, _) = MountChain();
            Assume.That(DriveFocus(leaf), Is.True, "Precondition: the environment granted focus to the leaf");
            Assume.That(outer.ClassListContains("bg-outer"), Is.True, "Precondition: the top ancestor is lit while focused");

            // Act — focus hops to a sibling still inside the subtree (focus-out names an in-subtree successor).
            DriveFocus(leaf2);

            // Assert — focus-within is kept across the internal hop (the manipulator's relatedTarget KEEP branch).
            Assert.IsTrue(outer.ClassListContains("bg-outer"));
        }
    }
}
