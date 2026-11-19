#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies an easing token a stylesheet declares as <c>cubic-bezier()</c>, which UI Toolkit's own importer stores
    /// with no value: the transition of a class reading it runs along the curve.
    /// </summary>
    internal sealed class ThemeTokenEaseTests : PanelTestBase
    {
        // Written from this file because the sheet must exist as an asset before Velvet's importer is selected for it.
        private const string SheetPath = "Assets/VelvetThemeTokenEase.uss";

        private static readonly BezierCurve s_curve = new(0.9f, 0f, 0.1f, 1f);

        public override void TearDown()
        {
            try
            {
                base.TearDown();
            }
            finally
            {
                AssetDatabase.DeleteAsset(SheetPath);
            }
        }

        // The fraction of its way a width has covered a quarter of a second into a second-long transition, on a leaf
        // of the class given inside a host of the classes given, under the sheet text given.
        private float QuarterSecond(string sheetText, string hostClasses, string leafClass)
        {
            var sheet = RemSheetImport.Import(SheetPath, sheetText);
            _window.rootVisualElement.styleSheets.Add(sheet);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: hostClasses, children: new VNode?[]
            {
                V.Div(name: "leaf", className: leafClass),
            }));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(leaf.panel, () => now);
            leaf.style.transitionProperty = new List<StylePropertyName> { new("width") };
            leaf.style.transitionDuration = new List<TimeValue> { new(1f) };
            leaf.style.width = 0f;
            ForcePanelUpdate(leaf.panel);
            ForcePanelUpdate(leaf.panel);
            leaf.style.width = 100f;
            ForcePanelUpdate(leaf.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            ForcePanelUpdate(leaf.panel);
            return leaf.resolvedStyle.width / 100f;
        }

        [Test]
        public void Given_AnEaseClassNamingACubicBezier_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(s_curve, 0.25f);

            // Act
            var fraction = QuarterSecond(".unused { width: 1px; }", "theme-token-host", "ease-[cubic-bezier(0.9,0,0.1,1)]");

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AnEaseTokenDeclaredOnceAsCubicBezier_When_AWidthTransitionOfItsClassRunsAQuarterSecond_Then_ItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var expected = StyleEaseCurve.Evaluate(s_curve, 0.25f);

            // Act
            var fraction = QuarterSecond(".theme-token-host {\n    --ease-snappytest: cubic-bezier(0.9, 0, 0.1, 1);\n}\n",
                "theme-token-host", "ease-snappytest");

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AnEaseTokenDeclaredDifferentlyUnderAThemeClass_When_AWidthTransitionRunsAQuarterSecond_Then_ItIsWhereTheThemeClassCurveStandsThere()
        {
            // Arrange
            var themed = new BezierCurve(0.1f, 0.9f, 0.2f, 1f);
            var expected = StyleEaseCurve.Evaluate(themed, 0.25f);

            // Act
            var fraction = QuarterSecond(
                ".theme-token-host {\n    --ease-snappytest: cubic-bezier(0.9, 0, 0.1, 1);\n}\n"
                + ".theme-token-host.theme-token-alt {\n    --ease-snappytest: cubic-bezier(0.1, 0.9, 0.2, 1);\n}\n",
                "theme-token-host theme-token-alt", "ease-snappytest");

            // Assert
            Assert.That(fraction, Is.EqualTo(expected).Within(0.02f));
        }
    }
}
