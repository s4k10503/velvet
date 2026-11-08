using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that LineBoxFont's copy of a font lays lines out one line height apart under either of UI Toolkit's
    /// text generators, the first line included.
    /// </summary>
    internal sealed class LineBoxFontTests : PanelTestBase
    {
        private Label Laid(TextGeneratorType generator)
        {
            var label = new Label("M");
            label.style.fontSize = 16f;
            label.style.unityTextGenerator = generator;
            _window.rootVisualElement.Add(label);
            ForcePanelUpdate(label.panel);
            var variant = LineBoxFont.For(StyleFontUnits.AssetOf(label)!, 24f, 16f);
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(variant));
            ForcePanelUpdate(label.panel);
            return label;
        }

        private static float HeightOf(TextElement element, string text)
            => element.MeasureTextSize(text, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN,
                VisualElement.MeasureMode.Undefined).y;

        [TestCase(TextGeneratorType.Standard)]
        [TestCase(TextGeneratorType.Advanced)]
        public void Given_ACopyAtTwentyFourPixels_When_OneLineIsMeasured_Then_ItIsTwentyFourPixelsTall(TextGeneratorType generator)
        {
            // Act
            var label = Laid(generator);

            // Assert
            Assert.That(HeightOf(label, "M"), Is.EqualTo(24f).Within(0.01f));
        }

        [TestCase(TextGeneratorType.Standard)]
        [TestCase(TextGeneratorType.Advanced)]
        public void Given_ACopyAtTwentyFourPixels_When_ThreeLinesAreMeasured_Then_TheyAreSeventyTwoPixelsTall(TextGeneratorType generator)
        {
            // Act
            var label = Laid(generator);

            // Assert
            Assert.That(HeightOf(label, "M\nM\nM"), Is.EqualTo(72f).Within(0.01f));
        }
    }
}
