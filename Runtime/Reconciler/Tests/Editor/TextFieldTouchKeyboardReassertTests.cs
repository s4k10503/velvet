using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a declared <c>keyboardType:</c> or <c>autoCorrection:</c> holds across the engine's own
    /// reset of both, which Enter in a single-line field makes as it hands focus from the input to the field.
    /// The two Enter cases run a control beside the declared field — the same value written from a
    /// <c>refCallback:</c>, which Velvet does not own — so each fails rather than passes where the engine
    /// stops resetting.
    /// </summary>
    internal sealed class TextFieldTouchKeyboardReassertTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static StateUpdater<bool> s_setDeclared;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_setDeclared = default;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private TextField Q(string name) => _host.Root.Q<TextField>(name);

        // The same forced style pass as FocusScopeTests' Mount, and for the same reason.
        private void Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        // Enter in a single-line field, then focus back into its input.
        private static void PressEnterAndRefocus(TextField field)
        {
            var input = Input(field);
            input.Focus();
            using (var enter = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None))
            {
                enter.target = input;
                input.SendEvent(enter);
            }

            input.Focus();
        }

        [Component]
        private static VNode KeyboardTypeHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "declared", keyboardType: TouchScreenKeyboardType.NumberPad),
            V.TextField(name: "control", refCallback: el =>
            {
                ((TextField)el).keyboardType = TouchScreenKeyboardType.NumberPad;
                return () => { };
            }),
        });

        [Component]
        private static VNode AutoCorrectionHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "declared", autoCorrection: true),
            V.TextField(name: "control", refCallback: el =>
            {
                ((TextField)el).autoCorrection = true;
                return () => { };
            }),
        });

        [Component]
        private static VNode DroppingHost()
        {
            var (declared, setDeclared) = Hooks.UseState(true);
            s_setDeclared = setDeclared;
            return V.TextField(
                name: "field",
                keyboardType: declared ? TouchScreenKeyboardType.NumberPad : (TouchScreenKeyboardType?)null);
        }

        [Test]
        public void Given_ADeclaredKeyboardType_When_EnterHandsFocusToTheFieldAndFocusComesBack_Then_TheFieldCarriesItAgain()
        {
            // Arrange
            Mount(KeyboardTypeHost);
            var declared = Q("declared");
            var control = Q("control");

            // Act
            PressEnterAndRefocus(control);
            PressEnterAndRefocus(declared);

            // Assert
            Assert.That(
                (control.keyboardType, declared.keyboardType),
                Is.EqualTo((TouchScreenKeyboardType.Default, TouchScreenKeyboardType.NumberPad)));
        }

        [Test]
        public void Given_ADeclaredAutoCorrectionFlag_When_EnterHandsFocusToTheFieldAndFocusComesBack_Then_TheFieldCarriesItAgain()
        {
            // Arrange
            Mount(AutoCorrectionHost);
            var declared = Q("declared");
            var control = Q("control");

            // Act
            PressEnterAndRefocus(control);
            PressEnterAndRefocus(declared);

            // Assert
            Assert.That((control.autoCorrection, declared.autoCorrection), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AKeyboardTypeALaterRenderDropped_When_FocusComesIn_Then_AValueWrittenSinceStays()
        {
            // Arrange
            Mount(DroppingHost);
            var field = Q("field");
            s_setDeclared.Invoke(false);
            _mounted.FlushStateForTest();
            field.keyboardType = TouchScreenKeyboardType.PhonePad;

            // Act
            Input(field).Focus();

            // Assert — the identity term is what makes this a reading of the field the declaration was on.
            Assert.That(
                (ReferenceEquals(Q("field"), field), field.keyboardType),
                Is.EqualTo((true, TouchScreenKeyboardType.PhonePad)));
        }
    }
}
