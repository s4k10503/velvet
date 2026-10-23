using NUnit.Framework;
using UnityEngine;
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
        private const string PairSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/OwnClassPairRule.uss";

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
        public void Given_AClassOfOwnWhoseVariantClassSetsTheSameColour_When_TheVariantIsOn_Then_TheVariantClassColoursTheElement()
        {
            // Arrange — pair-dark is declared before pair-base, so on stylesheet order alone pair-base wins both.
            var card = MountPair("pair-base w-[40px] h-[40px]");

            // Act — the first panel pass reads the probes, and the second restyles the element.
            StyleClassProjection.Add(card, "pair-dark", StyleLayerPriority.Hover);
            ForcePanelUpdate(card.panel);
            ForcePanelUpdate(card.panel);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(card.resolvedStyle.backgroundColor), Is.EqualTo("0A141EFF"));
        }
    }
}
