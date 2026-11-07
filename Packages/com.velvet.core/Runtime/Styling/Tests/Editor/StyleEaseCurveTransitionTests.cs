using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a transition the engine runs eases along the curve an element holds, with the duration the
    /// element's transition lists give it. The first case is the control: it fails when the harness stops driving the
    /// engine's transitions, which would leave every other case measuring nothing.
    /// </summary>
    internal sealed class StyleEaseCurveTransitionTests
    {
        private static readonly BezierCurve s_curve = new(0.9f, 0f, 0.1f, 1f);

        private const string Sheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/EaseTransitionTestSheet.uss";

        // The fraction of its way a width has covered a quarter of a second into a second-long transition from 0 to
        // 100, on an element whose transition list names inlineMode and which hold arranges.
        private static float QuarterSecondIn(EasingMode inlineMode, Action<VisualElement> hold)
            => QuarterSecondIn(host => { }, element =>
            {
                element.style.transitionProperty = new List<StylePropertyName> { new("width") };
                element.style.transitionDuration = new List<TimeValue> { new(1f) };
                element.style.transitionTimingFunction = new List<EasingFunction> { new(inlineMode) };
                hold(element);
            });

        // The same on an element whose rules, from the class given, resolve the keyword and which write no inline list.
        private static float QuarterSecondByRule(string ruleClass, Action<VisualElement> hold)
            => QuarterSecondIn(host => host.Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet)), element =>
            {
                element.AddToClassList(ruleClass);
                hold(element);
            });

        private static float QuarterSecondIn(Action<HeadlessEditorPanelHost> prepare, Action<VisualElement> arrange)
        {
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            prepare(host);
            var element = new VisualElement();
            host.Root.Add(element);
            element.style.width = 0f;
            arrange(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);

            element.style.width = 100f;
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            return element.resolvedStyle.width / 100f;
        }

        [Test]
        public void Given_NoCurve_When_AWidthTransitionRunsALinearQuarterSecond_Then_ItIsAQuarterOfTheWay()
        {
            // Arrange

            // Act
            var fraction = QuarterSecondIn(EasingMode.Linear, _ => { });

            // Assert
            Assert.That(fraction, Is.EqualTo(0.25f).Within(0.01f));
        }

        [Test]
        public void Given_AnEaseLayerOnTheElement_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(s_curve, 0.25f);

            // Act
            var fraction = QuarterSecondIn(EasingMode.Linear, element => StyleEaseCurve.SetLayer(element, s_curve, EasingMode.Linear));

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_ATransitionUtilityWhoseRulesResolveEaseInOut_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsDefaultCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-out-test",
                element => StyleEaseCurve.NoteClasses(element, new[] { "transition-colors" }));

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AVariantEaseInClassWhoseRuleHasApplied_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsEaseInStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 1f, 1f), 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-test",
                element => StyleEaseCurve.NoteClasses(element, new[] { "hover:ease-in" }));

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_NoClassThatGivesATimingKeyword_When_ARuleResolvesEaseInOut_Then_ItEasesByTheKeyword()
        {
            // Arrange
            var expected = UssEasing.Evaluate(EasingMode.EaseInOut, 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-out-test", element => { });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AStepsLayerOnTheElement_When_AWidthTransitionRunsAQuarterSecond_Then_ItHasNotYetJumped()
        {
            // Arrange
            var steps = EaseFunction.OfSteps(2, StepJump.End);

            // Act
            var fraction = QuarterSecondIn(EasingMode.Linear, element => StyleEaseCurve.SetLayer(element, steps, EasingMode.Linear));

            // Assert
            Assert.That(fraction, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Given_ACurveWhoseKeywordIsNotTheOneTheElementResolves_When_AWidthTransitionRuns_Then_TheResolvedKeywordEasesIt()
        {
            // Arrange

            // Act
            var fraction = QuarterSecondIn(EasingMode.Linear, element => StyleEaseCurve.SetLayer(element, s_curve, EasingMode.EaseIn));

            // Assert
            Assert.That(fraction, Is.EqualTo(0.25f).Within(0.01f));
        }

        [Test]
        public void Given_ACurveReleasedFromTheElement_When_AWidthTransitionRuns_Then_ItEasesByItsKeyword()
        {
            // Arrange

            // Act
            var fraction = QuarterSecondIn(EasingMode.Linear, element =>
            {
                StyleEaseCurve.SetLayer(element, s_curve, EasingMode.Linear);
                StyleEaseCurve.Release(element);
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(0.25f).Within(0.01f));
        }
    }
}
