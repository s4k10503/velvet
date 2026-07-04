using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Characterization coverage for the high-risk edges of the stacked-variant manipulator (outer gate AND
    /// inner signal, e.g. <c>dark:active:</c> / <c>dark:hover:</c> / <c>dark:focus-visible:</c>) that the broader
    /// <see cref="StackedVariantBehaviorTests"/> does not exercise: the element-local press/release/cancel and the
    /// worldBound-gated pointer-out for an <c>active</c> inner, the bounds-kept hover for a <c>hover</c> inner, the
    /// pointer-vs-keyboard split for a <c>focus-visible</c> inner, the inner kinds that are NOT supported as a
    /// stack (<c>checked:</c> / <c>focus-within:</c> / <c>peer-checked:</c> stay inert rather than crashing), and
    /// the detach teardown that clears the leaf and releases the inner subscription. These pin the current
    /// behavior so a refactor of the manipulator preserves it. Element-local / dark-only cases run off panel
    /// (the manipulator registers on the element itself); worldBound, responsive and relational cases mount in a
    /// real <see cref="UnityEditor.EditorWindow"/> panel so bounds resolve and the relational/responsive source
    /// binds. The outer (<c>dark</c>) gate is driven through <see cref="VelvetTheme.IsDark"/>. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantEdgeTests
    {
        // --- off-panel: element-local inner (active / focus-visible) AND the dark outer gate ---

        [TestFixture]
        internal sealed class ElementLocalInner
        {
            private VisualElement _root;
            private bool _darkBefore;
            private MountedTree _mounted;

            [SetUp]
            public void SetUp()
            {
                _root = new VisualElement();
                _darkBefore = VelvetTheme.IsDark;
                VelvetTheme.IsDark = false;
            }

            [TearDown]
            public void TearDown()
            {
                _mounted?.Dispose();
                _mounted = null;
                VelvetTheme.IsDark = _darkBefore;
            }

            private Label MountLeaf(string className)
            {
                _mounted = V.Mount(_root, V.Label(name: "leaf", className: className, text: "x"));
                return _root.Q<Label>("leaf");
            }

            [Test]
            public void Given_DarkActiveLeafWithDarkOn_When_ThePointerGoesDown_Then_TheLeafIsApplied()
            {
                // Arrange — dark:active:bg-hot with dark on (outer gate open), not yet pressed (inner off).
                var leaf = MountLeaf("dark:active:bg-hot");
                VelvetTheme.IsDark = true;
                Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: dark alone does not apply (active off)");

                // Act — the pointer goes down (the active inner gate opens).
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — both dark AND active hold, so the leaf applies.
                Assert.IsTrue(leaf.ClassListContains("bg-hot"));
            }

            [Test]
            public void Given_DarkActiveLeafPressedWithDarkOn_When_ThePointerGoesUp_Then_TheLeafIsRemoved()
            {
                // Arrange — dark:active:bg-hot applied while dark AND pressed.
                var leaf = MountLeaf("dark:active:bg-hot");
                VelvetTheme.IsDark = true;
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND pressed");

                // Act — the pointer is released (the active inner gate closes).
                using (var evt = PointerUpEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — the leaf clears (the AND no longer holds).
                Assert.IsFalse(leaf.ClassListContains("bg-hot"));
            }

            [Test]
            public void Given_DarkActiveLeafPressedWithDarkOn_When_ThePointerInteractionIsCancelled_Then_TheLeafIsRemoved()
            {
                // Arrange — dark:active:bg-hot applied while dark AND pressed.
                var leaf = MountLeaf("dark:active:bg-hot");
                VelvetTheme.IsDark = true;
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND pressed");

                // Act — the pointer interaction is cancelled (no pointer-up arrives).
                using (var evt = PointerCancelEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — a cancel drops the active inner just like a release, so the leaf clears.
                Assert.IsFalse(leaf.ClassListContains("bg-hot"));
            }

            [Test]
            public void Given_DarkFocusVisibleLeafWithDarkOn_When_FocusFollowsAPointerDown_Then_TheLeafIsNotApplied()
            {
                // Arrange — dark:focus-visible:ring-kbd with dark on (outer gate open), unfocused.
                var leaf = MountLeaf("dark:focus-visible:ring-kbd");
                VelvetTheme.IsDark = true;
                Assume.That(leaf.ClassListContains("ring-kbd"), Is.False, "Precondition: payload off before interaction");

                // Act — a pointer-down causes the focus (the click-to-focus path).
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — focus-visible stays off for pointer-driven focus even with the dark gate open.
                Assert.IsFalse(leaf.ClassListContains("ring-kbd"));
            }

            [Test]
            public void Given_DarkFocusVisibleLeafWithDarkOn_When_ItGainsFocusFromTheKeyboard_Then_TheLeafIsApplied()
            {
                // Arrange — dark:focus-visible:ring-kbd with dark on (outer gate open), unfocused.
                var leaf = MountLeaf("dark:focus-visible:ring-kbd");
                VelvetTheme.IsDark = true;
                Assume.That(leaf.ClassListContains("ring-kbd"), Is.False, "Precondition: payload off before focus");

                // Act — it gains focus with no preceding pointer-down (Tab navigation / Focus()).
                using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — both dark AND keyboard focus-visible hold, so the leaf applies.
                Assert.IsTrue(leaf.ClassListContains("ring-kbd"));
            }
        }

        // --- panel: worldBound-gated pointer-out, unsupported inners, and detach teardown ---

        [TestFixture]
        internal sealed class Panel : PanelTestBase
        {
            private bool _darkBefore;

            public override void SetUp()
            {
                base.SetUp();
                _darkBefore = VelvetTheme.IsDark;
                VelvetTheme.IsDark = false;
            }

            public override void TearDown()
            {
                VelvetTheme.IsDark = _darkBefore;
                base.TearDown();
            }

            private static void Over(VisualElement on)
            {
                using var evt = PointerOverEvent.GetPooled();
                evt.target = on;
                on.SendEvent(evt);
            }

            // The bubbling PointerOut bounds gate reads evt.position against the target's worldBound, so the Out
            // must carry an explicit position. The position setter is non-public on the pooled event.
            private static void OutAt(VisualElement on, Vector2 position)
            {
                using var evt = PointerOutEvent.GetPooled();
                typeof(PointerEventBase<PointerOutEvent>)
                    .GetProperty("position", BindingFlags.Public | BindingFlags.Instance)!
                    .GetSetMethod(nonPublic: true)!
                    .Invoke(evt, new object[] { (Vector3)position });
                evt.target = on;
                on.SendEvent(evt);
            }

            private static void Fire<TEvent>(VisualElement el) where TEvent : EventBase<TEvent>, new()
            {
                using var evt = EventBase<TEvent>.GetPooled();
                el.SimulateEvent(evt);
            }

            [Test]
            public void Given_DarkActivePressedWithDarkOn_When_ThePointerLeavesTheBoundsWithoutAPointerUp_Then_TheLeafIsCleared()
            {
                // Arrange — a sized dark:active:bg-on element pressed while dark on (both gates hold).
                _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: "dark:active:bg-on w-[400px] h-[200px]"));
                ForcePanelUpdate(_window.rootVisualElement.panel);
                var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
                VelvetTheme.IsDark = true;
                using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
                Assume.That(leaf.ClassListContains("bg-on"), Is.True, "Precondition: applied while dark AND pressed");

                // Act — the pointer leaves the element's bounds with no preceding pointer-up.
                OutAt(leaf, leaf.worldBound.center + new Vector2(100000f, 100000f));

                // Assert — leaving the bounds clears the active inner, so the leaf clears.
                Assert.IsFalse(leaf.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkHoverHoveredWithDarkOn_When_ABubblingPointerOutStaysWithinTheBounds_Then_TheLeafIsKept()
            {
                // Arrange — a sized dark:hover:bg-on parent whose child fills it, hovered via the child while dark on.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "dark:hover:bg-on w-[400px] h-[200px]",
                    V.Label(name: "child", className: "w-[400px] h-[200px]", text: "x")));
                ForcePanelUpdate(_window.rootVisualElement.panel);
                var child = _window.rootVisualElement.Q<Label>("child");
                var parent = child.parent;
                VelvetTheme.IsDark = true;
                Over(child);
                Assume.That(parent.ClassListContains("bg-on"), Is.True, "Precondition: applied while dark AND hovered");

                // Act — a bubbling pointer-out fires but the pointer is still inside the parent's bounds (crossing a descendant).
                OutAt(child, parent.worldBound.center);

                // Assert — hover persists while the pointer remains within the parent's bounds.
                Assert.IsTrue(parent.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkCheckedLeafWithDarkOn_When_NoPeerSourceExists_Then_TheLeafIsNotApplied()
            {
                // Arrange — dark:checked:bg-on. `checked` is not supported as a stacked inner: it falls through to
                // the relational branch, which seeks a preceding `peer` source and finds none, so it stays inert.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: "dark:checked:bg-on"));
                var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");

                // Act — the outer (dark) gate opens, which is the only signal a stacked dark:checked: can ever receive.
                VelvetTheme.IsDark = true;

                // Assert — the unsupported checked inner never lights the leaf (and does not crash).
                Assert.IsFalse(leaf.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkPeerCheckedChildWithDarkOn_When_ThePeerSourceIsChecked_Then_TheLeafIsNotApplied()
            {
                // Arrange — dark:peer-checked:bg-on preceded by a `peer` Toggle. peer-checked is not supported as a
                // stacked inner: the relational branch resolves the source but never subscribes to its ChangeEvent
                // nor seeds the initial checked state, so the inner gate can never open.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "container",
                    V.Toggle(name: "peer", className: "peer"),
                    V.Label(name: "child", className: "dark:peer-checked:bg-on")));
                var peer = _window.rootVisualElement.Q<Toggle>("peer");
                var child = _window.rootVisualElement.Q<Label>("child");
                VelvetTheme.IsDark = true;
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before any change");

                // Act — the preceding peer toggle is checked.
                peer.SimulateChange(true);

                // Assert — the unsupported peer-checked inner ignores the source's checked state, so the leaf stays off.
                Assert.IsFalse(child.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkGroupFocusWithinChildUnderAGroup_When_TheOuterDarkGateOpens_Then_TheLeafIsNotApplied()
            {
                // Arrange — dark:group-focus-within:bg-on under a `group` source. focus-within is not supported as a
                // stacked inner: the relational branch does not treat group-focus-within as a group kind, so it
                // seeks a `peer` sibling, finds none, and stays inert — no source signal can ever open the inner.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "group",
                    V.Label(name: "child", className: "dark:group-focus-within:bg-on")));
                var child = _window.rootVisualElement.Q<Label>("child");

                // Act — the outer (dark) gate opens, which is the only signal a fully-inert stacked inner can receive.
                VelvetTheme.IsDark = true;

                // Assert — the unsupported focus-within inner never lights the leaf (and does not crash).
                Assert.IsFalse(child.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkMdLeafAppliedWide_When_TheLeafIsDetached_Then_TheLeafIsCleared()
            {
                // Arrange — a dark:md:bg-on leaf applied while dark AND the panel is at least the md breakpoint wide.
                _window.position = new Rect(0, 0, 1000, 600);
                _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: "dark:md:bg-on", text: "x"));
                var leaf = _window.rootVisualElement.Q<Label>("leaf");
                ForcePanelUpdate(leaf.panel);
                VelvetTheme.IsDark = true;
                Assume.That(leaf.ClassListContains("bg-on"), Is.True, "Precondition: applied while dark AND wide");

                // Act — the consuming leaf is detached (DetachFromPanelEvent tears the responsive binding down).
                leaf.RemoveFromHierarchy();

                // Assert — detach clears the applied leaf.
                Assert.IsFalse(leaf.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkGroupHoverChildAppliedThenDetached_When_TheSourceIsHoveredAgain_Then_NoGhostClassRemains()
            {
                // Arrange — a dark:group-hover:bg-on child applied while dark AND the group source is hovered.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "group",
                    V.Label(name: "child", className: "dark:group-hover:bg-on")));
                var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
                var child = _window.rootVisualElement.Q<Label>("child");
                VelvetTheme.IsDark = true;
                Fire<PointerOverEvent>(source);
                Assume.That(child.ClassListContains("bg-on"), Is.True, "Precondition: applied while dark AND group hovered");

                // Act — the child is detached (tearing the relational binding down), then the source is hovered again.
                child.RemoveFromHierarchy();
                Fire<PointerOverEvent>(source);

                // Assert — detach released the source subscription, so the second hover leaves no ghost class.
                Assert.IsFalse(child.ClassListContains("bg-on"));
            }
        }
    }
}
