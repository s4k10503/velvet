using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a <see cref="KeyDownBinding"/> on a text-input element reaches the keys its input takes.
    /// The input stops the propagation of each key it takes, so a binding the field sees only after the input
    /// never runs for one; Enter in a single-line field is such a key.
    /// </summary>
    internal sealed class TextInputKeyBindingTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static int s_keyDowns;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_keyDowns = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode MotionTextFieldHost() => V.Motion(
            name: "field",
            elementType: typeof(TextField),
            events: new FiberEventBinding[] { new KeyDownBinding { Handler = _ => s_keyDowns++ } });

        [Test]
        public void Given_AKeyDownBindingOnAMotionTextField_When_EnterIsPressedInItsInput_Then_ItRuns()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Component((Func<VNode>)MotionTextFieldHost, key: "root"));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            var input = (TextElement)_host.Root.Q<TextField>("field").textEdition;
            input.Focus();

            // Act
            using (var enter = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None))
            {
                enter.target = input;
                input.SendEvent(enter);
            }

            // Assert
            Assert.That(s_keyDowns, Is.EqualTo(1));
        }
    }
}
