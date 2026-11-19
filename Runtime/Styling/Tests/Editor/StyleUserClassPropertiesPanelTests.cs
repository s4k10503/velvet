using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the ranking of two classes of an application's own, which no bundled stylesheet names, by the properties
    /// a probe reads off the rules that declare them.
    /// </summary>
    internal sealed class StyleUserClassPropertiesPanelTests : PanelTestBase
    {
        private const string PairSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";

        protected override void LoadStyleSheets()
            => _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(PairSheet));

        private VisualElement MountPair(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: className));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            return card;
        }

        [Test]
        public void Given_AClassOfOwnWhoseVariantClassSetsTheSameRadius_When_TheVariantIsOn_Then_TheVariantClassRoundsTheElement()
        {
            // Arrange — layout-id-test-round is declared before layout-id-test-rounder, so on stylesheet order alone the
            // rounder one wins both.
            var card = MountPair("layout-id-test-rounder w-[400px] h-[400px]");

            // Act — the first panel pass reads the probes, and the second restyles the element.
            StyleClassProjection.Add(card, "layout-id-test-round", StyleLayerPriority.Hover);
            ForcePanelUpdate(card.panel);
            ForcePanelUpdate(card.panel);

            // Assert — the payload's 10px, not the base's 20px.
            Assert.That(card.resolvedStyle.borderTopLeftRadius, Is.EqualTo(10f).Within(1e-3f));
        }
    }
}
