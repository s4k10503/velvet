using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for the interactive variant manipulators, driven by REAL UI events
    /// (<see cref="VisualElementTestExtensions.SimulateEvent{TEvent}"/>) and the <see cref="VelvetTheme"/> toggle
    /// rather than by inspecting the manipulator in isolation. <c>hover:</c> / <c>focus:</c> / <c>active:</c> must
    /// add their payload class on pointer-enter / focus / pointer-down and remove it on the matching exit event;
    /// <c>dark:</c> must add its payload when <see cref="VelvetTheme.IsDark"/> flips on and remove it when it flips
    /// off. These manipulators register on the element itself (not the panel), so the event path exercises them
    /// without a live panel. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantBehaviorTests
    {
        private VisualElement _root;
        private bool _darkBefore;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            // dark: reads global theme state; pin it off so each case starts from a known baseline and restore after.
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = false;
        }

        private MountedTree _mounted;

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
        public void Given_AHoverVariantLeaf_When_ThePointerGoesOver_Then_ThePayloadClassIsApplied()
        {
            // Arrange — a leaf with hover:bg-hot, not hovered.
            var leaf = MountLeaf("hover:bg-hot");
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: payload off before hover");

            // Act — the pointer goes over it (hover is driven by the bubbling over/out pair).
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the hover payload class is applied.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_AHoveredLeaf_When_ThePointerGoesOut_Then_ThePayloadClassIsRemoved()
        {
            // Arrange — a leaf hovered so its payload is applied.
            var leaf = MountLeaf("hover:bg-hot");
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: payload on while hovered");

            // Act — the pointer goes out past the leaf's (panel-less, zero) bounds.
            using (var evt = PointerOutEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the hover payload class is removed.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_AFocusVariantLeaf_When_ItGainsFocus_Then_ThePayloadClassIsApplied()
        {
            // Arrange — a leaf with focus:ring-on, unfocused.
            var leaf = MountLeaf("focus:ring-on");
            Assume.That(leaf.ClassListContains("ring-on"), Is.False, "Precondition: payload off before focus");

            // Act — the leaf gains focus.
            using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the focus payload class is applied.
            Assert.IsTrue(leaf.ClassListContains("ring-on"));
        }

        [Test]
        public void Given_AFocusVisibleLeaf_When_ItGainsFocusWithoutAPointerDown_Then_ThePayloadClassIsApplied()
        {
            // Arrange — a leaf with focus-visible:ring-kbd, unfocused (keyboard/programmatic focus path).
            var leaf = MountLeaf("focus-visible:ring-kbd");
            Assume.That(leaf.ClassListContains("ring-kbd"), Is.False, "Precondition: payload off before focus");

            // Act — it gains focus with no preceding pointer-down (Tab navigation / Focus()).
            using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the focus-visible payload class is applied.
            Assert.IsTrue(leaf.ClassListContains("ring-kbd"));
        }

        [Test]
        public void Given_AFocusVisibleLeaf_When_FocusFollowsAPointerDown_Then_ThePayloadClassIsNotApplied()
        {
            // Arrange — a leaf with focus-visible:ring-kbd, unfocused.
            var leaf = MountLeaf("focus-visible:ring-kbd");
            Assume.That(leaf.ClassListContains("ring-kbd"), Is.False, "Precondition: payload off before interaction");

            // Act — a pointer-down causes the focus (the click-to-focus path).
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
            using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — focus-visible stays off for pointer focus (mirrors CSS :focus-visible).
            Assert.IsFalse(leaf.ClassListContains("ring-kbd"));
        }

        [Test]
        public void Given_AFocusVisibleLeafLitByKeyboard_When_ThePointerGoesDown_Then_ThePayloadClassIsRemoved()
        {
            // Arrange — a focus-visible leaf lit by a keyboard focus.
            var leaf = MountLeaf("focus-visible:ring-kbd");
            using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("ring-kbd"), Is.True, "Precondition: payload on after keyboard focus");

            // Act — the user then interacts with a pointer-down.
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the focus ring is dropped on mouse interaction (browser parity).
            Assert.IsFalse(leaf.ClassListContains("ring-kbd"));
        }

        [Test]
        public void Given_AFocusVisibleLeafLitByKeyboard_When_ItLosesFocus_Then_ThePayloadClassIsRemoved()
        {
            // Arrange — a focus-visible leaf lit by a keyboard focus.
            var leaf = MountLeaf("focus-visible:ring-kbd");
            using (var evt = FocusEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("ring-kbd"), Is.True, "Precondition: payload on while focused");

            // Act — it loses focus.
            using (var evt = BlurEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the focus-visible payload class is removed.
            Assert.IsFalse(leaf.ClassListContains("ring-kbd"));
        }

        [Test]
        public void Given_AnActiveVariantLeaf_When_ThePointerGoesDown_Then_ThePayloadClassIsApplied()
        {
            // Arrange — a leaf with active:scale-down, not pressed.
            var leaf = MountLeaf("active:scale-down");
            Assume.That(leaf.ClassListContains("scale-down"), Is.False, "Precondition: payload off before press");

            // Act — the pointer goes down on it.
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the active payload class is applied.
            Assert.IsTrue(leaf.ClassListContains("scale-down"));
        }

        [Test]
        public void Given_APressedLeaf_When_ThePointerGoesUp_Then_ThePayloadClassIsRemoved()
        {
            // Arrange — a leaf pressed so its active payload is applied.
            var leaf = MountLeaf("active:scale-down");
            using (var evt = PointerDownEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(leaf.ClassListContains("scale-down"), Is.True, "Precondition: payload on while pressed");

            // Act — the pointer is released.
            using (var evt = PointerUpEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — the active payload class is removed.
            Assert.IsFalse(leaf.ClassListContains("scale-down"));
        }

        [Test]
        public void Given_ADarkVariantLeaf_When_ThemeFlipsToDark_Then_ThePayloadClassIsApplied()
        {
            // Arrange — a leaf with dark:bg-night while the theme is light.
            var leaf = MountLeaf("dark:bg-night");
            Assume.That(leaf.ClassListContains("bg-night"), Is.False, "Precondition: payload off in light theme");

            // Act — the theme flips to dark.
            VelvetTheme.IsDark = true;

            // Assert — the dark payload class is applied.
            Assert.IsTrue(leaf.ClassListContains("bg-night"));
        }

        [Test]
        public void Given_ADarkLeafInDarkTheme_When_ThemeFlipsBackToLight_Then_ThePayloadClassIsRemoved()
        {
            // Arrange — a dark: leaf whose payload is applied because the theme is dark.
            var leaf = MountLeaf("dark:bg-night");
            VelvetTheme.IsDark = true;
            Assume.That(leaf.ClassListContains("bg-night"), Is.True, "Precondition: payload on in dark theme");

            // Act — the theme flips back to light.
            VelvetTheme.IsDark = false;

            // Assert — the dark payload class is removed.
            Assert.IsFalse(leaf.ClassListContains("bg-night"));
        }
    }

    /// <summary>
    /// Behavioural coverage for the element-local <c>checked:</c> variant, driven by a control's
    /// <c>ChangeEvent&lt;bool&gt;</c> (a <see cref="Toggle"/>). The payload applies while the control is
    /// checked and clears when it is unchecked; a control mounted already-checked lights up on attach
    /// (the change event only fires on a change), and a value written through a fully-controlled prop —
    /// which raises no change event at all — tracks the same way. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class CheckedVariantBehaviorTests
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

        [Test]
        public void Given_CheckedVariantToggle_When_Checked_Then_PayloadApplied()
        {
            // Arrange — a Toggle with checked:bg-hot, currently unchecked.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot"));
            var leaf = _root.Q<Toggle>("leaf");
            Assume.That(leaf.ClassListContains("bg-hot"), Is.False, "Precondition: payload off while unchecked");

            // Act — the toggle is checked.
            leaf.SimulateChange(true);

            // Assert — the checked: payload is applied.
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_CheckedVariantToggleApplied_When_Unchecked_Then_PayloadRemoved()
        {
            // Arrange — a checked Toggle whose payload is applied.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot"));
            var leaf = _root.Q<Toggle>("leaf");
            leaf.SimulateChange(true);
            Assume.That(leaf.ClassListContains("bg-hot"), Is.True, "Precondition: payload on while checked");

            // Act — the toggle is unchecked.
            leaf.SimulateChange(false);

            // Assert — the payload clears.
            Assert.IsFalse(leaf.ClassListContains("bg-hot"));
        }

        [Test]
        public void Given_ACheckedVariantToggle_When_ItsValueArrivesThroughAControlledProp_Then_ThePayloadApplied()
        {
            // Arrange — a Toggle whose checked state is owned by the props (the ordinary controlled shape),
            // rendered unchecked.
            using var scope = new ReconcilerScope();
            var renderedUnchecked = new VNode[] { V.Toggle(name: "leaf", className: "checked:bg-hot", value: false) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), renderedUnchecked);
            var leaf = scope.Root.Q<Toggle>("leaf");
            var beforeTheControlledWrite = leaf.ClassListContains("bg-hot");

            // Act — the owner re-renders with the value flipped; no user interaction, so the control is
            // written without notification.
            scope.Reconciler.Reconcile(scope.Root, renderedUnchecked,
                new VNode[] { V.Toggle(name: "leaf", className: "checked:bg-hot", value: true) });

            // Assert — folded rather than assumed: a payload already applied while unchecked would make the
            // second term true without the write under test having done anything.
            Assert.That(
                (beforeTheControlledWrite, leaf.ClassListContains("bg-hot")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ACheckedVariantToggleApplied_When_AControlledPropUnchecksIt_Then_ThePayloadRemoved()
        {
            // Arrange — a controlled Toggle rendered checked, payload on.
            using var scope = new ReconcilerScope();
            var renderedChecked = new VNode[] { V.Toggle(name: "leaf", className: "checked:bg-hot", value: true) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), renderedChecked);
            var leaf = scope.Root.Q<Toggle>("leaf");
            var beforeTheControlledWrite = leaf.ClassListContains("bg-hot");

            // Act — the owner re-renders with the value flipped back.
            scope.Reconciler.Reconcile(scope.Root, renderedChecked,
                new VNode[] { V.Toggle(name: "leaf", className: "checked:bg-hot", value: false) });

            // Assert — folded rather than assumed: a payload that never applied would make the second term
            // false with the write under test doing nothing.
            Assert.That(
                (beforeTheControlledWrite, leaf.ClassListContains("bg-hot")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ToggleMountedAlreadyChecked_When_Mounted_Then_PayloadAppliedOnAttach()
        {
            // Arrange / Act — a Toggle mounted with value: true; ChangeEvent<bool> never fires, so the payload
            // must be applied from the attach-time read of the control's current checked state.
            _mounted = V.Mount(_root, V.Toggle(name: "leaf", className: "checked:bg-hot", value: true));
            var leaf = _root.Q<Toggle>("leaf");

            // Assert
            Assert.IsTrue(leaf.ClassListContains("bg-hot"));
        }
    }
}
