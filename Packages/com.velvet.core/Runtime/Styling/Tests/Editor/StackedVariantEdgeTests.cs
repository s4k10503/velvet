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
    /// pointer-vs-keyboard split for a <c>focus-visible</c> inner, the four inner kinds driven by something
    /// other than a pointer edge (<c>checked:</c> and <c>peer-checked:</c> by a change of checked state and
    /// by the hook-time read of a control mounted already checked, <c>checked:</c> also by a value written
    /// through a controlled prop — including the three-deep spelling whose leaf peels to a further variant,
    /// where opening the checked gate registers another manipulator while the settle is still walking the
    /// registry — <c>group-focus-within:</c> and
    /// <c>peer-focus-within:</c> by the source's bubbling focus), and
    /// the detach teardown that clears the leaf and releases the inner subscription. These pin the current
    /// behavior so a refactor of the manipulator preserves it. Element-local / dark-only cases run off panel
    /// (the manipulator registers on the element itself); worldBound, responsive and relational cases mount in a
    /// real <see cref="UnityEditor.EditorWindow"/> panel so bounds resolve and the relational/responsive source
    /// binds. The outer (<c>dark</c>) gate is driven through <see cref="VelvetTheme.IsDark"/>. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantEdgeTests
    {
        // --- off-panel: element-local inner (active / focus-visible / checked) AND the dark outer gate ---

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

            [Test]
            public void Given_DarkCheckedToggleWithDarkOn_When_TheToggleIsChecked_Then_TheLeafIsApplied()
            {
                // Arrange — dark:checked:bg-on on an unchecked Toggle with dark on (outer gate open).
                _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "dark:checked:bg-on"));
                var leaf = _root.Q<Toggle>("leaf");
                VelvetTheme.IsDark = true;
                Assume.That(leaf.ClassListContains("bg-on"), Is.False, "Precondition: dark alone does not apply (unchecked)");

                // Act — the toggle is checked (the checked inner gate opens).
                leaf.SimulateChange(true);

                // Assert — both dark AND checked hold, so the leaf applies.
                Assert.IsTrue(leaf.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkCheckedToggleWithDarkOn_When_ItsValueArrivesThroughAControlledProp_Then_TheLeafIsApplied()
            {
                // Arrange — dark:checked:bg-on on a controlled Toggle rendered unchecked, dark on so the
                // inner is built and hooked.
                using var scope = new ReconcilerScope();
                var renderedUnchecked = new VNode[]
                {
                    V.Toggle(name: "leaf", className: "dark:checked:bg-on", value: false),
                };
                scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), renderedUnchecked);
                var leaf = scope.Root.Q<Toggle>("leaf");
                VelvetTheme.IsDark = true;
                var beforeTheControlledWrite = leaf.ClassListContains("bg-on");

                // Act — the owner re-renders with the value flipped; the control is written without
                // notification, so no ChangeEvent reaches the inner gate.
                scope.Reconciler.Reconcile(scope.Root, renderedUnchecked, new VNode[]
                {
                    V.Toggle(name: "leaf", className: "dark:checked:bg-on", value: true),
                });

                // Assert — folded rather than assumed: dark alone applying the leaf would make the second
                // term true without the write under test having done anything.
                Assert.That(
                    (beforeTheControlledWrite, leaf.ClassListContains("bg-on")),
                    Is.EqualTo((false, true)));
            }

            [Test]
            public void Given_DarkCheckedToggleWhoseLeafIsItselfAVariant_When_ItsValueArrivesThroughAControlledProp_Then_TheInnermostLeafStillApplies()
            {
                // Arrange — dark:checked:hover:bg-on, whose leaf peels to a further variant, on a controlled
                // Toggle rendered unchecked with dark on.
                using var scope = new ReconcilerScope();
                var renderedUnchecked = new VNode[]
                {
                    V.Toggle(name: "leaf", className: "dark:checked:hover:bg-on", value: false),
                };
                scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), renderedUnchecked);
                var leaf = scope.Root.Q<Toggle>("leaf");
                VelvetTheme.IsDark = true;
                var beforeTheControlledWrite = leaf.ClassListContains("bg-on");

                // Act — the controlled write settles the checked gate, which registers the hover manipulator
                // its leaf needs while the settle is still walking the registry; then hover opens that one.
                scope.Reconciler.Reconcile(scope.Root, renderedUnchecked, new VNode[]
                {
                    V.Toggle(name: "leaf", className: "dark:checked:hover:bg-on", value: true),
                });
                using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

                // Assert — folded rather than assumed: a payload already applied before the write would make
                // the second term true with neither gate having moved.
                Assert.That(
                    (beforeTheControlledWrite, leaf.ClassListContains("bg-on")),
                    Is.EqualTo((false, true)));
            }

            [Test]
            public void Given_DarkCheckedToggleMountedAlreadyChecked_When_TheDarkGateOpens_Then_TheLeafIsApplied()
            {
                // Arrange — dark:checked:bg-on on a Toggle mounted ALREADY checked, in the light theme. No
                // ChangeEvent will ever fire, so the inner gate can only come from the hook-time read.
                _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "dark:checked:bg-on", value: true));
                var leaf = _root.Q<Toggle>("leaf");
                Assume.That(leaf.ClassListContains("bg-on"), Is.False, "Precondition: checked alone does not apply (light)");

                // Act — the outer (dark) gate opens, which is what builds and hooks the inner.
                VelvetTheme.IsDark = true;

                // Assert — the inner is seeded from the control's current value, so the leaf applies.
                Assert.IsTrue(leaf.ClassListContains("bg-on"));
            }
        }

        // --- panel: worldBound-gated pointer-out, relational inners, and detach teardown ---

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
            public void Given_DarkPeerCheckedChildWithDarkOn_When_ThePeerSourceIsChecked_Then_TheLeafIsApplied()
            {
                // Arrange — dark:peer-checked:bg-on preceded by an unchecked `peer` Toggle, dark on.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "container",
                    V.Toggle(name: "peer", className: "peer"),
                    V.Label(name: "child", className: "dark:peer-checked:bg-on")));
                var peer = _window.rootVisualElement.Q<Toggle>("peer");
                var child = _window.rootVisualElement.Q<Label>("child");
                VelvetTheme.IsDark = true;
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: payload off before any change");

                // Act — the preceding peer toggle is checked (the peer-checked inner gate opens).
                peer.SimulateChange(true);

                // Assert — both dark AND the peer's checked state hold, so the leaf applies.
                Assert.IsTrue(child.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkPeerCheckedChildWhosePeerIsAlreadyChecked_When_TheDarkGateOpens_Then_TheLeafIsApplied()
            {
                // Arrange — dark:peer-checked:bg-on preceded by a `peer` Toggle mounted ALREADY checked, light
                // theme. No ChangeEvent will fire, so the inner gate can only come from the hook-time read.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "container",
                    V.Toggle(name: "peer", className: "peer", value: true),
                    V.Label(name: "child", className: "dark:peer-checked:bg-on")));
                var child = _window.rootVisualElement.Q<Label>("child");
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: the peer's state alone does not apply (light)");

                // Act — the outer (dark) gate opens, which is what resolves and hooks the peer source.
                VelvetTheme.IsDark = true;

                // Assert — the inner is seeded from the resolved source's current value, so the leaf applies.
                Assert.IsTrue(child.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkPeerCheckedChildWhosePeerIsARadioButtonAlreadyChecked_When_TheDarkGateOpens_Then_TheLeafIsApplied()
            {
                // Arrange — the same hook-time read with a RadioButton as the peer, which reports a bool
                // without being a Toggle. The stacked inner reaches the same read as the plain binding, so it
                // was narrower than its own change registration in the same way.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "container",
                    V.Custom<RadioButton>(name: "peer", className: "peer",
                        props: new FiberElementProps { FieldValue = true }),
                    V.Label(name: "child", className: "dark:peer-checked:bg-on")));
                var peer = _window.rootVisualElement.Q<RadioButton>("peer");
                var child = _window.rootVisualElement.Q<Label>("child");
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: the peer's state alone does not apply (light)");

                // Act — the outer (dark) gate opens, which is what resolves and hooks the peer source.
                VelvetTheme.IsDark = true;

                // Assert — folded rather than assumed: a controlled value that never reached the peer would
                // make the absent leaf correct, so the seed is read beside the value it seeds from.
                Assert.That((peer.value, child.ClassListContains("bg-on")), Is.EqualTo((true, true)));
            }

            [Test]
            public void Given_DarkGroupFocusWithinChildWithDarkOn_When_TheGroupSourceGainsFocus_Then_TheLeafIsApplied()
            {
                // Arrange — dark:group-focus-within:bg-on under a `group` ancestor, dark on.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "group",
                    V.Label(name: "child", className: "dark:group-focus-within:bg-on")));
                var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
                var child = _window.rootVisualElement.Q<Label>("child");
                VelvetTheme.IsDark = true;
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: dark alone does not apply (unfocused)");

                // Act — focus reaches the group source (its bubbling FocusIn is the focus-within signal).
                Fire<FocusInEvent>(source);

                // Assert — both dark AND focus-within hold, so the leaf applies.
                Assert.IsTrue(child.ClassListContains("bg-on"));
            }

            [Test]
            public void Given_DarkPeerFocusWithinChildWithDarkOn_When_ThePrecedingPeerGainsFocus_Then_TheLeafIsApplied()
            {
                // Arrange — dark:peer-focus-within:bg-on preceded by a `peer` sibling, dark on.
                _mounted = V.Mount(_window.rootVisualElement, V.Div(
                    "container",
                    V.Label(name: "peer", className: "peer"),
                    V.Label(name: "child", className: "dark:peer-focus-within:bg-on")));
                var peer = _window.rootVisualElement.Q<Label>("peer");
                var child = _window.rootVisualElement.Q<Label>("child");
                VelvetTheme.IsDark = true;
                Assume.That(child.ClassListContains("bg-on"), Is.False, "Precondition: dark alone does not apply (unfocused)");

                // Act — focus reaches the preceding peer (its bubbling FocusIn is the focus-within signal).
                Fire<FocusInEvent>(peer);

                // Assert — both dark AND focus-within hold, so the leaf applies.
                Assert.IsTrue(child.ClassListContains("bg-on"));
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

    /// <summary>
    /// Behavioural coverage for stacked variants (<c>dark:hover:</c>, <c>hover:dark:</c>), driven by REAL UI
    /// events and the <see cref="VelvetTheme"/> toggle. A stacked leaf applies iff ALL of its conditions hold
    /// simultaneously and clears when any turns off, order-independently. Two-deep is the certified path
    /// (deeper nesting falls out of the same recursion but is documented best-effort). This also pins that an
    /// edge-based inner state (hover/focus/active) survives the outer condition closing and reopening: because
    /// pointer/focus signals fire only on state EDGES, a manipulator that detached and dropped its inner state
    /// on outer-close would need a fresh physical edge (e.g. the pointer leaving and re-entering) to reapply
    /// <c>dark:hover:*</c> after reopen — a pointer that never left the element would have no edge to fire. So
    /// the manipulator instance and its edge-tracked inner state persist across the outer gate closing and
    /// reopening; only the outer condition re-evaluates. In real CSS the oracle is a continuously-tracked
    /// :hover pseudo-class, unaffected by an ancestor class toggling. Level-based inners (dark, responsive)
    /// still detach on close: they re-derive their truth on re-attach, and dark's process-wide theme
    /// subscription must release immediately. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantBehaviorTests
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
        public void Given_DarkHoverLeaf_When_DarkThenHover_Then_PayloadApplied()
        {
            // Arrange — dark:hover:bg-hot; flip dark on so the outer gate opens but hover (inner) is still off.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: dark alone does not apply (hover off)");

            // Act — the pointer goes over (inner gate opens too).
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the leaf applies because BOTH dark and hover hold.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeafApplied_When_ThemeFlipsToLight_Then_PayloadRemoved()
        {
            // Arrange — applied while dark AND hovered.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND hovered");

            // Act — the outer (dark) gate closes.
            VelvetTheme.IsDark = false;

            // Assert — the leaf clears (the AND no longer holds).
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeafApplied_When_PointerLeaves_Then_PayloadRemoved()
        {
            // Arrange — applied while dark AND hovered.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND hovered");

            // Act — the inner (hover) gate closes.
            using (var evt = PointerOutEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the leaf clears.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeaf_When_HoveredButNotDark_Then_PayloadNotApplied()
        {
            // Arrange — dark:hover:bg-hot in the light theme (outer gate closed).
            var leaf = MountLeaf("dark:hover:bg-hot");

            // Act — only the inner (hover) signal fires.
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — hover alone never lights a dark:hover: leaf (the AND withholds it).
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_HoverDarkLeaf_When_HoverThenDark_Then_PayloadApplied()
        {
            // Arrange — hover:dark:bg-hot (the reverse order); hover first opens the outer gate, dark still off.
            var leaf = MountLeaf("hover:dark:bg-hot");
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: hover alone does not apply (dark off)");

            // Act — the theme flips to dark (inner gate opens).
            VelvetTheme.IsDark = true;

            // Assert — applies, proving stacking is order-independent (hover:dark: == dark:hover:).
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_HoverWithStaticScalePayload_When_Hovered_Then_InlineNegativeMarginApplied()
        {
            // Arrange — hover:-mt-2; the payload -mt-2 has no '[' and is a static-scale name, so the variant
            // apply gate must route it to the inline resolver rather than add it as a (never-matching) class.
            var leaf = MountLeaf("hover:-mt-2");

            // Act
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the inline margin-top is the negated --space-2 (8px).
            Assert.That(leaf.style.marginTop.value.value, Is.EqualTo(-8f));
        }

        [Test]
        public void Given_DarkHoverLeafApplied_When_DarkTogglesOffAndBackOnWithoutRehover_Then_PayloadReapplies()
        {
            // Arrange — applied while dark AND hovered, then the outer (dark) gate closes while the
            // pointer never leaves the element.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: applied while dark AND hovered");
            VelvetTheme.IsDark = false;
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: cleared while light");

            // Act — dark returns; no new pointer event has fired.
            VelvetTheme.IsDark = true;

            // Assert — the continuously-held hover still counts, matching CSS's live :hover.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_DarkHoverLeafReopenedWhileHovered_When_ThePointerFinallyLeaves_Then_PayloadClears()
        {
            // Arrange — the close/reopen cycle above, payload re-applied via the retained hover.
            var leaf = MountLeaf("dark:hover:bg-hot");
            VelvetTheme.IsDark = true;
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            VelvetTheme.IsDark = false;
            VelvetTheme.IsDark = true;
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: re-applied after reopen");

            // Act — the pointer leaves for real.
            using (var evt = PointerOutEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the retained manipulator still tracks the live edge and clears.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }
    }
}
