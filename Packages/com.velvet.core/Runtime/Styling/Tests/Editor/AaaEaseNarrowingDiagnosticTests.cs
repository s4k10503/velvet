using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // TEMPORARY DIAGNOSTIC fixture, out before merge. Named to run ahead of StyleEaseCurveTransitionTests, whose
    // narrowing case reads 1.0: the same case here, with no case of that fixture run before it, says whether an
    // earlier case there leaves something behind that this one runs into.
    internal sealed class AaaEaseNarrowingDiagnosticTests
    {
        private const string Sheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/EaseTransitionTestSheet.uss";

        [Test]
        public void Given_DiagnosticATransitionUtilityWhoseRulesANarrowingWritesInlineInAFixtureOfItsOwn_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTailwindsDefaultCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(new BezierCurve(0.4f, 0f, 0.2f, 1f), 0.25f);
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            host.Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet));
            var element = new VisualElement();
            host.Root.Add(element);
            element.style.width = 0f;
            element.AddToClassList("ease-in-out-test");
            StyleEaseCurve.NoteClasses(element, new[] { "transition-colors" });
            MotionTweenTiming.Narrow(element, null, new List<EasingFunction> { new(EasingMode.EaseInOut) }, null);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);

            // Act
            element.style.width = 100f;
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);

            // Assert
            Assert.That(element.resolvedStyle.width / 100f, Is.EqualTo(expected).Within(0.02f));
        }
    }
}
