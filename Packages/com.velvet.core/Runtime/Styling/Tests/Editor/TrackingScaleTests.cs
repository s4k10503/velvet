using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the tracking-* (letter-spacing) scale to Tailwind's em values baked at the 16px root font.
    /// UI Toolkit letter-spacing has no em unit, so the scale is fixed px, exact at the default text size.
    /// </summary>
    [TestFixture]
    internal sealed class TrackingScaleTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        private VisualElement MountLeaf(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        [Test]
        public void Given_TrackingWidest_When_Resolved_Then_ItIsPointOneEmAtTheSixteenPxRoot()
        {
            // Tailwind tracking-widest is 0.1em; at the 16px root that is 1.6px.
            var leaf = MountLeaf("tracking-widest");

            Assert.That(leaf.resolvedStyle.letterSpacing, Is.EqualTo(1.6f).Within(1e-3f));
        }

        [Test]
        public void Given_TrackingTight_When_Resolved_Then_ItIsNegativePointZeroTwoFiveEm()
        {
            // Tailwind tracking-tight is -0.025em; at the 16px root that is -0.4px.
            var leaf = MountLeaf("tracking-tight");

            Assert.That(leaf.resolvedStyle.letterSpacing, Is.EqualTo(-0.4f).Within(1e-3f));
        }
    }
}
