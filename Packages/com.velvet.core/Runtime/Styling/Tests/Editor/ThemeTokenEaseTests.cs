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

        [Test]
        public void Given_AnEaseTokenDeclaredAsCubicBezier_When_AWidthTransitionOfItsClassRunsAQuarterSecond_Then_ItIsWhereTheCurveStandsThere()
        {
            // Arrange
            var sheet = RemSheetImport.Import(SheetPath, ".theme-token-host {\n    --ease-snappytest: cubic-bezier(0.9, 0, 0.1, 1);\n}\n");
            _window.rootVisualElement.styleSheets.Add(sheet);
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "theme-token-host", children: new VNode?[]
            {
                V.Div(name: "leaf", className: "ease-snappytest"),
            }));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(leaf.panel, () => now);
            leaf.style.transitionProperty = new List<StylePropertyName> { new("width") };
            leaf.style.transitionDuration = new List<TimeValue> { new(1f) };
            leaf.style.width = 0f;
            ForcePanelUpdate(leaf.panel);
            var expected = StyleEaseCurve.Evaluate(s_curve, 0.25f);

            // Act
            leaf.style.width = 100f;
            ForcePanelUpdate(leaf.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            now += 0.25;
            EditorPanelTestHelpers.DriveAnimationsOnce(leaf.panel);
            ForcePanelUpdate(leaf.panel);

            // Assert
            Assert.That(leaf.resolvedStyle.width / 100f, Is.EqualTo(expected).Within(0.02f));
        }
    }
}
