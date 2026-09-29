using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The <c>focus-visible:</c> variant reads its panel's last input: after a real pointer press anywhere in the
    /// panel, a programmatic focus shows no ring until a key press or a navigation move, as React Aria's
    /// useFocusVisible reads its window's.
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
        {
            var elsewhere = _host.Root.Q<VisualElement>("elsewhere");
            using var keyDown = KeyDownEvent.GetPooled('\0', key, modifiers);
            keyDown.target = elsewhere;
            elsewhere.SendEvent(keyDown);
        }

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

        // GREEN_ON_BASE(characterization): the base lights the ring on every programmatic focus.
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

        // GREEN_ON_BASE(characterization): the base lights the ring on every programmatic focus.
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

        [Component]
        private static VNode LateStyledHost()
        {
            var (styled, setStyled) = Hooks.UseState(false);
            s_setStyled = setStyled;
            return Host(styled ? "focus-visible:ring-kbd" : "");
        }

        [Test]
        public void Given_AFocusVisibleVariantAddedToAMountedElement_When_ItIsFocusedAfterAPointerPress_Then_TheFocusVisiblePayloadIsNotApplied()
        {
            // Arrange — the variant hooks an element that already sits in the panel.
            _mounted = V.Mount(_host.Root, V.Component(LateStyledHost, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            s_setStyled.Invoke(true);
            _mounted.FlushStateForTest();
            var target = _host.Root.Q<VisualElement>("target");
            PressElsewhere();

            // Act
            target.Focus();

            // Assert
            Assert.That((target.panel.focusController.focusedElement == target, target.ClassListContains("ring-kbd")),
                Is.EqualTo((true, false)));
        }
    }
}
