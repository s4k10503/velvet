using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the overlay that draws a text field's selected text above the engine's selection highlight:
    /// the rich text it composes, when it exists, and that it follows the selection. What the composed text
    /// looks like once drawn is not read back here.
    /// </summary>
    internal sealed class SelectionTextOverlayTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setDeclared;
        private static StateUpdater<int> s_setStep;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            VelvetStyleUtilities.AttachTo(_host.Root);
            s_setDeclared = default;
            s_setStep = default;
        }

        private void Settle()
        {
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static void SendGeometryChanged(VisualElement element)
        {
            using var changed = GeometryChangedEvent.GetPooled(Rect.zero, element.layout);
            changed.target = element;
            element.SendEvent(changed);
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private TextField Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            return _host.Root.Q<TextField>("field");
        }

        private static SelectionTextOverlay Overlay(TextField field) => field.Q<SelectionTextOverlay>();

        [Component]
        private static VNode TextColorHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(name: "field", value: "hello", className: declared ? "selection:text-white" : null);
        }

        // Step 0 colours the selected text white, 1 black, 2 drops the utility and 3 brings it back.
        [Component]
        private static VNode SteppingHost()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var className = step switch { 0 => "selection:text-white", 1 => "selection:text-black", 2 => null, _ => "selection:text-white" };
            return V.TextField(name: "field", value: "hello", className: className);
        }

        [Test]
        public void Given_ARunOutsideTheText_When_Composed_Then_ItIsClampedToTheText()
        {
            // Arrange
            var shown = "abc";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, -2, 9, null);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#FF>abc<alpha=#00>"));
        }

        [Test]
        public void Given_ASelectionTextUtility_When_TheOverlayIsBuilt_Then_ItTakesNoInputAndLiesOverTheInput()
        {
            // Arrange / Act
            var overlay = Overlay(Mount(TextColorHost));

            // Assert — every inline value below is one the overlay sets; an unset one reads as the Null keyword.
            Assert.That(
                (overlay.ClassListContains(SelectionTextOverlay.ClassName), overlay.pickingMode,
                    overlay.style.position.value,
                    overlay.style.marginLeft.keyword, overlay.style.paddingLeft.keyword, overlay.style.left.keyword,
                    overlay.style.top.keyword, overlay.style.width.keyword, overlay.style.height.keyword),
                Is.EqualTo((true, PickingMode.Ignore, Position.Absolute, StyleKeyword.Undefined,
                    StyleKeyword.Undefined, StyleKeyword.Undefined, StyleKeyword.Undefined, StyleKeyword.Undefined,
                    StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_AnOverlay_When_TheInputsGeometryChanges_Then_ItTakesTheInputsContentWidthAgain()
        {
            // Arrange
            var field = Mount(TextColorHost);
            var overlay = Overlay(field);
            var input = (TextElement)field.textEdition;
            overlay.style.width = 123f;

            // Act
            SendGeometryChanged(input);

            // Assert
            Assert.That(overlay.style.width.value.value, Is.EqualTo(input.contentRect.width));
        }

        [Test]
        public void Given_AnOverlayTheUtilityLeft_When_TheInputsGeometryChanges_Then_ItNoLongerFollows()
        {
            // Arrange
            var field = Mount(SteppingHost);
            var overlay = Overlay(field);
            s_setStep.Invoke(2);
            Settle();
            overlay.style.width = 123f;

            // Act
            SendGeometryChanged((TextElement)field.textEdition);

            // Assert
            Assert.That(overlay.style.width.value.value, Is.EqualTo(123f));
        }

        [Test]
        public void Given_ASelection_When_ARenderChangesTheTextColor_Then_TheOverlayTakesItWithoutATick()
        {
            // Arrange
            var field = Mount(SteppingHost);
            ((TextElement)field.textEdition).Focus();
            field.textSelection.SelectRange(1, 3);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act
            s_setStep.Invoke(1);
            Settle();

            // Assert
            Assert.That(Overlay(field).text, Is.EqualTo(SelectionTextOverlay.Compose("hello", 1, 3, Color.black)));
        }

        [Test]
        public void Given_ASelectionTextUtilityDroppedAndBroughtBack_When_ItReturns_Then_TheFieldCarriesAnOverlayAgain()
        {
            // Arrange
            var field = Mount(SteppingHost);
            s_setStep.Invoke(2);
            Settle();

            // Act
            s_setStep.Invoke(3);
            Settle();

            // Assert
            Assert.That(Overlay(field) != null, Is.True);
        }

        [Test]
        public void Given_ARunAndATextColor_When_Composed_Then_OnlyTheRunIsVisibleInThatColor()
        {
            // Arrange
            var shown = "hello";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 1, 3, Color.white);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00>h<color=#FFFFFFFF>el</color><alpha=#00>lo"));
        }

        [Test]
        public void Given_NoTextColor_When_Composed_Then_TheRunKeepsTheInheritedColor()
        {
            // Arrange
            var shown = "hello";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 0, 2, null);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#FF>he<alpha=#00>llo"));
        }

        [Test]
        public void Given_TextHoldingATagOpener_When_Composed_Then_TheOpenerIsEscaped()
        {
            // Arrange
            var shown = "a<b";

            // Act
            var composed = SelectionTextOverlay.Compose(shown, 0, 3, null);

            // Assert
            Assert.That(composed, Is.EqualTo("<alpha=#00><alpha=#FF>a<noparse><</noparse>b<alpha=#00>"));
        }

        [Test]
        public void Given_APasswordField_When_ItsShownTextIsRead_Then_ItIsTheMask()
        {
            // Arrange
            var field = new TextField { isPasswordField = true, maskChar = '*' };
            field.SetValueWithoutNotify("secret");

            // Act
            var shown = SelectionTextOverlay.Displayed((TextElement)field.textEdition);

            // Assert
            Assert.That(shown, Is.EqualTo("******"));
        }

        [Test]
        public void Given_ASelectionTextUtility_When_ALaterRenderDropsIt_Then_TheOverlayComesAndGoes()
        {
            // Arrange
            var field = Mount(TextColorHost);
            var whileDeclared = Overlay(field) != null;

            // Act
            s_setDeclared.Invoke(false);
            _mounted.FlushStateForTest();
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((whileDeclared, Overlay(field) != null), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ASelectionTextUtility_When_ARangeIsSelectedAndTheSchedulerTicks_Then_TheOverlayShowsThatRun()
        {
            // Arrange
            var field = Mount(TextColorHost);
            var input = (TextElement)field.textEdition;
            input.Focus();

            // Act
            field.textSelection.SelectRange(1, 3);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(Overlay(field).text, Is.EqualTo(SelectionTextOverlay.Compose("hello", 1, 3, Color.white)));
        }
    }
}
