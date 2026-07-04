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
    /// GWT, one assert per case.
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
    }
}
