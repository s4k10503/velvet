using System.Globalization;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the keyword layer StyleCustomProperties writes for an unresolvable var() draws in a family
    /// Velvet composes from several longhands or several layers.
    /// </summary>
    internal sealed class CustomPropertyUnsetLayerTests
    {
        [Test]
        public void Given_AKeywordFlexLayer_When_Applied_Then_TheShrinkTakesTheKeyword()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.Flex, StyleKeyword.Initial));

            // Assert
            Assert.That(element.style.flexShrink.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_AKeywordDropShadowColourOverAColour_When_Composed_Then_TheShadowTakesItsOwnColour()
        {
            // Arrange — the colour variable left without a value hands the shadow its own fallback colour, over the
            // colour a lower rule gives the variable.
            var element = new VisualElement();
            foreach (var token in new[] { "drop-shadow-[#ff0000]", "drop-shadow-[0_2px_4px_#0000ff]" })
            {
                StyleArbitraryValueResolver.TryParse(token, out var layer);
                StyleArbitraryValueResolver.Apply(element, in layer, StyleLayerPriority.Base);
            }

            // Act
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterDropShadowColor, StyleKeyword.Initial),
                StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));

            // Assert
            var colour = element.style.filter.value[0].GetParameter(3).colorValue;
            Assert.That(string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##},{3:0.##}",
                colour.r, colour.g, colour.b, colour.a), Is.EqualTo("0,0,1,1"));
        }
    }
}
