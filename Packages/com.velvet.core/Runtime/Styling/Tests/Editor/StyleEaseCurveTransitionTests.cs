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

        // Holds layout-id-test-two, whose two durations pair with two properties, so a narrowing writes the timing lists.
        private const string PairedSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";

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
        public void Given_ATransitionUtilityWhoseRulesANarrowingWritesInline_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsDefaultCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-out-test", element =>
            {
                StyleEaseCurve.NoteClasses(element, new[] { "transition-colors" });
                MotionTweenTiming.Narrow(element, new List<TimeValue> { new(1f) },
                    new List<EasingFunction> { new(EasingMode.EaseInOut) }, new List<TimeValue> { new(0f) });
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        // GREEN_ON_BASE(characterization): the base's CurveFor reads the inline slot alone, so a list code writes and puts
        // back leaves nothing behind. Calling `Adopt` from MotionTweenTiming.PeekOwn keeps the list as the element's own
        // once it is put back, which reddens this case.
        [Test]
        public void Given_ATransitionUtilityOnATweenedElementWhoseCodeWritesATimingAndPutsItBack_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsDefaultCurveStandsThere()
        {
            // Arrange — as a sortable list's drag writes its own timing around a shift and puts the element's back.
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-out-test", element =>
            {
                StyleEaseCurve.NoteClasses(element, new[] { "transition-colors" });
                MotionTweenTiming.End(element, MotionTweenTiming.Begin(element));
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Ease) };
                StyleEaseCurve.CurveFor(element, EasingMode.Ease);
                element.style.transitionTimingFunction = StyleKeyword.Null;
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        // GREEN_ON_BASE(characterization): the base's CurveFor reads the inline slot, where code's keyword stands.
        // Dropping `Same(slot, written)` from MotionTweenTiming.Peek reads the tween's own list beneath it, which
        // reddens this case.
        [Test]
        public void Given_ATransitionUtilityOnATweenedElementWhoseCodeHoldsATimingInline_When_AWidthTransitionRunsAQuarterSecond_Then_TheKeywordEasesIt()
        {
            // Arrange
            var expected = UssEasing.Evaluate(EasingMode.EaseInOut, 0.25f);

            // Act
            var fraction = QuarterSecondByRule("ease-in-out-test", element =>
            {
                StyleEaseCurve.NoteClasses(element, new[] { "transition-colors" });
                MotionTweenTiming.End(element, MotionTweenTiming.Begin(element));
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseInOut) };
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AnEaseLayerUnderAMotionTweenNamingItsKeyword_When_AWidthTransitionRunsAQuarterSecond_Then_TheKeywordEasesIt()
        {
            // Arrange
            var expected = UssEasing.Evaluate(EasingMode.EaseOut, 0.25f);

            // Act
            var fraction = QuarterSecondIn(EasingMode.EaseOut, element =>
            {
                StyleEaseCurve.SetLayer(element, s_curve, EasingMode.EaseOut);
                MotionTweenTiming.Begin(element);
                MotionTweenTiming.Write(element, null, new List<EasingFunction> { new(EasingMode.EaseOut) }, null);
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        // GREEN_ON_BASE(characterization): the base retimes by the layer whatever wrote the keyword it left.
        // Dropping `Same(element.style.transitionTimingFunction, hold.WrittenCurve)` from MotionTweenTiming.PlaysCurve
        // reads the Motion tween's list as holding the slot after code writes over it, which reddens this case.
        [Test]
        public void Given_AnEaseLayerWhoseKeywordCodeWritesOverAMotionTweens_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(s_curve, 0.25f);

            // Act
            var fraction = QuarterSecondIn(EasingMode.EaseOut, element =>
            {
                StyleEaseCurve.SetLayer(element, s_curve, EasingMode.EaseOut);
                MotionTweenTiming.Begin(element);
                MotionTweenTiming.Write(element, null, new List<EasingFunction> { new(EasingMode.EaseIn) }, null);
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOut) };
            });

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        // The timing a paired-timing element holds inline once a narrowing of its opacity ends, after a drag wrote its own
        // lists over the narrowing's, `midDrag` read the element's timing, and the drag put the lists back.
        private static StyleKeyword TimingLeftInlineAfterADrag(Action<VisualElement> midDrag)
        {
            using var host = new HeadlessEditorPanelHost();
            host.Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(PairedSheet));
            var element = new VisualElement();
            element.AddToClassList("layout-id-test-two");
            host.Root.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var owner = new object();
            MotionNativeTransitionGuard.NarrowIfIntercepted(element, owner, MotionTransitionSlots.Opacity);
            var style = element.style;
            // Copies, as a sortable list's drag saves them: a list read out of a slot is refilled by that slot's next write
            // (MotionNativeTransitionGuard.ExcludeFromHeldList).
            var (property, duration, timing) = (MotionTweenTiming.Copy(style.transitionProperty),
                MotionTweenTiming.Copy(style.transitionDuration), MotionTweenTiming.Copy(style.transitionTimingFunction));
            style.transitionProperty = new List<StylePropertyName> { new("translate") };
            style.transitionDuration = new List<TimeValue> { new(200f, TimeUnit.Millisecond) };
            style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Ease) };
            midDrag(element);
            (style.transitionProperty, style.transitionDuration, style.transitionTimingFunction) = (property, duration, timing);
            MotionNativeTransitionGuard.Release(element, owner);
            return element.style.transitionTimingFunction.keyword;
        }

        [Test]
        public void Given_ANarrowingOfPairedTimingWhoseListsADragPutsBack_When_AProjectionPassRunsMidDrag_Then_NoTimingIsLeftInlineOnceItEnds()
        {
            // Act
            var keyword = TimingLeftInlineAfterADrag(MotionNativeTransitionGuard.Refresh);

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ANarrowingOfPairedTimingWhoseListsADragPutsBack_When_ItsTransitionsAreReadMidDrag_Then_NoTimingIsLeftInlineOnceItEnds()
        {
            // Act
            var keyword = TimingLeftInlineAfterADrag(element => StyleCascade.Lists(element));

            // Assert
            Assert.That(keyword, Is.EqualTo(StyleKeyword.Null));
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
