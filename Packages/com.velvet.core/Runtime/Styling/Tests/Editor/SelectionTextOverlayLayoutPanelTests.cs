using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the selection drawing leaves the input sizing to its own text. Laid out on an editor
    /// window's panel: the headless panel SelectionTextOverlayTests uses gives the input no height to compare.
    /// </summary>
    internal sealed class SelectionTextOverlayLayoutPanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Component]
        private static VNode SingleLineHost() =>
            V.TextField(name: "field", value: "hello", className: "selection:text-white");

        [Component]
        private static VNode MultilineHost() =>
            V.TextField(name: "field", value: "hello\nworld", multiline: true, className: "selection:text-white");

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AField_When_TextIsFirstSelected_Then_TheInputKeepsTheHeightItsTextGaveIt(bool multiline)
        {
            // Arrange
            Func<VNode> host = multiline ? MultilineHost : SingleLineHost;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(host, key: "root"));
            var panel = _window.rootVisualElement.panel;
            ForcePanelUpdate(panel);
            var field = _window.rootVisualElement.Q<TextField>("field");
            var input = (TextElement)field.textEdition;
            var before = input.layout.height;

            // Act
            input.Focus();
            field.textSelection.SelectRange(1, 3);
            EditorPanelTestHelpers.DriveSchedulerOnce(panel);
            ForcePanelUpdate(panel);
            var drawing = field.Q<TextElement>(className: SelectionTextOverlay.ClassName);

            // Assert — the drawing is read beside the height, so a selection that drew nothing cannot pass.
            Assert.That(
                (drawing != null, before > 0f, input.layout.height),
                Is.EqualTo((true, true, before)),
                $"drawing={drawing != null} before={before} after={input.layout.height}");
        }
    }
}
