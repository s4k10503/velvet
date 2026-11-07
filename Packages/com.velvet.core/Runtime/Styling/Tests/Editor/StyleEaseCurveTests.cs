using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the curve an <c>ease-[…]</c> class names and the keyword the element is left easing by where the
    /// curve itself cannot be applied.
    /// </summary>
    internal sealed class StyleEaseCurveTests
    {
        private static BezierCurve? Parse(string text)
            => StyleEaseCurve.TryParse(text.AsSpan(), out var curve) ? curve : null;

        [Test]
        public void Given_ACubicBezierText_When_Parsed_Then_ItCarriesTheFourCoordinates()
        {
            // Arrange
            const string text = "cubic-bezier(0.2, 0.8, 0.2, 1)";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((BezierCurve?)new BezierCurve(0.2f, 0.8f, 0.2f, 1f)));
        }

        [Test]
        public void Given_ACubicBezierWrittenAsAClassValue_When_Parsed_Then_UnderscoresReadAsSpaces()
        {
            // Arrange
            const string text = "cubic-bezier(0.2,_0.8,_0.2,_1)";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((BezierCurve?)new BezierCurve(0.2f, 0.8f, 0.2f, 1f)));
        }

        [Test]
        public void Given_ACssEasingKeyword_When_Parsed_Then_ItIsThatKeywordsCurve()
        {
            // Arrange
            const string text = "ease-in-out";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((BezierCurve?)new BezierCurve(0.42f, 0f, 0.58f, 1f)));
        }

        [TestCase("cubic-bezier(1.2, 0, 0.2, 1)")]
        [TestCase("cubic-bezier(0.2, 0, -0.1, 1)")]
        [TestCase("cubic-bezier(0, 0, 1)")]
        [TestCase("cubic-bezier(0, 0, 1, 1, 1)")]
        [TestCase("cubic-bezier(0, 0, one, 1)")]
        [TestCase("cubic-bezier(NaN, 0, 0.2, 1)")]
        [TestCase("steps(4)")]
        [TestCase("")]
        public void Given_AValueCssRejects_When_Parsed_Then_ThereIsNoCurve(string text)
        {
            // Arrange

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.Null);
        }

        [Test]
        public void Given_AnEaseClassNamingACurve_When_Parsed_Then_ItIsTheTimingFunctionLayer()
        {
            // Arrange
            const string className = "ease-[cubic-bezier(0.2,0.8,0.2,1)]";

            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out var layer);

            // Assert
            Assert.That(parsed ? layer.Property : (ArbitraryProperty?)null,
                Is.EqualTo((ArbitraryProperty?)ArbitraryProperty.TransitionTimingFunction));
        }

        [Test]
        public void Given_AnEaseClassNamingACurve_When_Parsed_Then_TheLayerCarriesItsSecondCoordinate()
        {
            // Arrange
            const string className = "ease-[cubic-bezier(0.2,0.8,0.2,1)]";

            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out var layer);

            // Assert
            Assert.That(parsed ? layer.Value2 : float.NaN, Is.EqualTo(0.8f));
        }

        [Test]
        public void Given_AnEaseClassNamingNoCurve_When_Parsed_Then_ItIsNoLayer()
        {
            // Arrange
            const string className = "ease-[wobbly]";

            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        [Test]
        public void Given_TheLinearCurve_When_TheNearestKeywordIsAsked_Then_ItIsLinear()
        {
            // Arrange
            var linear = new BezierCurve(0f, 0f, 1f, 1f);

            // Act
            var mode = StyleEaseCurve.NearestMode(linear);

            // Assert
            Assert.That(mode, Is.EqualTo(EasingMode.Linear));
        }

        [Test]
        public void Given_AnEaseLayer_When_ItIsApplied_Then_TheElementEasesByTheKeywordNearestItsCurve()
        {
            // Arrange
            var element = new VisualElement();
            var layer = new ArbitraryStyle(ArbitraryProperty.TransitionTimingFunction, 0f, 0f, 1f, 1f);

            // Act
            StyleArbitraryValueResolver.Apply(element, layer);

            // Assert
            Assert.That(element.style.transitionTimingFunction.value.Select(function => function.mode).ToArray(),
                Is.EqualTo(new[] { EasingMode.Linear }));
        }

        [Test]
        public void Given_AnAppliedEaseLayer_When_ItIsCleared_Then_TheElementKeepsNoInlineTimingFunction()
        {
            // Arrange
            var element = new VisualElement();
            var layer = new ArbitraryStyle(ArbitraryProperty.TransitionTimingFunction, 0f, 0f, 1f, 1f);
            StyleArbitraryValueResolver.Apply(element, layer);

            // Act
            StyleArbitraryValueResolver.Clear(element, in layer);

            // Assert
            Assert.That(element.style.transitionTimingFunction.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
