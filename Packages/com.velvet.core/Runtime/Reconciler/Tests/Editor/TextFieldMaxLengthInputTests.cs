using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>maxLength:</c> limits what a key or a paste may insert at the caret. A value a render sets, and an
    /// edit a delayed field has not committed, are left as they are.
    /// </summary>
    internal sealed class TextFieldMaxLengthInputTests
    {
        private HeadlessEditorPanelHost _host;
        private Reconciler _reconciler;
        private VisualElement _root;
        private string _clipboard;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _reconciler = new Reconciler();
            _root = new VisualElement();
            _host.Root.Add(_root);
            _clipboard = GUIUtility.systemCopyBuffer;
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.systemCopyBuffer = _clipboard;
            _reconciler?.Dispose();
            _reconciler = null;
            _root = null;
            _host?.Dispose();
            _host = null;
        }

        [Test]
        public void Given_AValueLongerThanMaxLength_When_TheFieldIsReconciled_Then_TheValueIsShownInFull()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "hello", maxLength: 3) };

            // Act
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)_root.ElementAt(0)).text, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_ADelayedFieldWhoseValueExceedsMaxLength_When_ItLosesFocus_Then_TheValueStaysWhole()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.TextField(value: "hello", maxLength: 3, isDelayed: true),
                V.TextField(name: "other"),
            };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();

            // Act
            Input((TextField)_root.ElementAt(1)).Focus();

            // Assert
            Assert.That(field.value, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_AnEditADelayedFieldHasNotCommitted_When_ALaterRenderNarrowsMaxLength_Then_TheEditStaysWhole()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), oldTree);
            var field = (TextField)_root.ElementAt(0);
            Input(field).text = "typed";

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(field.text, Is.EqualTo("typed"));
        }

        [Test]
        public void Given_AFieldShowingAsManyCharactersAsItsLimit_When_ACharacterIsTypedInTheMiddle_Then_TheTextIsUnchanged()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "abcd", maxLength: 4) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(1, 1);

            // Act
            SendKey(field, 'X', KeyCode.X, EventModifiers.None);

            // Assert
            Assert.That(field.text, Is.EqualTo("abcd"));
        }

        [Test]
        public void Given_RoomForTwoCharactersAtTheCaret_When_APasteStartsWithACarriageReturn_Then_OnlyThoseTwoAreInserted()
        {
            // Arrange — a single-line field turns a carriage return and line feed into one space, so the two
            // characters that fit are that space and the W after it.
            var tree = new VNode[] { V.TextField(value: "abcd", maxLength: 6) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(2, 2);
            GUIUtility.systemCopyBuffer = "\r\nWXYZ";

            // Act
            SendKey(field, 'v', KeyCode.V, PasteModifiers);

            // Assert
            Assert.That(field.text, Is.EqualTo("ab Wcd"));
        }

        [Test]
        public void Given_RoomForOneCharacterAtTheCaret_When_APasteIsASurrogatePair_Then_NothingIsInserted()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "ab", maxLength: 3) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(2, 2);
            GUIUtility.systemCopyBuffer = "\uD83D\uDE00";

            // Act
            SendKey(field, 'v', KeyCode.V, PasteModifiers);

            // Assert
            Assert.That(field.text, Is.EqualTo("ab"));
        }

        [Test]
        public void Given_RoomForTwoCharactersAtTheCaret_When_ThePasteArrivesAsACommand_Then_OnlyThoseTwoAreInserted()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "abcd", maxLength: 6) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(2, 2);
            GUIUtility.systemCopyBuffer = "WXYZ";

            // Act
            SendPaste(field);

            // Assert
            Assert.That(field.text, Is.EqualTo("abWXcd"));
        }

        // GREEN_ON_BASE(characterization): a paste reads the clipboard and writes nothing back, so the buffer afterwards is the string the paste was given.
        [Test]
        public void Given_APasteThatDoesNotFit_When_TheEngineHasReadIt_Then_TheClipboardIsTheOriginalString()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "abcd", maxLength: 6) };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), tree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(2, 2);
            GUIUtility.systemCopyBuffer = "\r\nWXYZ";

            // Act
            SendKey(field, 'v', KeyCode.V, PasteModifiers);

            // Assert
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("\r\nWXYZ"));
        }

        // GREEN_ON_BASE(characterization): dropping the limit leaves a normal field unlimited, so a character past the old bound is inserted.
        [Test]
        public void Given_ALimitThatWasDeclared_When_ALaterRenderDropsItAndACharacterIsTyped_Then_TheCharacterIsInserted()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "ab", maxLength: 2) };
            var newTree = new VNode[] { V.TextField(value: "ab") };
            _reconciler.Reconcile(_root, System.Array.Empty<VNode>(), oldTree);
            _reconciler.Reconcile(_root, oldTree, newTree);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var field = (TextField)_root.ElementAt(0);
            Input(field).Focus();
            field.SelectRange(2, 2);

            // Act
            SendKey(field, 'X', KeyCode.X, EventModifiers.None);

            // Assert
            Assert.That(field.text, Is.EqualTo("abX"));
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        private static EventModifiers PasteModifiers =>
            SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX
                ? EventModifiers.Command
                : EventModifiers.Control;

        private static void SendKey(TextField field, char character, KeyCode keyCode, EventModifiers modifiers)
        {
            var input = Input(field);
            using var key = KeyDownEvent.GetPooled(character, keyCode, modifiers);
            key.target = input;
            input.SendEvent(key);
        }

        private static void SendPaste(TextField field)
        {
            var input = Input(field);
            using var command = CommandEventBase<ExecuteCommandEvent>.GetPooled("Paste");
            command.target = input;
            input.SendEvent(command);
        }
    }
}
