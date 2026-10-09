using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.TextField</c>'s <c>onSubmit:</c> for the soft keyboard. The engine closes a keyboard the
    /// user dismissed and blurs the input without sending a key, so what decides a submit is the status the
    /// keyboard held at focus-in reports once focus leaves. An editor opens no soft keyboard, so each case
    /// stands one in through <c>SoftKeyboard</c>'s delegates and reports the status the case names.
    /// </summary>
    internal sealed class SoftKeyboardSubmitTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private Delegate _savedOf;
        private Delegate _savedStatusOf;

        private static List<string> s_log;
        private static StateUpdater<bool> s_setUseSecond;

        private static readonly FieldInfo s_of =
            typeof(SoftKeyboard).GetField("s_of", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo s_statusOf =
            typeof(SoftKeyboard).GetField("s_statusOf", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_log = new List<string>();
            s_setUseSecond = default;
            _savedOf = (Delegate)s_of.GetValue(null);
            _savedStatusOf = (Delegate)s_statusOf.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            s_of.SetValue(null, _savedOf);
            s_statusOf.SetValue(null, _savedStatusOf);
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // A keyboard object that never reaches native code: its status comes from the delegate set beside it.
        private static void StandInKeyboard(TouchScreenKeyboard.Status status)
        {
            var keyboard = (TouchScreenKeyboard)FormatterServices.GetUninitializedObject(typeof(TouchScreenKeyboard));
            s_of.SetValue(null, (Func<TextField, TouchScreenKeyboard>)(_ => keyboard));
            s_statusOf.SetValue(null, (Func<TouchScreenKeyboard, TouchScreenKeyboard.Status>)(_ => status));
        }

        private TextField Q(string name) => _host.Root.Q<TextField>(name);

        private void Mount(Func<VNode> body)
        {
            _mounted = V.Mount(_host.Root, V.Component(body, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        // Focus into the field's input, then out of it to the other field, as a dismissed keyboard leaves it.
        private void FocusInAndOut()
        {
            Input(Q("field")).Focus();
            Input(Q("other")).Focus();
        }

        [Component]
        private static VNode DelayedHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "field", isDelayed: true, onSubmit: value => s_log.Add(value)),
            V.TextField(name: "other"),
        });

        [Component]
        private static VNode MultilineHost() => V.Div(children: new VNode[]
        {
            V.TextField(name: "field", multiline: true, onSubmit: value => s_log.Add("multi")),
            V.TextField(name: "other"),
        });

        [Component]
        private static VNode ReplacingHost()
        {
            var (useSecond, setUseSecond) = Hooks.UseState(false);
            s_setUseSecond = setUseSecond;
            var onSubmit = useSecond ? (Action<string>)(_ => s_log.Add("second")) : _ => s_log.Add("first");
            return V.Div(children: new VNode[]
            {
                V.TextField(name: "field", onSubmit: onSubmit),
                V.TextField(name: "other"),
            });
        }

        [Test]
        public void Given_ADelayedFieldHoldingAnEdit_When_TheSoftKeyboardClosesWithDone_Then_OnSubmitReceivesTheCommittedText()
        {
            // Arrange
            StandInKeyboard(TouchScreenKeyboard.Status.Done);
            Mount(DelayedHost);
            ((TextElement)Q("field").textEdition).text = "typed";

            // Act
            FocusInAndOut();

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("typed"));
        }

        [TestCase(TouchScreenKeyboard.Status.Canceled)]
        [TestCase(TouchScreenKeyboard.Status.LostFocus)]
        [TestCase(TouchScreenKeyboard.Status.Visible)]
        public void Given_ASoftKeyboardThatDidNotFinish_When_FocusLeaves_Then_OnSubmitIsNotCalled(
            TouchScreenKeyboard.Status status)
        {
            // Arrange
            StandInKeyboard(status);
            Mount(DelayedHost);

            // Act
            FocusInAndOut();
            var unfinished = s_log.Count;
            StandInKeyboard(TouchScreenKeyboard.Status.Done);
            FocusInAndOut();

            // Assert — the Done round is the control showing the binding was live.
            Assert.That((unfinished, s_log.Count), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AMultilineField_When_TheSoftKeyboardClosesWithDone_Then_OnSubmitIsNotCalled()
        {
            // Arrange
            StandInKeyboard(TouchScreenKeyboard.Status.Done);
            Mount(MultilineHost);

            // Act
            FocusInAndOut();

            // Assert
            Assert.That(s_log.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ARenderReplacingOnSubmit_When_TheSoftKeyboardClosesWithDone_Then_OnlyTheNewHandlerRuns()
        {
            // Arrange
            StandInKeyboard(TouchScreenKeyboard.Status.Done);
            Mount(ReplacingHost);
            s_setUseSecond.Invoke(true);
            _mounted.FlushStateForTest();

            // Act
            FocusInAndOut();

            // Assert
            Assert.That(string.Join("|", s_log), Is.EqualTo("second"));
        }
    }
}
