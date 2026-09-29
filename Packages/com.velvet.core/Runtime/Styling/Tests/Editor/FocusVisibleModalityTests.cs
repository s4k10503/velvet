using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The <c>focus-visible:</c> variant reads the last input to any panel Velvet renders into, React Aria's
    /// process-wide input modality: after a real pointer press, a programmatic focus shows no ring until a key
    /// press or a navigation move, and a focused element's ring follows later input.
    /// </summary>
    internal sealed class FocusVisibleModalityTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setStyled;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setStyled = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private static VNode Host(string targetClass) => V.Div(children: new VNode[]
        {
            V.Button(name: "target", className: targetClass),
            V.Div(name: "elsewhere"),
            V.TextField(name: "field"),
        });

        // Mounted detached and then attached, so the variant hooks the target before it has a panel.
        private VisualElement MountDetachedThenAttach()
        {
            var detached = new VisualElement();
            _mounted = V.Mount(detached, Host("focus-visible:ring-kbd"));
            _host.Root.Add(detached);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            return _host.Root.Q<VisualElement>("target");
        }

        private void PressElsewhere()
        {
            var elsewhere = _host.Root.Q<VisualElement>("elsewhere");
            using var press = PointerDownEvent.GetPooled();
            press.target = elsewhere;
            elsewhere.SendEvent(press);
        }

        private void PressKey(KeyCode key, EventModifiers modifiers = EventModifiers.None)
            => PressKeyOn(_host.Root.Q<VisualElement>("elsewhere"), key, modifiers);

        private static void PressKeyOn(VisualElement on, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            using var keyDown = KeyDownEvent.GetPooled('\0', key, modifiers);
            keyDown.target = on;
            on.SendEvent(keyDown);
        }

        // A real press, whose dispatch focuses the element it lands on.
        private static void PressOn(VisualElement on)
        {
            using var press = PointerDownEvent.GetPooled();
            press.target = on;
            on.SendEvent(press);
        }

        private VisualElement EditableTextOfTheField()
            => _host.Root.Q<TextField>("field").Query<TextElement>().Where(text => !((ITextEdition)text).isReadOnly).First();

        [Test]
        public void Given_APointerPressElsewhereInThePanel_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base lights the ring for a programmatic focus whatever was
        // pressed elsewhere.
        [Test]
        public void Given_APointerPressThenAKeyPress_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();
            PressKey(KeyCode.A);

            // Act
            target.Focus();

            // Assert
            Assert.That(target.ClassListContains("ring-kbd"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base lights the ring for a programmatic focus whatever was
        // pressed elsewhere.
        [Test]
        public void Given_APointerPressThenANavigationMove_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();
            var elsewhere = _host.Root.Q<VisualElement>("elsewhere");
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.None))
            {
                move.target = elsewhere;
                elsewhere.SendEvent(move);
            }

            // Act
            target.Focus();

            // Assert
            Assert.That(target.ClassListContains("ring-kbd"), Is.True);
        }

        [Test]
        public void Given_APointerPressThenAShiftKeyAlone_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();
            PressKey(KeyCode.LeftShift, EventModifiers.Shift);

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerPressThenACtrlChord_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();
            PressKey(KeyCode.C, EventModifiers.Control);

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerPressThenACommandChord_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressElsewhere();
            PressKey(KeyCode.C, EventModifiers.Command);

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerMoveElsewhereInThePanel_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            var elsewhere = _host.Root.Q<VisualElement>("elsewhere");
            using (var move = PointerMoveEvent.GetPooled())
            {
                move.target = elsewhere;
                elsewhere.SendEvent(move);
            }

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerPressThenAnAltChord_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsAppliedOnlyOnAMac()
        {
            // Arrange — React Aria's isValidKey counts an Alt chord as keyboard use on a Mac alone.
            var target = MountDetachedThenAttach();
            PressElsewhere();
            PressKey(KeyCode.C, EventModifiers.Alt);
            var onAMac = Application.platform is RuntimePlatform.OSXEditor or RuntimePlatform.OSXPlayer;

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, onAMac)));
        }

        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_AKeyIsPressed_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);
            var darkAfterThePress = !target.ClassListContains("ring-kbd");

            // Act
            PressKeyOn(target, KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, darkAfterThePress, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_AKeyIsReleased_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);
            var darkAfterThePress = !target.ClassListContains("ring-kbd");

            // Act
            using (var keyUp = KeyUpEvent.GetPooled('\0', KeyCode.A, EventModifiers.None))
            {
                keyUp.target = target;
                target.SendEvent(keyUp);
            }

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, darkAfterThePress, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base never relights a ring on a key press. This case pins that
        // a blurred element stops following the panel's input.
        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_ItBlursAndAKeyIsPressed_Then_TheFocusVisiblePayloadStaysOff()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);
            target.Blur();

            // Act
            PressKeyOn(_host.Root.Q<VisualElement>("elsewhere"), KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement, target.ClassListContains("ring-kbd")),
                Is.EqualTo(((Focusable)null, false)));
        }

        // GREEN_ON_BASE(characterization): the base never relights a ring on a key press. This case pins that a
        // key typed into a text input leaves it dark, as React Aria's isKeyboardFocusEvent does.
        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_AKeyIsTypedIntoATextInput_Then_TheFocusVisiblePayloadStaysOff()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);

            // Act
            PressKeyOn(EditableTextOfTheField(), KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_EscapeIsPressedInATextInput_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);

            // Act
            PressKeyOn(EditableTextOfTheField(), KeyCode.Escape);

            // Assert
            Assert.That(target.ClassListContains("ring-kbd"), Is.True);
        }

        [Component]
        private static VNode LateStyledHost()
        {
            var (styled, setStyled) = Hooks.UseState(false);
            s_setStyled = setStyled;
            return Host(styled ? "focus-visible:ring-kbd" : "");
        }

        [Test]
        public void Given_APointerPressBeforeThePanelHasAnyFocusVisibleUser_When_OneMountsAndIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange — the press is the panel's last input when its first focus-visible user mounts.
            _mounted = V.Mount(_host.Root, V.Component(LateStyledHost, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            PressElsewhere();
            s_setStyled.Invoke(true);
            _mounted.FlushStateForTest();
            var target = _host.Root.Q<VisualElement>("target");

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerPressInAnotherPanel_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange — as a click in the main panel opens a dialog in a layer host.
            var target = MountDetachedThenAttach();
            using var other = new HeadlessEditorPanelHost();
            using var otherMounted = V.Mount(other.Root, V.Div(name: "pressed"));
            var pressed = other.Root.Q<VisualElement>("pressed");
            PressOn(pressed);

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base never relights a ring on a key press. This case pins that
        // a settled focus loss stops the element following the panel's input.
        [Test]
        public void Given_ATargetWhoseFocusLossWasSettled_When_AKeyIsPressed_Then_TheFocusVisiblePayloadStaysOff()
        {
            // Arrange — the settle a reverted containment landing receives.
            var target = MountDetachedThenAttach();
            PressOn(target);
            VariantSettleSweep.ForEach(target, _mounted.Root.Reconciler.Context, static settler => settler.SettleFocusLoss());

            // Act
            PressKeyOn(target, KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base never relights a ring on a key press. This case pins that
        // a variant removed while its element holds focus stops following the panel's input.
        [Test]
        public void Given_AFocusVisibleVariantRemovedWhileItsElementHoldsFocus_When_AKeyIsPressed_Then_TheFocusVisiblePayloadStaysOff()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Component(LateStyledHost, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            s_setStyled.Invoke(true);
            _mounted.FlushStateForTest();
            var target = _host.Root.Q<VisualElement>("target");
            PressOn(target);
            s_setStyled.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            PressKeyOn(target, KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APointerReleaseElsewhereInThePanel_When_TheTargetIsFocusedProgrammatically_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressKey(KeyCode.A);
            var elsewhere = _host.Root.Q<VisualElement>("elsewhere");
            using (var release = PointerUpEvent.GetPooled())
            {
                release.target = elsewhere;
                elsewhere.SendEvent(release);
            }

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }

        // GREEN_ON_BASE(characterization): the base reads no panel input at all. This case pins that an element
        // in no panel reads none of the process-wide one either.
        [Test]
        public void Given_APointerPressInAPanel_When_AnElementInNoPanelReceivesFocus_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            MountDetachedThenAttach();
            PressElsewhere();
            var offPanel = new VisualElement();
            using var offPanelMounted = V.Mount(offPanel, V.Label(name: "leaf", className: "focus-visible:ring-kbd", text: "x"));
            var leaf = offPanel.Q<Label>("leaf");

            // Act
            using (var focus = FocusEvent.GetPooled()) leaf.SimulateEvent(focus);

            // Assert
            Assert.That(leaf.ClassListContains("ring-kbd"), Is.True);
        }

        [Test]
        public void Given_ATargetFocusedByAPressOnIt_When_ANavigationMoveLeavesFocusWhereItIs_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);
            var darkAfterThePress = !target.ClassListContains("ring-kbd");

            // Act
            using (var move = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.None))
            {
                move.target = target;
                target.SendEvent(move);
            }

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, darkAfterThePress, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_ATargetBlurredAndFocusedAgainByAPress_When_AKeyIsPressed_Then_TheFocusVisiblePayloadIsApplied()
        {
            // Arrange
            var target = MountDetachedThenAttach();
            PressOn(target);
            target.Blur();
            PressOn(target);

            // Act
            PressKeyOn(target, KeyCode.A);

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, true)));
        }
    }
}
