using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the key a line box keeps its one-line measurement under: the legacy font is part of it, so a
    /// measurement taken with that font is reused rather than taken again on every measure.
    /// </summary>
    internal sealed class TextLineBoxMeasureCacheTests : PanelTestBase
    {
        private static readonly FieldInfo s_legacyFont =
            typeof(TextLineBox).GetField("_legacyFont", BindingFlags.Instance | BindingFlags.NonPublic)!;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // GREEN_ON_BASE(characterization): the base keys a line-box measurement on the label's legacy font.
        [Test]
        public void Given_ALabelWithALegacyFont_When_ItsLinesAreMeasured_Then_TheMeasurementIsKeyedOnThatFont()
        {
            // Arrange
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "tag", className: "text-sm/6", text: "Tag"));
            var label = _window.rootVisualElement.Q<Label>("tag");
            label.style.unityFont = font;
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(label.panel);
            }

            // Act
            TextLineBox.HeightAt(label, "Tag", 200f);

            // Assert
            Assert.That(s_legacyFont.GetValue(((ILineBoxed)label).LineBox), Is.SameAs(font));
        }
    }
}
