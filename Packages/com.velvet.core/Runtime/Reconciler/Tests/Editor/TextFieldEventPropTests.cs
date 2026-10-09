using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.TextField</c>'s <c>onSubmit:</c>, <c>onKeyDown:</c>, <c>onKeyUp:</c>, <c>onFocus:</c>,
    /// <c>onBlur:</c> and <c>onCreated:</c> against the engine's own handling of keys and focus inside the
    /// field: keys land on the text element inside it, and Enter hands focus from that element to the field.
    /// A delayed field holding an edit is the arrangement wherever a case needs to tell a reading taken
    /// before the field handled a key from one taken after it, since the Enter is what commits that edit.
    /// </summary>
    internal sealed class TextFieldEventPropTests
    {
        private const string NotCalled = "<not called>";

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static List<string> s_log;
        private static string s_valueAtKeyDown;
        private static int s_count;
        private static VisualElement s_created;
        private static StateUpdater<bool> s_setUseSecond;
        private static Action<string> s_shared;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_log = new List<string>();
            s_valueAtKeyDown = NotCalled;
            s_count = 0;
            s_created = null;
            s_setUseSecond = default;
            s_shared = value => s_log.Add(value);
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

        // The same forced style pass as TextFieldTouchKeyboardReassertTests' Mount, and for the same reason.
        private void Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        private static void TypeWithoutCommitting(TextField field, string text) => Input(field).text = text;

        private static void PressKey(TextField field, char character, KeyCode keyCode, EventModifiers modifiers)
        {
            Input(field).Focus();
            SendKey(field, character, keyCode, modifiers);
        }

        private static void SendKey(TextField field, char character, KeyCode keyCode, EventModifiers modifiers)
        {
            var input = Input(field);
            using var key = KeyDownEvent.GetPooled(character, keyCode, modifiers);
            key.target = input;
            input.SendEvent(key);
        }

        // The input's own record of an open IME composition, which an editor test has no OS composition to set.
        private static void SetComposing(TextField field, bool composing)
        {
            var manipulator = EngineMember.TextEditingManipulator.ResolveProperty()!.GetValue(Input(field));
            var utilities = EngineMember.TextEditingUtilities.ResolveField()!.GetValue(manipulator);
            EngineMember.TextCompositionActive.ResolveField()!.SetValue(utilities, composing);
        }

        private static void PressEnter(TextField field) => PressKey(field, '\n', KeyCode.Return, EventModifiers.None);

        [Component]
        private static VNode DelayedSubmitHost() =>
            V.TextField(name: "field", isDelayed: true, onSubmit: value => s_log.Add(value));

        [Component]
        private static VNode ReadOnlySubmitHost() =>
            V.TextField(name: "field", value: "kept", isReadOnly: true, onSubmit: value => s_log.Add(value));

        [Component]
        private static VNode SubmitAndOtherHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "field", onSubmit: value => s_log.Add("submit")),
            V.TextField(name: "other"),
        });

        [Component]
        private static VNode SingleAndMultilineSubmitHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "single", onSubmit: _ => s_log.Add("single")),
            V.TextField(name: "multi", multiline: true, onSubmit: _ => s_log.Add("multi")),
        });

        [Component]
        private static VNode KeyDownReadingHost() =>
            V.TextField(name: "field", isDelayed: true,
                onKeyDown: evt => s_valueAtKeyDown = ((TextField)evt.currentTarget).value);

        [Component]
        private static VNode KeyDownStoppingHost() =>
            V.TextField(name: "field", isDelayed: true, onKeyDown: evt =>
            {
                s_count++;
                evt.StopPropagation();
            });

        [Component]
        private static VNode KeyUpHost() => V.TextField(name: "field", onKeyUp: _ => s_count++);

        [Component]
        private static VNode BlurHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "field", onBlur: _ => s_count++),
            V.TextField(name: "other"),
        });

        [Component]
        private static VNode FocusHost() => V.TextField(name: "field", onFocus: _ => s_count++);

        [Component]
        private static VNode SharedDelegateHost() =>
            V.TextField(name: "field", value: string.Empty, onValueChanged: s_shared, onSubmit: s_shared);

        [Component]
        private static VNode CreatedHost() => V.TextField(name: "field", onCreated: element => s_created = element);

        [Component]
        private static VNode ReplacingSubmitHost()
        {
            var (useSecond, setUseSecond) = Hooks.UseState(false);
            s_setUseSecond = setUseSecond;
            var onSubmit = useSecond ? (Action<string>)(_ => s_log.Add("second")) : _ => s_log.Add("first");
            return V.TextField(name: "field", onSubmit: onSubmit);
        }

        [Component]
        private static VNode ReplacingKeyDownHost()
        {
            var (useSecond, setUseSecond) = Hooks.UseState(false);
            s_setUseSecond = setUseSecond;
            var onKeyDown = useSecond
                ? (EventCallback<KeyDownEvent>)(_ => s_log.Add("second"))
                : _ => s_log.Add("first");
            return V.TextField(name: "field", onKeyDown: onKeyDown);
        }

        [Component]
        private static VNode ReplacingFocusHost()
        {
            var (useSecond, setUseSecond) = Hooks.UseState(false);
            s_setUseSecond = setUseSecond;
            var onFocus = useSecond
                ? (EventCallback<FocusInEvent>)(_ => s_log.Add("second"))
                : _ => s_log.Add("first");
            return V.TextField(name: "field", onFocus: onFocus);
        }

        [Component]
        private static VNode ReplacingBlurHost()
        {
            var (useSecond, setUseSecond) = Hooks.UseState(false);
            s_setUseSecond = setUseSecond;
            var onBlur = useSecond
                ? (EventCallback<FocusOutEvent>)(_ => s_log.Add("second"))
                : _ => s_log.Add("first");
            return V.Div(children: new VNode[]
            {
                V.TextField(name: "field", onBlur: onBlur),
                V.TextField(name: "other"),
            });
        }

        private void SwitchToTheSecondHandler()
        {
            s_setUseSecond.Invoke(true);
            _mounted.FlushStateForTest();
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterIsPressed_Then_OnSubmitReceivesTheTextTheEnterCommitted()
        {
            // Arrange
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");

            // Act
            PressEnter(field);

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("typed"));
        }

        [Test]
        public void Given_AReadOnlyField_When_EnterIsPressed_Then_OnSubmitReceivesItsValue()
        {
            // Arrange — the engine runs no editor on a read-only field, so its Enter commits nothing, and a
            // browser still submits a form from a readonly input on Enter.
            Mount(ReadOnlySubmitHost);

            // Act
            PressEnter(Q("field"));

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("kept"));
        }

        [Test]
        public void Given_AnOpenImeComposition_When_EnterIsPressed_Then_OnSubmitWaitsForTheNextEnter()
        {
            // Arrange
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");
            Input(field).Focus();
            SetComposing(field, true);

            // Act
            SendKey(field, '\n', KeyCode.Return, EventModifiers.None);
            var afterComposingEnter = s_log.Count;
            Input(field).Focus();
            SetComposing(field, false);
            SendKey(field, '\n', KeyCode.Return, EventModifiers.None);

            // Assert — the second Enter is the control showing the binding was live.
            Assert.That((afterComposingEnter, string.Join("|", s_log)), Is.EqualTo((0, "typed")));
        }

        [Test]
        public void Given_AnOnSubmit_When_FocusLeavesTheFieldWithNoSoftKeyboard_Then_ItWaitsForEnter()
        {
            // Arrange
            Mount(SubmitAndOtherHost);
            var field = Q("field");
            Input(field).Focus();

            // Act
            Input(Q("other")).Focus();
            var afterBlur = s_log.Count;
            PressEnter(field);

            // Assert
            Assert.That((afterBlur, s_log.Count), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_ASingleLineAndAMultilineField_When_ShiftEnterIsPressedInEach_Then_OnlyTheSingleLineFieldSubmits()
        {
            // Arrange — Shift+Enter is the key a multi-line field commits on, so it is the one that tells
            // the multi-line gate apart from following the field's own commit.
            Mount(SingleAndMultilineSubmitHost);

            // Act
            PressKey(Q("single"), '\n', KeyCode.Return, EventModifiers.Shift);
            PressKey(Q("multi"), '\n', KeyCode.Return, EventModifiers.Shift);

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("single"));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterIsPressedWithTheCommandModifierAndThenAlone_Then_OnlyThePlainEnterCommitsAndSubmits()
        {
            // Arrange — Control and Command together, so the modifier is the command one on every platform.
            Mount(DelayedSubmitHost);
            var field = Q("field");
            var before = field.value;
            TypeWithoutCommitting(field, "typed");

            // Act
            PressKey(field, '\n', KeyCode.Return, EventModifiers.Control | EventModifiers.Command);
            var afterCommandEnter = (field.value == before, s_log.Count);
            PressEnter(field);

            // Assert — the plain Enter is the control showing the field and the binding were live.
            Assert.That(
                (afterCommandEnter, field.value, string.Join("|", s_log)),
                Is.EqualTo(((true, 0), "typed", "typed")));
        }

        [TestCase(EventModifiers.Control)]
        [TestCase(EventModifiers.Command)]
        public void Given_AnOnSubmit_When_EnterIsPressedWithOneModifierAndThenAlone_Then_OnlyThePlainEnterSubmits(
            EventModifiers modifier)
        {
            // Arrange — each modifier alone, since only one of the two is the command modifier on a given
            // platform, and a gate reading that modifier passes the other.
            Mount(DelayedSubmitHost);
            var field = Q("field");

            // Act
            PressKey(field, '\n', KeyCode.Return, modifier);
            var afterModifiedEnter = s_log.Count;
            PressEnter(field);

            // Assert — the plain Enter is the control showing the binding was live.
            Assert.That((afterModifiedEnter, s_log.Count), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AFieldThatSubmitted_When_EnterIsPressedAgainOnTheFieldHoldingFocus_Then_OnSubmitRunsAgain()
        {
            // Arrange
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");
            PressEnter(field);

            // Act — the field itself holds focus after an Enter, so the next key lands on it.
            using (var key = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None))
            {
                key.target = field;
                field.SendEvent(key);
            }

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("typed|typed"));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterArrivesAsACarriageReturn_Then_OnSubmitReceivesTheCommittedText()
        {
            // Arrange
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");

            // Act
            PressKey(field, '\r', KeyCode.Return, EventModifiers.None);

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("typed"));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterIsPressedWithTheCommandModifierAndAlt_Then_TheFieldCommitsAndSubmits()
        {
            // Arrange — Control and Command together, as in the command-modifier case, with Alt beside them.
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");

            // Act
            PressKey(field, '\n', KeyCode.Return, EventModifiers.Control | EventModifiers.Command | EventModifiers.Alt);

            // Assert
            Assert.That((field.value, string.Join("|", s_log)), Is.EqualTo(("typed", "typed")));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterIsPressedWithAlt_Then_TheFieldCommitsAndSubmits()
        {
            // Arrange
            Mount(DelayedSubmitHost);
            var field = Q("field");
            TypeWithoutCommitting(field, "typed");

            // Act
            PressKey(field, '\n', KeyCode.Return, EventModifiers.Alt);

            // Assert
            Assert.That((field.value, string.Join("|", s_log)), Is.EqualTo(("typed", "typed")));
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_EnterIsPressed_Then_OnKeyDownReadsTheValueTheFieldHadNotYetCommitted()
        {
            // Arrange
            Mount(KeyDownReadingHost);
            var field = Q("field");
            var before = field.value;
            TypeWithoutCommitting(field, "typed");

            // Act
            PressEnter(field);

            // Assert — a handler that never ran leaves NotCalled, and one run after the field took the key
            // reads "typed".
            Assert.That(s_valueAtKeyDown, Is.EqualTo(before));
        }

        [Test]
        public void Given_AnOnKeyDownStoppingPropagation_When_EnterIsPressedInADelayedFieldHoldingAnEdit_Then_TheFieldDoesNotCommit()
        {
            // Arrange
            Mount(KeyDownStoppingHost);
            var field = Q("field");
            var before = field.value;
            TypeWithoutCommitting(field, "typed");

            // Act
            PressEnter(field);

            // Assert — the count is what rules out an Enter that reached nothing.
            Assert.That((s_count, field.value == before), Is.EqualTo((1, true)));
        }

        [Test]
        public void Given_AnOnKeyUp_When_AKeyIsReleasedInTheField_Then_ItRuns()
        {
            // Arrange
            Mount(KeyUpHost);
            var input = Input(Q("field"));
            input.Focus();

            // Act
            using (var key = KeyUpEvent.GetPooled('\0', KeyCode.A, EventModifiers.None))
            {
                key.target = input;
                input.SendEvent(key);
            }

            // Assert
            Assert.That(s_count, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnOnBlur_When_EnterHandsFocusToTheFieldAndFocusThenLeavesIt_Then_OnlyTheLeavingIsReported()
        {
            // Arrange
            Mount(BlurHost);
            var field = Q("field");
            Input(field).Focus();

            // Act
            PressEnter(field);
            var afterEnter = s_count;
            Input(Q("other")).Focus();

            // Assert
            Assert.That((afterEnter, s_count), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AnOnFocus_When_FocusEntersAndComesBackToTheInputAfterEnter_Then_OnlyTheEntryIsReported()
        {
            // Arrange
            Mount(FocusHost);
            var field = Q("field");

            // Act
            Input(field).Focus();
            var afterEntry = s_count;
            PressEnter(field);
            Input(field).Focus();

            // Assert
            Assert.That((afterEntry, s_count), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_OneDelegateForBothOnValueChangedAndOnSubmit_When_EnterIsPressedWithNothingTyped_Then_OnSubmitRuns()
        {
            // Arrange — a declared empty value and nothing typed, so the Enter reports no value change and the
            // one call is the submit.
            Mount(SharedDelegateHost);

            // Act
            PressEnter(Q("field"));

            // Assert
            Assert.That(s_log.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnOnCreated_When_TheFieldMounts_Then_ItReceivesTheField()
        {
            // Arrange
            Mount(CreatedHost);

            // Act
            var field = Q("field");

            // Assert
            Assert.That(ReferenceEquals(s_created, field) && field != null, Is.True);
        }

        [Test]
        public void Given_ARenderReplacingOnSubmit_When_EnterIsPressed_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            Mount(ReplacingSubmitHost);
            SwitchToTheSecondHandler();

            // Act
            PressEnter(Q("field"));

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("second"));
        }

        [Test]
        public void Given_ARenderReplacingOnKeyDown_When_AKeyIsPressed_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            Mount(ReplacingKeyDownHost);
            SwitchToTheSecondHandler();

            // Act
            PressEnter(Q("field"));

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("second"));
        }

        [Test]
        public void Given_ARenderReplacingOnFocus_When_FocusEnters_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            Mount(ReplacingFocusHost);
            SwitchToTheSecondHandler();

            // Act
            Input(Q("field")).Focus();

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("second"));
        }

        [Test]
        public void Given_ARenderReplacingOnBlur_When_FocusLeaves_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            Mount(ReplacingBlurHost);
            SwitchToTheSecondHandler();
            Input(Q("field")).Focus();

            // Act
            Input(Q("other")).Focus();

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("second"));
        }
    }
}
