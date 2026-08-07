using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for the relational (<c>group-*</c> / <c>peer-*</c>) variants. Unlike the
    /// element-local variants, a relational manipulator resolves its SOURCE — the nearest <c>group</c> ancestor or
    /// preceding <c>peer</c> sibling — only once it is attached to a panel (<c>AttachToPanelEvent</c>), then listens
    /// to that source's pointer/focus events. These tests mount inside a real <see cref="UnityEditor.EditorWindow"/> panel so the
    /// source resolves, then fire a real event on the source and assert the payload toggles on the consuming child.
    /// This also covers the harder-to-reach edges: the bubbling <c>PointerOut</c> bounds gate (clearing active
    /// when the pointer leaves the source vs. keeping hover while crossing the source's descendants), the
    /// <c>peer-checked:</c> bubbling-change guard, the UNNAMED peer-checked initial read, the shared
    /// <c>FocusIn</c> signal feeding both focus and focus-within layers, the detach cleanup that tears the
    /// binding down so no ghost class survives, and the relational variants added alongside the element-local
    /// <c>checked:</c> — <c>peer-checked:</c> and <c>group-focus-within:</c> / <c>peer-focus-within:</c>
    /// (the source's bubbling focus, i.e. focus reaching the source or any descendant). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalVariantPanelTests : PanelTestBase
    {
        private static void Fire<TEvent>(VisualElement el) where TEvent : EventBase<TEvent>, new()
        {
            using var evt = EventBase<TEvent>.GetPooled();
            el.SimulateEvent(evt);
        }

        // Hover now uses bubbling PointerOut, cleared only once the pointer leaves the source's bounds. In a real
        // panel the source is laid out at the origin, so a default-position (0,0) Out would read as "still inside".
        // Fire the Out with a position well outside any element so it registers as a genuine leave.
        private static void FirePointerOutOutside(VisualElement el)
        {
            using var evt = PointerOutEvent.GetPooled();
            typeof(PointerEventBase<PointerOutEvent>)
                .GetProperty("position", BindingFlags.Public | BindingFlags.Instance)!
                .GetSetMethod(nonPublic: true)!
                .Invoke(evt, new object[] { new Vector3(100000f, 100000f, 0f) });
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
        public void Given_AGroupHoverChild_When_TheGroupSourceIsHovered_Then_ThePayloadIsAppliedToTheChild()
        {
            // Arrange — a child with group-hover:bg-on under a `group` ancestor, mounted in a panel.
            _mounted = V.Mount(_window.rootVisualElement, V.Div("group", V.Label(name: "child", className: "group-hover:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before group hover");

            // Act — the pointer goes over the GROUP source (not the child).
            Fire<PointerOverEvent>(source);

            // Assert — the group-hover payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_AHoveredGroup_When_ThePointerLeavesTheSource_Then_ThePayloadIsRemovedFromTheChild()
        {
            // Arrange — a group whose source is hovered, so the child's payload is applied.
            _mounted = V.Mount(_window.rootVisualElement, V.Div("group", V.Label(name: "child", className: "group-hover:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Fire<PointerOverEvent>(source);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: payload on while group hovered");

            // Act — the pointer leaves the source.
            FirePointerOutOutside(source);

            // Assert — the payload is removed from the child.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_APeerHoverChild_When_ThePrecedingPeerIsHovered_Then_ThePayloadIsAppliedToTheChild()
        {
            // Arrange — a child with peer-hover:bg-on preceded by a `peer` sibling, mounted in a panel.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Label(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-hover:bg-on")));
            var peer = _window.rootVisualElement.Q<Label>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before peer hover");

            // Act — the pointer goes over the preceding PEER sibling.
            Fire<PointerOverEvent>(peer);

            // Assert — the peer-hover payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_ANamedGroupHoverChild_When_TheNamedGroupSourceIsHovered_Then_ThePayloadIsApplied()
        {
            // Arrange — a child with group-hover/sidebar:bg-on under an ancestor marked `group/sidebar`.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("group/sidebar", V.Label(name: "child", className: "group-hover/sidebar:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group/sidebar");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before named group hover");

            // Act — the pointer goes over the NAMED group source.
            Fire<PointerOverEvent>(source);

            // Assert — the named group-hover payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_TwoNamedGroupsOnOneChild_When_TheInnerNamedSourceIsHovered_Then_ItsOwnPayloadIsApplied()
        {
            // Arrange — a child consuming two distinct named groups (group/outer ⊃ group/inner), so the manipulator
            // holds two bindings resolving to two different ancestors.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group/outer",
                V.Div("group/inner", V.Label(name: "child", className: "group-hover/outer:bg-a group-hover/inner:bg-b"))));
            var inner = _window.rootVisualElement.Q<VisualElement>(className: "group/inner");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-b"), Is.False, "Precondition: inner payload off before hover");

            // Act — the inner named source is hovered.
            Fire<PointerOverEvent>(inner);

            // Assert — the binding for /inner applies ITS payload.
            Assert.IsTrue(child.ClassListContains("bg-b"));
        }

        [Test]
        public void Given_TwoNamedGroupsOnOneChild_When_TheInnerNamedSourceIsHovered_Then_TheOtherNamedPayloadStaysOff()
        {
            // Arrange — same two-named-group child.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group/outer",
                V.Div("group/inner", V.Label(name: "child", className: "group-hover/outer:bg-a group-hover/inner:bg-b"))));
            var inner = _window.rootVisualElement.Q<VisualElement>(className: "group/inner");
            var child = _window.rootVisualElement.Q<Label>("child");

            // Act — only the inner named source is hovered (non-bubbling, so the outer source is untouched).
            Fire<PointerOverEvent>(inner);

            // Assert — the /outer binding (a distinct source) does not fire, so its payload stays off.
            Assert.IsFalse(child.ClassListContains("bg-a"));
        }

        [Test]
        public void Given_ANamedPeerHoverChild_When_TheNamedPeerSourceIsHovered_Then_ThePayloadIsApplied()
        {
            // Arrange — a child with peer-hover/email:bg-on preceded by a sibling marked `peer/email`.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Label(name: "peer", className: "peer/email"),
                V.Label(name: "child", className: "peer-hover/email:bg-on")));
            var peer = _window.rootVisualElement.Q<Label>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before named peer hover");

            // Act — the pointer goes over the named peer sibling.
            Fire<PointerOverEvent>(peer);

            // Assert — the named peer-hover payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_ANamedGroupHoverChild_When_NoMatchingNamedSourceExists_Then_ThePayloadStaysOff()
        {
            // Arrange — a child wants group-hover/sidebar: but the only ancestor is an UNNAMED `group` (no
            // `group/sidebar`), so the named binding must resolve nothing and not fall back to the unnamed source.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("group", V.Label(name: "child", className: "group-hover/sidebar:bg-on")));
            var unnamed = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");

            // Act — a real (panel-routed) pointer-over reaches the unnamed group. SendEvent (not SimulateEvent)
            // because a correct named binding never subscribed to the unnamed source, so it has no callback
            // registry — SendEvent simply fires nothing there, while a mis-binding would light the child.
            using (var evt = PointerOverEvent.GetPooled())
            {
                evt.target = unnamed;
                unnamed.SendEvent(evt);
            }

            // Assert — the named binding does not bind to the unnamed group, so the payload stays off.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_StackedNamedGroupHover_When_DarkAndTheNamedSourceIsHovered_Then_ItResolvesTheNamedSource()
        {
            // dark:group-hover/sidebar:bg-on — the stacked INNER is a NAMED relational, which must resolve the
            // `group/sidebar` source (not the unnamed group). The only ancestor is `group/sidebar`, so without
            // name-threading the inner would resolve the unnamed group, find nothing, and never light.
            var darkBefore = VelvetTheme.IsDark;
            try
            {
                VelvetTheme.IsDark = true;
                _mounted = V.Mount(_window.rootVisualElement,
                    V.Div("group/sidebar", V.Label(name: "child", className: "dark:group-hover/sidebar:bg-on")));
                var source = _window.rootVisualElement.Q<VisualElement>(className: "group/sidebar");
                var child = _window.rootVisualElement.Q<Label>("child");
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: dark alone does not apply (group not hovered)");

                // Act — the named group source is hovered (inner gate opens; dark outer gate already open).
                Fire<PointerOverEvent>(source);

                // Assert — the stacked inner resolved the NAMED source, so both gates hold and the payload applies.
                Assert.IsTrue(child.ClassListContains("bg-on"));
            }
            finally
            {
                VelvetTheme.IsDark = darkBefore;
            }
        }

        [Test]
        public void Given_TwoNamedGroupsStackingTheSameInnerLeaf_When_OneSourceLeavesThenTheInnerRefires_Then_TheOtherBindingStillApplies()
        {
            // Two named groups stacking the SAME inner leaf (group-hover/a:hover:bg-red group-hover/b:hover:bg-red).
            // Each binding must own an INDEPENDENT nested stacked manipulator; otherwise source A leaving tears
            // down the shared manipulator that source B still needs, so B's subscription dies and a later inner
            // re-hover can no longer re-apply the leaf. Structure: group/a ⊃ group/b ⊃ child.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "group/a",
                V.Div("group/b", V.Label(name: "child", className: "group-hover/a:hover:bg-red group-hover/b:hover:bg-red"))));
            var a = _window.rootVisualElement.Q<VisualElement>(className: "group/a");
            var b = _window.rootVisualElement.Q<VisualElement>(className: "group/b");
            var child = _window.rootVisualElement.Q<Label>("child");

            // Open both outer gates (hover A and B) and the inner gate (hover the child), so bg-red applies.
            Fire<PointerOverEvent>(a);
            Fire<PointerOverEvent>(b);
            Fire<PointerOverEvent>(child);
            Assume.That(child.ClassListContains("bg-red"), Is.True, "Precondition: both gates + inner open applies bg-red");

            // Act — source A leaves (tearing down A's nested manipulator), then the inner re-fires (re-hover child).
            FirePointerOutOutside(a);
            FirePointerOutOutside(child);
            Fire<PointerOverEvent>(child);

            // Assert — B's binding kept its own live manipulator, so the inner re-hover re-applies the leaf.
            Assert.IsTrue(child.ClassListContains("bg-red"));
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
        public void Given_APeerCheckedChild_When_ThePrecedingPeerIsARadioButtonMountedAlreadyChecked_Then_ThePayloadIsSeededAtMount()
        {
            // Arrange/Act — the same mount-time read with a RadioButton as the peer, which reports a bool
            // without being a Toggle. Its change path already drives peer-checked:, so only the read was
            // narrower than the registration beside it.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Custom<RadioButton>(name: "peer", className: "peer",
                    props: new FiberElementProps { FieldValue = true }),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var peer = _window.rootVisualElement.Q<RadioButton>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");

            // Assert — folded rather than assumed: a controlled value that never reached the peer would make
            // the absent payload correct, so the seed is read beside the value it seeds from.
            Assert.That((peer.value, child.ClassListContains("bg-on")), Is.EqualTo((true, true)));
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

        [Test]
        public void Given_PeerCheckedChild_When_ThePrecedingPeerIsChecked_Then_PayloadApplied()
        {
            // Arrange — a child with peer-checked:bg-on preceded by a `peer` Toggle.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var peer = _window.rootVisualElement.Q<Toggle>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before peer checked");

            // Act — the preceding peer toggle is checked.
            peer.SimulateChange(true);

            // Assert — the peer-checked payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_PeerCheckedApplied_When_ThePeerIsUnchecked_Then_PayloadRemoved()
        {
            // Arrange — a child whose peer-checked payload is applied (peer checked).
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-checked:bg-on")));
            var peer = _window.rootVisualElement.Q<Toggle>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            peer.SimulateChange(true);
            Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: payload on while peer checked");

            // Act — the peer is unchecked.
            peer.SimulateChange(false);

            // Assert — the payload clears.
            Assert.IsFalse(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_GroupFocusWithinChild_When_TheGroupSourceGainsFocus_Then_PayloadApplied()
        {
            // Arrange — a child with group-focus-within:bg-on under a `group` ancestor.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("group", V.Label(name: "child", className: "group-focus-within:bg-on")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before focus");

            // Act — focus reaches the group source (FocusIn = focus-within).
            Fire<FocusInEvent>(source);

            // Assert — the group-focus-within payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_PeerFocusWithinChild_When_ThePrecedingPeerGainsFocus_Then_PayloadApplied()
        {
            // Arrange — a child with peer-focus-within:bg-on preceded by a `peer` sibling.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Label(name: "peer", className: "peer"),
                V.Label(name: "child", className: "peer-focus-within:bg-on")));
            var peer = _window.rootVisualElement.Q<Label>("peer");
            var child = _window.rootVisualElement.Q<Label>("child");
            Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before focus");

            // Act — focus reaches the preceding peer (FocusIn = focus-within).
            Fire<FocusInEvent>(peer);

            // Assert — the peer-focus-within payload is applied to the consuming child.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }

        [Test]
        public void Given_NamedPeerCheckedChild_When_TheNamedPeerIsMountedAlreadyChecked_Then_PayloadSeededAtMount()
        {
            // Arrange/Act — a child with peer-checked/opt:bg-on preceded by a `peer/opt` Toggle mounted ALREADY
            // checked. peer-checked is the one relational state seeded by Resolve (not an event), so the named
            // binding must read the initial Toggle value at mount and apply the payload without any ChangeEvent.
            _mounted = V.Mount(_window.rootVisualElement, V.Div(
                "container",
                V.Toggle(name: "peer", className: "peer/opt", value: true),
                V.Label(name: "child", className: "peer-checked/opt:bg-on")));
            var child = _window.rootVisualElement.Q<Label>("child");

            // Assert — the named peer-checked payload is seeded from the already-checked source at mount.
            Assert.IsTrue(child.ClassListContains("bg-on"));
        }
    }
}
