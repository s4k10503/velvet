using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.TextField</c>'s <c>maxLength:</c> against HTML <c>maxlength</c>: the limit holds for what
    /// the user types or pastes and not for a value written from code. Typing and pasting go through the
    /// engine's own key and command handling on a real editor panel, so the cases also pin the engine
    /// behaviour <c>FiberPropApplier.LimitUserEdit</c> and <c>FiberPropApplier.TryGetReportedValue</c> are
    /// written against: an edit by the user sends an <see cref="InputEvent"/>, an assignment to
    /// <c>value</c> does not, and a non-delayed field has set its value by the time that event is handled.
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldMaxLengthTests
    {
        private HeadlessEditorPanelHost _host;
        private Reconciler _reconciler;
        private VNode[] _tree;
        private string _clipboard;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _reconciler = new Reconciler();
            _tree = Array.Empty<VNode>();
            _clipboard = GUIUtility.systemCopyBuffer;
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.systemCopyBuffer = _clipboard;
            _reconciler?.Dispose();
            _reconciler = null;
            _host?.Dispose();
            _host = null;
            VNodePoolTestAccess.ClearTextFieldPoolForTest();
        }

        private void Render(params VNode[] next)
        {
            _reconciler.Reconcile(_host.Root, _tree, next);
            _tree = next;
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private TextField Mount(params VNode[] next)
        {
            Render(next);
            return (TextField)_host.Root.ElementAt(0);
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        private static void PlaceCaret(TextField field, int index)
        {
            Input(field).Focus();
            field.textSelection.SelectRange(index, index);
        }

        private static void Press(TextField field, char character, KeyCode keyCode)
        {
            using var key = KeyDownEvent.GetPooled(character, keyCode, EventModifiers.None);
            key.target = Input(field);
            Input(field).SendEvent(key);
        }

        private static void Type(TextField field, char character) => Press(field, character, KeyCode.None);

        // The touch keyboard's poll hands the engine text through ITextEdition.UpdateText with no key or
        // command before it. The method is internal to the engine, so it is reached by reflection; a
        // missing one fails the case that calls it.
        private static void Poll(TextField field, string text)
        {
            var update = typeof(ITextEdition).GetMethod(
                "UpdateText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                              | System.Reflection.BindingFlags.Public);
            if (update == null)
            {
                throw new MissingMethodException(nameof(ITextEdition), "UpdateText");
            }

            update.Invoke(Input(field), new object[] { text });
        }

        private static void Paste(TextField field, string text)
        {
            GUIUtility.systemCopyBuffer = text;
            using var command = ExecuteCommandEvent.GetPooled("Paste");
            command.target = Input(field);
            Input(field).SendEvent(command);
        }

        [Test]
        public void Given_AControlledValueLongerThanTheLimit_When_TheFieldIsMounted_Then_TheValueAndTheShownTextStayWhole()
        {
            // Arrange
            var tree = V.TextField(value: "abcdef", maxLength: 3);

            // Act
            var field = Mount(tree);

            // Assert
            Assert.That((field.value, field.text), Is.EqualTo(("abcdef", "abcdef")));
        }

        [Test]
        public void Given_ALimitedField_When_CodeAssignsALongerValue_Then_TheValueAndTheShownTextStayWhole()
        {
            // Arrange
            var field = Mount(V.TextField(maxLength: 3));

            // Act
            field.value = "abcdef";

            // Assert
            Assert.That((field.value, field.text), Is.EqualTo(("abcdef", "abcdef")));
        }

        // The assignment above is told from an edit by the user by the InputEvent only the latter sends.
        [Test]
        public void Given_ALimitedField_When_CodeAssignsALongerValue_Then_NoInputEventIsSent()
        {
            // Arrange
            var field = Mount(V.TextField(maxLength: 3));
            var sent = 0;
            field.RegisterCallback<InputEvent>(_ => sent++);

            // Act
            field.value = "abcdef";

            // Assert
            Assert.That(sent, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base sends no InputEvent for a code assignment to a field with no limit.
        // It pins the engine fact LimitUserEdit relies on where it can hold on the base; the limited field
        // above is the reading that differs there, since the engine's cut makes the text differ from the value.
        [Test]
        public void Given_AFieldWithNoLimit_When_CodeAssignsAValue_Then_NoInputEventIsSent()
        {
            // Arrange
            var field = Mount(V.TextField());
            var sent = 0;
            field.RegisterCallback<InputEvent>(_ => sent++);

            // Act
            field.value = "abcdef";

            // Assert
            Assert.That(sent, Is.EqualTo(0));
        }

        // A raw listener hears the change event after LimitUserEdit has run, so it carries the text before
        // the refusal while the field already holds the value it ended with. The first event is an accepted
        // character, where the two agree.
        [Test]
        public void Given_AFieldWithRoomForOneCharacter_When_TheUserTypesTwo_Then_ARawChangeListenerSeesTheRefusedTextBesideTheCorrectedValue()
        {
            // Arrange
            var field = Mount(V.TextField(value: "ac", maxLength: 3));
            var heard = new List<string>();
            field.RegisterValueChangedCallback(evt => heard.Add(evt.newValue + "/" + field.value));
            PlaceCaret(field, 1);
            Type(field, 'b');

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(string.Join("|", heard), Is.EqualTo("abc/abc|abxc/abc"));
        }

        // Pasting over a selection leaves the room the limit gives less the text that stays, and a value
        // longer than the limit leaves none, so the selection goes and nothing is put in its place.
        [Test]
        public void Given_AValueLongerThanTheLimit_When_TheUserPastesOverASelection_Then_NothingIsInserted()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 3));
            Input(field).Focus();
            field.textSelection.SelectRange(4, 2);

            // Act
            Paste(field, "XYZ");

            // Assert
            Assert.That(field.text, Is.EqualTo("abef"));
        }

        // The replacement is as long as the selection, so the edit does not lengthen the text.
        [Test]
        public void Given_AValueLongerThanTheLimit_When_TheUserReplacesASelectionWithAsManyCharacters_Then_TheReplacementIsKept()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 3));
            Input(field).Focus();
            field.textSelection.SelectRange(4, 2);

            // Act
            Paste(field, "XY");

            // Assert
            Assert.That(field.text, Is.EqualTo("abXYef"));
        }

        // GREEN_ON_BASE(characterization): the base never edits a value when the engine's limit moves.
        // The declared limit of four is below the value, and the drop puts the built limit of three back.
        [Test]
        public void Given_AValueLongerThanTheDroppedLimit_When_ALaterRenderDropsMaxLength_Then_TheValueIsUntouched()
        {
            // Arrange
            var declared = new FiberElementProps
            {
                FieldValue = "hello",
                TextField = new TextFieldSettings(MaxLength: 4),
            };
            var dropped = new FiberElementProps { FieldValue = "hello" };
            var field = Mount(V.Custom<TextFieldBuiltLimitTests.BuiltWithThree>(props: declared));

            // Act
            Render(V.Custom<TextFieldBuiltLimitTests.BuiltWithThree>(props: dropped));

            // Assert
            Assert.That(field.value, Is.EqualTo("hello"));
        }

        // Selecting "bc" and pasting "bxy" shares its first character with the selection, which the text
        // alone cannot tell from an insertion one place on.
        [Test]
        public void Given_AValueLongerThanTheLimit_When_TheUserPastesOverASelectionTheInsertionStartsLike_Then_NothingIsInserted()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 3));
            Input(field).Focus();
            field.textSelection.SelectRange(3, 1);

            // Act
            Paste(field, "bxy");

            // Assert
            Assert.That(field.text, Is.EqualTo("adef"));
        }

        // GREEN_ON_BASE(characterization): the base deletes forward and leaves the caret where it was.
        // Deleting does not lengthen the text, so the limit leaves the caret to the engine.
        [Test]
        public void Given_AFieldHoldingItsLimit_When_TheUserDeletesForward_Then_TheCaretStays()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));
            PlaceCaret(field, 1);

            // Act
            Press(field, '\0', KeyCode.Delete);

            // Assert
            Assert.That(field.textSelection.cursorIndex, Is.EqualTo(1));
        }

        // The one write of code that is limited: on a single-line field the shown text strips the line
        // break, so the assignment makes the engine announce a change of the shown text as it does for
        // typing, and nothing tells the two apart.
        [Test]
        public void Given_ASingleLineLimitedField_When_CodeAssignsAValueWithALineBreak_Then_TheValueIsLimitedLikeAnEdit()
        {
            // Arrange
            var field = Mount(V.TextField(maxLength: 3));

            // Act
            field.value = "ab\ncd";

            // Assert
            Assert.That(field.value, Is.EqualTo("ab\nc"));
        }

        // The assignment of a refCallback is code too, and a value without a line break sends no InputEvent.
        [Test]
        public void Given_ARefCallbackAssigningALongerValue_When_TheLimitedFieldIsMounted_Then_TheValueAndTheShownTextStayWhole()
        {
            // Arrange
            Func<VisualElement, Action> assign = el =>
            {
                ((TextField)el).value = "abcdef";
                return () => { };
            };

            // Act
            var field = Mount(V.TextField(maxLength: 3, refCallback: assign));

            // Assert
            Assert.That((field.value, field.text), Is.EqualTo(("abcdef", "abcdef")));
        }

        [Test]
        public void Given_ALimitedField_When_ARenderPatchesAValueLongerThanTheLimit_Then_TheValueAndTheShownTextStayWhole()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));

            // Act
            Render(V.TextField(value: "abcdef", maxLength: 3));

            // Assert
            Assert.That((field.value, field.text), Is.EqualTo(("abcdef", "abcdef")));
        }

        // Multiline coming on re-shows the value with its line break, which is longer than the single-line
        // display the field showed before.
        [Test]
        public void Given_ALimitedSingleLineField_When_ARenderTurnsMultilineOn_Then_TheValueIsReShownWhole()
        {
            // Arrange
            var field = Mount(V.TextField(value: "a\nbcdef", maxLength: 3));

            // Act
            Render(V.TextField(value: "a\nbcdef", maxLength: 3, multiline: true));

            // Assert
            Assert.That(field.text, Is.EqualTo("a\nbcdef"));
        }

        // GREEN_ON_BASE(characterization): the base cuts the shown text to its limit whatever handed it over.
        // It pins that text handed over with no key behind it, as the touch keyboard's poll does, is cut to
        // the limit here too.
        [Test]
        public void Given_AFieldHoldingItsLimit_When_TheShownTextGrowsWithNoKeyBehindIt_Then_ItIsCutFromTheEnd()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));

            // Act
            Poll(field, "abcdef");

            // Assert
            Assert.That(field.text, Is.EqualTo("abc"));
        }

        // The cut never goes below the length the text had, so an over-long value can only shrink.
        [Test]
        public void Given_AValueLongerThanTheLimit_When_TheShownTextGrowsWithNoKeyBehindIt_Then_ItIsCutToWhatItWas()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 3));

            // Act
            Poll(field, "abcdefg");

            // Assert
            Assert.That(field.text, Is.EqualTo("abcdef"));
        }

        // GREEN_ON_BASE(characterization): the base cuts the shown text to its limit whatever handed it over.
        // A Backspace at index 0 changes nothing and leaves a note that no event consumes; the poll that
        // follows must still be cut from the end.
        [Test]
        public void Given_ABackspaceAtTheStartThatChangedNothing_When_TheShownTextGrowsWithNoKeyBehindIt_Then_ItIsCutFromTheEnd()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));
            PlaceCaret(field, 0);
            Press(field, '\b', KeyCode.Backspace);

            // Act
            Poll(field, "abcdef");

            // Assert
            Assert.That(field.text, Is.EqualTo("abc"));
        }

        // GREEN_ON_BASE(characterization): the base cuts the shown text to its limit whatever handed it over.
        // Enter in a single-line field is not an edit and leaves no note.
        [Test]
        public void Given_EnterPressedInASingleLineField_When_TheShownTextGrowsWithNoKeyBehindIt_Then_ItIsCutFromTheEnd()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));
            PlaceCaret(field, 3);
            Press(field, '\n', KeyCode.Return);

            // Act
            Poll(field, "abcdef");

            // Assert
            Assert.That(field.text, Is.EqualTo("abc"));
        }

        [Test]
        public void Given_AValueLongerThanTheNewLimit_When_ALaterRenderNarrowsMaxLength_Then_TheValueAndTheShownTextStayWhole()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 10));

            // Act
            Render(V.TextField(value: "abcdef", maxLength: 3));

            // Assert
            Assert.That((field.value, field.text), Is.EqualTo(("abcdef", "abcdef")));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAValueLongerThanTheLimit_When_FocusLeavesIt_Then_NothingIsReported()
        {
            // Arrange
            var reported = new List<string>();
            var field = Mount(V.TextField(value: "abcdef", isDelayed: true, maxLength: 3, onValueChanged: reported.Add));
            Input(field).Focus();

            // Act
            Input(field).Blur();

            // Assert
            Assert.That(reported, Is.Empty);
        }

        [Test]
        public void Given_AFieldHoldingItsLimit_When_TheUserTypesAtTheStart_Then_TheTextIsUnchanged()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));
            PlaceCaret(field, 0);

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(field.text, Is.EqualTo("abc"));
        }

        [Test]
        public void Given_AFieldHoldingItsLimit_When_TheUserTypesAtTheStart_Then_TheCaretStaysAtTheStart()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abc", maxLength: 3));
            PlaceCaret(field, 0);

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(field.textSelection.cursorIndex, Is.EqualTo(0));
        }

        // Zero is a limit that refuses insertions, where -1 declares none.
        [Test]
        public void Given_AValueLongerThanALimitOfZero_When_TheUserTypesAtTheStart_Then_TheTextIsUnchanged()
        {
            // Arrange
            var field = Mount(V.TextField(value: "ab", maxLength: 0));
            PlaceCaret(field, 0);

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(field.text, Is.EqualTo("ab"));
        }

        // The second character finds no room, and the first, typed before it, is kept in the middle of the
        // text rather than cut from the end.
        [Test]
        public void Given_AFieldWithRoomForOneCharacter_When_TheUserTypesTwoInTheMiddleOfTheText_Then_OnlyTheFirstIsInserted()
        {
            // Arrange
            var field = Mount(V.TextField(value: "ac", maxLength: 3));
            PlaceCaret(field, 1);
            Type(field, 'b');

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(field.text, Is.EqualTo("abc"));
        }

        [Test]
        public void Given_AValueLongerThanTheLimit_When_TheUserDeletesACharacter_Then_TheDeletionIsKept()
        {
            // Arrange
            var field = Mount(V.TextField(value: "abcdef", maxLength: 3));
            PlaceCaret(field, 6);

            // Act
            Press(field, '\b', KeyCode.Backspace);

            // Assert
            Assert.That(field.text, Is.EqualTo("abcde"));
        }

        // The refused second character is neither shown nor reported, and the accepted first is reported
        // as the value the field ended with.
        [Test]
        public void Given_AFieldWithRoomForOneCharacter_When_TheUserTypesTwo_Then_OnValueChangedReportsOnlyTheAcceptedText()
        {
            // Arrange
            var reported = new List<string>();
            var field = Mount(V.TextField(value: "ac", maxLength: 3, onValueChanged: reported.Add));
            PlaceCaret(field, 1);
            Type(field, 'b');

            // Act
            Type(field, 'x');

            // Assert
            Assert.That(string.Join("|", reported), Is.EqualTo("abc"));
        }

        // GREEN_ON_BASE(characterization): the base passes on the text of any change event the field hears.
        // It pins that an event from the inner text element, which is not an edit of the field's value, is
        // still reported as it carries it.
        [Test]
        public void Given_ALimitedField_When_TheInnerElementRaisesAChangeEvent_Then_OnValueChangedReceivesItsText()
        {
            // Arrange
            var reported = new List<string>();
            var field = Mount(V.TextField(maxLength: 3, onValueChanged: reported.Add));
            using var evt = ChangeEvent<string>.GetPooled(string.Empty, "xyzzy");

            // Act
            field.SimulateBubbledEvent(evt, Input(field));

            // Assert
            Assert.That(string.Join("|", reported), Is.EqualTo("xyzzy"));
        }

        [Test]
        public void Given_AFieldWithRoomForTwoCharacters_When_TheUserPastesFour_Then_OnValueChangedReportsTheTextWithOnlyTwoInserted()
        {
            // Arrange
            var reported = new List<string>();
            var field = Mount(V.TextField(value: "ad", maxLength: 4, onValueChanged: reported.Add));
            PlaceCaret(field, 1);

            // Act
            Paste(field, "bcXY");

            // Assert
            Assert.That(string.Join("|", reported), Is.EqualTo("abcd"));
        }

        [Test]
        public void Given_ADelayedFieldWithRoomForTwoCharacters_When_TheUserPastesFour_Then_TheEditHoldsTwoAndTheValueIsUntouched()
        {
            // Arrange
            var field = Mount(V.TextField(value: "ad", isDelayed: true, maxLength: 4));
            PlaceCaret(field, 1);

            // Act
            Paste(field, "bcXY");

            // Assert
            Assert.That((field.text, field.value), Is.EqualTo(("abcd", "ad")));
        }

        // GREEN_ON_BASE(characterization): the base unbinds a field's handler when the field returns to the pool.
        // It pins that the branch's handler for the value a limited field ended an edit with is unbound too.
        [Test]
        public void Given_ARecycledField_When_TheNextTenantTakesAnEdit_Then_TheLastTenantsOnValueChangedHearsNothing()
        {
            // Arrange
            var reported = new List<string>();
            var first = Mount(V.TextField(onValueChanged: reported.Add));
            Render();
            var second = Mount(V.TextField());
            PlaceCaret(second, 0);

            // Act
            Type(second, 'x');

            // Assert
            Assert.That((ReferenceEquals(second, first), string.Join("|", reported)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): the base resets a pooled field's limit to none.
        // It pins that the limit a tenant declared is not the next tenant's: the second tenant declares
        // nothing about the limit, so the record and the callback the first left are all there is to
        // refuse its typing. The instance is folded in, since a fresh field would pass without the pool.
        [Test]
        public void Given_ARecycledField_When_TheNextTenantDeclaresNoLimitAndTheUserTypesPastTheLastOne_Then_TheTextGrows()
        {
            // Arrange
            var first = Mount(V.TextField(maxLength: 3));
            Render();
            var second = Mount(V.TextField(value: "abc"));
            PlaceCaret(second, 3);

            // Act
            Type(second, 'd');

            // Assert
            Assert.That((ReferenceEquals(second, first), second.text), Is.EqualTo((true, "abcd")));
        }
    }
}
