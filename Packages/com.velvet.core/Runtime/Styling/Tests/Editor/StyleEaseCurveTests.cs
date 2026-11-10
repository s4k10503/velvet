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
        private static EaseFunction? Parse(string text)
            => StyleEaseCurve.TryParse(text.AsSpan(), out var curve) ? curve : null;

        [Test]
        public void Given_ACubicBezierText_When_Parsed_Then_ItCarriesTheFourCoordinates()
        {
            // Arrange
            const string text = "cubic-bezier(0.2, 0.8, 0.2, 1)";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((EaseFunction?)new BezierCurve(0.2f, 0.8f, 0.2f, 1f)));
        }

        [Test]
        public void Given_ACubicBezierWrittenAsAClassValue_When_Parsed_Then_UnderscoresReadAsSpaces()
        {
            // Arrange
            const string text = "cubic-bezier(0.2,_0.8,_0.2,_1)";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((EaseFunction?)new BezierCurve(0.2f, 0.8f, 0.2f, 1f)));
        }

        [Test]
        public void Given_ACssEasingKeyword_When_Parsed_Then_ItIsThatKeywordsCurve()
        {
            // Arrange
            const string text = "ease-in-out";

            // Act
            var curve = Parse(text);

            // Assert
            Assert.That(curve, Is.EqualTo((EaseFunction?)new BezierCurve(0.42f, 0f, 0.58f, 1f)));
        }

        [TestCase("steps(0)")]
        [TestCase("steps(1, jump-none)")]
        [TestCase("steps(4, sideways)")]
        [TestCase("steps(-2)")]
        [TestCase("cubic-bezier(1.2, 0, 0.2, 1)")]
        [TestCase("cubic-bezier(0.2, 0, -0.1, 1)")]
        [TestCase("cubic-bezier(0, 0, 1)")]
        [TestCase("cubic-bezier(0, 0, 1, 1, 1)")]
        [TestCase("cubic-bezier(0, 0, one, 1)")]
        [TestCase("cubic-bezier(NaN, 0, 0.2, 1)")]
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
        public void Given_StepsWithAJump_When_Parsed_Then_ItIsThatManyStepsJumpingThere()
        {
            // Arrange
            const string text = "steps(4, jump-both)";

            // Act
            var function = Parse(text);

            // Assert
            Assert.That(function, Is.EqualTo((EaseFunction?)EaseFunction.OfSteps(4, StepJump.Both)));
        }

        [TestCase(StepJump.End, 0.3f, 0.25f)]
        [TestCase(StepJump.Start, 0.3f, 0.5f)]
        [TestCase(StepJump.None, 0.3f, 1f / 3f)]
        [TestCase(StepJump.Both, 0.3f, 0.4f)]
        public void Given_FourSteps_When_EvaluatedPartWayThrough_Then_TheJumpPlacesTheStep(StepJump jump, float t, float expected)
        {
            // Arrange
            var steps = EaseFunction.OfSteps(4, jump);

            // Act
            var eased = StyleEaseCurve.Evaluate(steps, t);

            // Assert
            Assert.That(eased, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void Given_AStepsClass_When_Parsed_Then_TheLayerCarriesTheCountAndAnX2NoCurveHolds()
        {
            // Arrange
            const string className = "ease-[steps(3,_jump-start)]";

            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(className, out var layer);

            // Assert
            Assert.That(parsed ? layer.Value3 : float.NaN, Is.EqualTo(-1f));
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

        // GREEN_ON_BASE(characterization): the base forgets an ease curve once its class is removed.
        [Test]
        public void Given_AnElementWhoseEaseClassIsRemoved_When_ItsCurveIsAskedFor_Then_ItHasNone()
        {
            // Arrange
            var element = new VisualElement();
            StyleEaseCurve.NoteClasses(element, new[] { "ease-in" });
            StyleEaseCurve.NoteClasses(element, Array.Empty<string>());

            // Act
            var curve = StyleEaseCurve.CurveFor(element, EasingMode.EaseIn);

            // Assert
            Assert.That(curve, Is.Null);
        }
    }
}
