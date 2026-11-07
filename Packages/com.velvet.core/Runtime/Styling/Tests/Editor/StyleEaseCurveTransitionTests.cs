using System;
using System.Collections.Generic;
using NUnit.Framework;
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

        // The fraction of its way a width has covered a quarter of a second into a second-long transition from 0 to
        // 100, on an element whose transition list names inlineMode and which hold arranges.
        private static float QuarterSecondIn(EasingMode inlineMode, Action<VisualElement> hold)
        {
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            var element = new VisualElement();
            host.Root.Add(element);
            element.style.transitionProperty = new List<StylePropertyName> { new("width") };
            element.style.transitionDuration = new List<TimeValue> { new(1f) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(inlineMode) };
            element.style.width = 0f;
            hold(element);
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
        public void Given_ABundledEaseInOutClass_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);

            // Act
            var fraction = QuarterSecondIn(EasingMode.EaseInOut,
                element => StyleEaseCurve.NoteClasses(element, new[] { "transition-all", "ease-in-out" }));

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
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
