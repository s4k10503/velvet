using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a carry taking over a transition whose easing the classes or the layer give carries it on that
    /// easing's curve, whether or not the transition's run event has reached the element, and on the curve the engine
    /// runs where the transition started before the element was listened to or its run event has already been seen.
    /// </summary>
    [TestFixture]
    internal sealed class StyleEaseCurveTakeOverTests : PanelTestBase
    {
        private double _now;
        private bool _ran;

        private static readonly BezierCurve s_tailwindEaseInOut = new(0.4f, 0f, 0.2f, 1f);

        // A second-long transition of property, on the keyword given, that has run three tenths of a second by the panel's
        // clock at 100 s, from the value from writes to the one to writes, and whose run event has not been dispatched. The
        // classes are noted before the transition starts where listenFirst, and after it where not.
        private VisualElement Started(string property, Action<VisualElement> from, Action<VisualElement> to, bool listenFirst = true,
            EasingMode mode = EasingMode.EaseInOut)
        {
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            element.style.transitionProperty = new List<StylePropertyName> { new(property) };
            element.style.transitionDuration = new List<TimeValue> { new(1f) };
            element.style.transitionDelay = new List<TimeValue> { new(-0.3f) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(mode) };
            element.RegisterCallback<TransitionRunEvent>(_ => _ran = true);
            from(element);
            ForcePanelUpdate(element.panel);
            _now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => _now);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            if (listenFirst)
            {
                StyleEaseCurve.NoteClasses(element, new[] { "transition-all" });
            }
            to(element);
            ForcePanelUpdate(element.panel);
            if (!listenFirst)
            {
                StyleEaseCurve.NoteClasses(element, new[] { "transition-all" });
            }
            return element;
        }

        private static float TailwindAtThreeTenths => StyleEaseCurve.Evaluate(s_tailwindEaseInOut, 0.3f);

        private static float EngineAtThreeTenths => UssEasing.Evaluate(EasingMode.EaseInOut, 0.3f);

        // NaN where a run event had already reached the element, which no expected value matches.
        private float WhereNoRunEventHasArrived(float value) => _ran ? float.NaN : value;

        // NaN where no run event had reached the element.
        private float WhereARunEventHasArrived(float value) => _ran ? value : float.NaN;

        [SetUp]
        public void ClearRan() => _ran = false;

        [Test]
        public void Given_AnOpacityTransitionOnEaseInOutClasses_When_ACarryTakesItOverBeforeItsRunEvent_Then_ItCarriesTailwindsEaseInOut()
        {
            // Arrange
            var element = Started("opacity", e => e.style.opacity = 0f, e => e.style.opacity = 1f);
            var carry = new LayoutIdCarry();

            // Act
            RunningStyleTransition.TakeOverOpacity(element, carry);
            var opacity = carry.Advance(0f);

            // Assert
            Assert.That(WhereNoRunEventHasArrived(opacity), Is.EqualTo(TailwindAtThreeTenths).Within(1e-2f));
        }

        [Test]
        public void Given_ARotateTransitionOnEaseInOutClasses_When_ACarryTakesItOverBeforeItsRunEvent_Then_ItCarriesTailwindsEaseInOut()
        {
            // Arrange
            var element = Started("rotate", e => e.style.rotate = new Rotate(0f), e => e.style.rotate = new Rotate(90f));
            var carry = new LayoutIdCarry();

            // Act
            RunningStyleTransition.TakeOverRotate(element, carry);
            var degrees = carry.Advance(0f);

            // Assert
            Assert.That(WhereNoRunEventHasArrived(degrees), Is.EqualTo(90f * TailwindAtThreeTenths).Within(1f));
        }

        [Test]
        public void Given_ATranslateTransitionOnEaseInOutClasses_When_ACarryTakesItOverBeforeItsRunEvent_Then_ItCarriesTailwindsEaseInOut()
        {
            // Arrange
            var element = Started("translate", e => e.style.translate = new Translate(0f, 0f), e => e.style.translate = new Translate(0f, 20f));
            var axes = new[] { new LayoutIdCarry(), new LayoutIdCarry() };

            // Act
            RunningStyleTransition.TakeOverTranslate(element, axes, new[] { LengthUnit.Pixel, LengthUnit.Pixel });
            var y = axes[1].Advance(0f);

            // Assert
            Assert.That(WhereNoRunEventHasArrived(y), Is.EqualTo(20f * TailwindAtThreeTenths).Within(0.2f));
        }

        [Test]
        public void Given_AScaleTransitionOnEaseInOutClasses_When_ACarryTakesItOverBeforeItsRunEvent_Then_ItCarriesTailwindsEaseInOut()
        {
            // Arrange
            var element = Started("scale", e => e.style.scale = new Scale(new Vector2(1f, 1f)),
                e => e.style.scale = new Scale(new Vector2(2f, 2f)));
            var axes = new[] { new LayoutIdCarry(), new LayoutIdCarry() };

            // Act
            RunningStyleTransition.TakeOverScale(element, axes);
            var x = axes[0].Advance(0f);

            // Assert
            Assert.That(WhereNoRunEventHasArrived(x), Is.EqualTo(1f + TailwindAtThreeTenths).Within(1e-2f));
        }

        [Test]
        public void Given_ARadiusTransitionOnEaseInOutClasses_When_ACarryTakesItOverBeforeItsRunEvent_Then_ItCarriesTailwindsEaseInOut()
        {
            // Arrange
            var element = Started("border-top-left-radius", e => e.style.borderTopLeftRadius = 0f, e => e.style.borderTopLeftRadius = 20f);
            var carry = new LayoutIdCarry();

            // Act
            RunningStyleTransition.TakeOverRadius(element, 0, carry, LengthUnit.Pixel);
            var radius = carry.Advance(0f);

            // Assert
            Assert.That(WhereNoRunEventHasArrived(radius), Is.EqualTo(20f * TailwindAtThreeTenths).Within(0.2f));
        }

        // GREEN_ON_BASE(characterization): the base hands a carry the curve UI Toolkit runs a transition on.
        // Dropping the RunningAtListen clause from StyleEaseCurve.TakenOver reddens it.
        [Test]
        public void Given_AnOpacityTransitionThatStartedBeforeTheElementWasListenedTo_When_ACarryTakesItOver_Then_ItKeepsUIToolkitsCurve()
        {
            // Arrange
            var element = Started("opacity", e => e.style.opacity = 0f, e => e.style.opacity = 1f, listenFirst: false);
            var carry = new LayoutIdCarry();

            // Act
            RunningStyleTransition.TakeOverOpacity(element, carry);
            var opacity = carry.Advance(0f);

            // Assert
            Assert.That(opacity, Is.EqualTo(EngineAtThreeTenths).Within(1e-2f));
        }

        // GREEN_ON_BASE(characterization): the base hands a carry the curve UI Toolkit runs a transition on.
        // Dropping the Run clause from StyleEaseCurve.TakenOver reddens it.
        [Test]
        public void Given_AnOpacityTransitionWhoseRunEventLeftItOnALinearCurve_When_ALayerGivesLinearAFunctionAndACarryTakesItOver_Then_ItKeepsTheLinearCurve()
        {
            // Arrange — Tailwind gives linear no curve, so the run event leaves the transition as the engine runs it.
            var element = Started("opacity", e => e.style.opacity = 0f, e => e.style.opacity = 1f, mode: EasingMode.Linear);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            StyleEaseCurve.SetLayer(element, new BezierCurve(0.9f, 0f, 0.1f, 1f), EasingMode.Linear);
            var carry = new LayoutIdCarry();

            // Act
            RunningStyleTransition.TakeOverOpacity(element, carry);
            var opacity = carry.Advance(0f);

            // Assert
            Assert.That(WhereARunEventHasArrived(opacity), Is.EqualTo(0.3f).Within(1e-2f));
        }
    }
}
