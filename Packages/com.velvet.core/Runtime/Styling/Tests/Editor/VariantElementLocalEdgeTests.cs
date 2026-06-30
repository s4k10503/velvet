using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Characterization coverage for the harder-to-reach corners of the element-local variant manipulator
    /// (<c>hover:</c> / <c>focus:</c> / <c>focus-visible:</c> / <c>active:</c> / <c>checked:</c>): the active
    /// release paths that do NOT arrive via a matching pointer-up (a pointer-out past the bounds, a pointer
    /// cancel), the bounds-gated persistence while crossing onto a descendant, the element-local gate on
    /// <c>checked:</c> against a bubbling descendant change, the one-shot nature of the pointer-focus
    /// suppression, and the payload-swap / removal lifecycle. These pin the CURRENT behaviour so a refactor
    /// keeps it. The bounds-dependent cases mount in a real <see cref="UnityEditor.EditorWindow"/> panel
    /// (<see cref="PanelTestBase"/>) with a forced layout so <c>worldBound</c> resolves; the rest run off panel
    /// through the callback registry. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class VariantElementLocalEdgeTests
    {
        /// <summary>
        /// Bounds-dependent active-release cases. <c>active:</c> is cleared on a pointer-out only once the
        /// pointer is actually outside the element's <c>worldBound</c>; while it merely crosses onto a
        /// descendant (still inside) the active payload is kept. Needs a laid-out panel so <c>worldBound</c> is
        /// real, so it sits in its own panel-backed nested fixture.
        /// </summary>
        [TestFixture]
        internal sealed class BoundsGated : PanelTestBase
        {
            // A leaf carrying active:scale-down whose own child fills it, so a pointer-out dispatched at the
            // child can carry either an inside or an outside position. The leaf is the element with the manipulator.
            private (VisualElement leaf, VisualElement child) Mount()
            {
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "w-[400px] h-[200px] active:scale-down",
                    V.Label(name: "child", className: "w-[400px] h-[200px]", text: "x")));
                ForcePanelUpdate(_window.rootVisualElement.panel);
                var child = _window.rootVisualElement.Q<Label>("child");
                return (child.parent, child);
            }

            private static void DownOn(VisualElement on)
            {
                using var evt = PointerDownEvent.GetPooled();
                evt.target = on;
                on.SendEvent(evt);
            }

            private static void OutAt(VisualElement on, Vector2 pos)
            {
                using var evt = PointerOutEvent.GetPooled();
                typeof(PointerEventBase<PointerOutEvent>).GetProperty("position", BindingFlags.Public | BindingFlags.Instance)!
                    .GetSetMethod(nonPublic: true)!.Invoke(evt, new object[] { (Vector3)pos });
                evt.target = on;
                on.SendEvent(evt);
            }

            [Test]
            public void Given_APressedLeaf_When_ThePointerLeavesItsBoundsWithoutAPointerUp_Then_TheActivePayloadIsRemoved()
            {
                // Arrange — a leaf pressed (active on) by a pointer-down on it.
                var (leaf, _) = Mount();
                DownOn(leaf);
                Assume.That(leaf.ClassListContains("scale-down"), Is.True, "Precondition: active on while pressed");

                // Act — the pointer leaves the bounds entirely; no PointerUp arrives.
                OutAt(leaf, leaf.worldBound.center + new Vector2(10000f, 10000f));

                // Assert — releasing outside the bounds ends the active state.
                Assert.IsFalse(leaf.ClassListContains("scale-down"));
            }

            [Test]
            public void Given_APressedLeaf_When_ThePointerCrossesOntoADescendantStillInsideBounds_Then_TheActivePayloadIsKept()
            {
                // Arrange — a leaf pressed (active on); its child fills the interior.
                var (leaf, child) = Mount();
                DownOn(leaf);
                Assume.That(leaf.ClassListContains("scale-down"), Is.True, "Precondition: active on while pressed");

                // Act — a bubbling pointer-out fires but the pointer is still inside the leaf's bounds (over the child).
                OutAt(child, leaf.worldBound.center);

                // Assert — active persists while the pointer remains within bounds.
                Assert.IsTrue(leaf.ClassListContains("scale-down"));
            }
        }

        /// <summary>
        /// Off-panel cases that exercise the callback registry directly. No layout / <c>worldBound</c> is read,
        /// so a bare detached leaf is enough and events go through <c>SimulateEvent</c> / <c>SimulateBubbledEvent</c>.
        /// </summary>
        [TestFixture]
        internal sealed class OffPanel
        {
            private VisualElement _root;
            private MountedTree _mounted;

            [SetUp]
            public void SetUp() => _root = new VisualElement();

            [TearDown]
            public void TearDown()
            {
                _mounted?.Dispose();
                _mounted = null;
            }

            private Label MountLeaf(string className)
            {
                _mounted = V.Mount(_root, V.Label(name: "leaf", className: className, text: "x"));
                return _root.Q<Label>("leaf");
            }

            private Toggle MountToggle(string className)
            {
                _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: className));
                return _root.Q<Toggle>("leaf");
            }

            // The live StyleVariantManipulator the reconciler attached to a mounted element, fetched from the
            // context tracking map so direct-API edges (UpdatePayloads / manipulator removal) can be driven.
            private StyleVariantManipulator ManipulatorOf(VisualElement element)
                => _mounted.Root.Reconciler.Context.VariantManipulators[element];

            [Test]
            public void Given_APressedLeaf_When_ThePointerIsCancelled_Then_TheActivePayloadIsRemoved()
            {
                // Arrange — a leaf pressed (active on) by a pointer-down.
                var leaf = MountLeaf("active:scale-down");
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("scale-down"), Is.True, "Precondition: active on while pressed");

                // Act — the gesture is cancelled (no PointerUp; e.g. an OS-level interruption).
                using (var evt = PointerCancelEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — a cancel ends the active state just like a release.
                Assert.IsFalse(leaf.ClassListContains("scale-down"));
            }

            [Test]
            public void Given_ACheckedVariantToggle_When_ADescendantBubblesAChange_Then_TheCheckedPayloadIsUnchanged()
            {
                // Arrange — a Toggle with checked:bg-hot, unchecked, plus a detached descendant to act as the
                // bubbling change's origin.
                var toggle = MountToggle("checked:bg-hot");
                var descendant = new VisualElement();
                Assume.That(toggle.ClassListContains("bg-hot"), Is.False, "Precondition: payload off before any change");

                // Act — a ChangeEvent<bool> arrives at the toggle but originated at a DESCENDANT control (target != toggle).
                using (var evt = ChangeEvent<bool>.GetPooled(false, true)) toggle.SimulateBubbledEvent(evt, descendant);

                // Assert — element-local checked: ignores a descendant's change, so the payload stays off.
                Assert.IsFalse(toggle.ClassListContains("bg-hot"));
            }

            [Test]
            public void Given_AFocusVisibleLeafAfterAPointerFocusCycle_When_ItIsRefocusedWithoutAPointerDown_Then_ThePayloadIsApplied()
            {
                // Arrange — drive one full pointer-focus cycle so the pointer-focus suppression is consumed:
                // pointer-down (arms suppression) → focus (consumed, no focus-visible) → blur.
                var leaf = MountLeaf("focus-visible:ring-kbd");
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);
                using (var evt = BlurEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("ring-kbd"), Is.False, "Precondition: pointer focus did not light focus-visible");

                // Act — a SECOND focus with no preceding pointer-down (keyboard re-focus); suppression was one-shot.
                using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — the suppression reset on the prior focus, so this keyboard focus lights focus-visible.
                Assert.IsTrue(leaf.ClassListContains("ring-kbd"));
            }

            [Test]
            public void Given_AHoveredLeaf_When_ItsHoverPayloadStringIsSwapped_Then_TheOldClassIsRemoved()
            {
                // Arrange — a hovered leaf so its current hover payload (bg-hot) is applied.
                var leaf = MountLeaf("hover:bg-hot");
                using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: old hover payload on while hovered");

                // Act — the className changes so the hover payload becomes bg-cold (re-applied under the new set).
                ManipulatorOf(leaf).UpdatePayloads(new[] { "bg-cold" }, null, null, null, null);

                // Assert — the previously-applied old payload class is cleared.
                Assert.IsFalse(leaf.ClassListContains("bg-hot"));
            }

            [Test]
            public void Given_AHoveredLeaf_When_ItsHoverPayloadStringIsSwapped_Then_TheNewClassIsApplied()
            {
                // Arrange — a hovered leaf so its current hover payload (bg-hot) is applied.
                var leaf = MountLeaf("hover:bg-hot");
                using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("bg-cold"), Is.False, "Precondition: new payload not yet present");

                // Act — the className changes so the hover payload becomes bg-cold.
                ManipulatorOf(leaf).UpdatePayloads(new[] { "bg-cold" }, null, null, null, null);

                // Assert — the new payload class is applied under the still-hovered state.
                Assert.IsTrue(leaf.ClassListContains("bg-cold"));
            }

            [Test]
            public void Given_AHoveredLeaf_When_TheManipulatorIsRemovedFromTheElement_Then_TheAppliedClassIsCleared()
            {
                // Arrange — a hovered leaf whose hover payload is applied.
                var leaf = MountLeaf("hover:bg-hot");
                using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
                var manipulator = ManipulatorOf(leaf);
                Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: payload on while hovered");

                // Act — the manipulator is detached from the element (the cleanup path on dispose / variant removal).
                leaf.RemoveManipulator(manipulator);

                // Assert — unregistering clears any payload it had applied.
                Assert.IsFalse(leaf.ClassListContains("bg-hot"));
            }
        }
    }
}
