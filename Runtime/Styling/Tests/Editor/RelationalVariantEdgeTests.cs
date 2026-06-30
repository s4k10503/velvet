using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Characterization coverage for the high-risk edges of the relational (<c>group-*</c> / <c>peer-*</c>)
    /// variant binding that the broader relational fixtures do not exercise: the bubbling <c>PointerOut</c>
    /// bounds gate (clearing active when the pointer leaves the source vs. keeping hover while crossing the
    /// source's descendants), the <c>peer-checked:</c> bubbling-change guard, the UNNAMED peer-checked initial
    /// read, the shared <c>FocusIn</c> signal feeding both focus and focus-within layers, and the detach
    /// cleanup that tears the binding down so no ghost class survives. These pin the current behavior so a
    /// refactor of the binding preserves it. Mounted in a real <see cref="UnityEditor.EditorWindow"/> panel so
    /// the source resolves and bounds are real; events go through the source's callback registry. GWT, one
    /// assert each.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalVariantEdgeTests : PanelTestBase
    {
        private static void Fire<TEvent>(VisualElement el) where TEvent : EventBase<TEvent>, new()
        {
            using var evt = EventBase<TEvent>.GetPooled();
            el.SimulateEvent(evt);
        }

        // The bubbling PointerOut bounds gate reads evt.position against the source's worldBound, so the Out
        // must carry an explicit position. The position setter is non-public on the pooled event.
        private static void FirePointerOutAt(VisualElement el, Vector2 position)
        {
            using var evt = PointerOutEvent.GetPooled();
            typeof(PointerEventBase<PointerOutEvent>)
                .GetProperty("position", BindingFlags.Public | BindingFlags.Instance)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(evt, new object[] { (Vector3)position });
            el.SimulateEvent(evt);
        }

        [Test]
        public void Given_AnActiveGroup_When_ThePointerLeavesTheSourceWithoutAPointerUp_Then_TheActivePayloadIsCleared()
        {
            // Arrange — a group-active child under a sized `group` source pressed (pointer-down) but not released.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group w-[400px] h-[200px]",
                V.Label(name: "child", className: "group-active:bg-on")));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Fire<PointerDownEvent>(source);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: active payload on while pressed");

            // Act — the pointer leaves the source's bounds with no preceding pointer-up.
            FirePointerOutAt(source, source.worldBound.center + new Vector2(100000f, 100000f));

            // Assert — leaving the source's bounds clears the active payload even without a pointer-up.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AHoveredGroup_When_ABubblingPointerOutStaysWithinTheSourceBounds_Then_TheHoverPayloadIsKept()
        {
            // Arrange — a group-hover child under a sized `group` source that is hovered.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group w-[400px] h-[200px]",
                V.Label(name: "child", className: "group-hover:bg-on")));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Fire<PointerOverEvent>(source);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: hover payload on while hovered");

            // Act — a bubbling pointer-out fires but the pointer is still inside the source's bounds (crossing a descendant).
            FirePointerOutAt(source, source.worldBound.center);

            // Assert — hover persists while the pointer remains within the source's bounds.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_APeerCheckedChild_When_ADescendantOfThePeerBubblesAChange_Then_ThePeerCheckedPayloadStaysOff()
        {
            // Arrange — a peer-checked child preceded by a `peer` container that holds an INNER toggle. The peer
            // source itself never changes; only its descendant toggle does.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Div("peer", V.Toggle(name: "inner")),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "peer");
            var inner = _window.rootVisualElement.Q<Toggle>("inner");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before any change");

            // Act — a ChangeEvent<bool> bubbles up to the peer source from its descendant toggle (target = the descendant).
            using (var evt = ChangeEvent<bool>.GetPooled(false, true))
            {
                source.SimulateBubbledEvent(evt, inner);
            }

            // Assert — peer-checked reflects only the source's OWN checked state, so a descendant's change is ignored.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AnUnnamedPeerCheckedChild_When_ThePrecedingPeerToggleIsMountedAlreadyChecked_Then_ThePayloadIsSeededAtMount()
        {
            // Arrange/Act — an unnamed peer-checked child preceded by a `peer` Toggle mounted ALREADY checked.
            // peer-checked is the one relational state seeded by Resolve (not an event), so the unnamed binding
            // must read the initial Toggle value at mount and apply the payload without any ChangeEvent.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer", value: true),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var child = _window.rootVisualElement.Q<Label>("child");

            // Assert — the unnamed peer-checked payload is seeded from the already-checked source at mount.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AChildConsumingBothGroupFocusAndFocusWithin_When_TheSourceGainsFocus_Then_TheFocusPayloadIsApplied()
        {
            // Arrange — a child consuming BOTH group-focus and group-focus-within on the same `group` source, so a
            // single FocusIn must feed both layers; this case asserts the focus layer.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group",
                V.Label(name: "child", className: "group-focus:bg-a group-focus-within:bg-b")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-a"), Is.False, "Precondition: focus payload off before focus");

            // Act — focus reaches the group source.
            Fire<FocusInEvent>(source);

            // Assert — the shared FocusIn applies the group-focus payload.
            Assert.IsTrue(child.ClassListContains("bg-a"));
        }

        [Test]
        public void Given_AChildConsumingBothGroupFocusAndFocusWithin_When_TheSourceGainsFocus_Then_TheFocusWithinPayloadIsApplied()
        {
            // Arrange — same child consuming BOTH group-focus and group-focus-within; this case asserts the
            // focus-within layer is driven by the SAME FocusIn signal.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group",
                V.Label(name: "child", className: "group-focus:bg-a group-focus-within:bg-b")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-b"), Is.False, "Precondition: focus-within payload off before focus");

            // Act — focus reaches the group source.
            Fire<FocusInEvent>(source);

            // Assert — the same FocusIn also applies the group-focus-within payload.
            Assert.IsTrue(child.ClassListContains("bg-b"));
        }

        [Test]
        public void Given_AGroupHoverChildWithThePayloadApplied_When_TheChildIsDetachedThenTheSourceIsHoveredAgain_Then_NoGhostClassRemains()
        {
            // Arrange — a group-hover child whose payload is applied (source hovered).
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group",
                V.Label(name: "child", className: "group-hover:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Fire<PointerOverEvent>(source);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: payload on while hovered");

            // Act — the consuming child is detached (DetachFromPanelEvent tears the binding down), then the source
            // is hovered again. If detach had not unhooked the source, this second hover would re-light the child.
            child.RemoveFromHierarchy();
            Fire<PointerOverEvent>(source);

            // Assert — detach cleared the payload and unsubscribed the source, so no ghost class survives.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }
    }
}
