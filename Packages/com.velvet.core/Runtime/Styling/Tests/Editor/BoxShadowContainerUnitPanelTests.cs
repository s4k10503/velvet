using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A shadow length in a container-relative unit, measured against the content box of the nearest container as
    /// Chromium measures it (a container 400px wide with 20px of padding and a 10px border gives 1cqw = 3.4px, and with
    /// only the padding 3.6px). A container is inline-size, so cqh and cqb read the panel's height, as CSS does where
    /// the block axis is not contained. GWT, one assert per case.
    /// </summary>
    internal sealed class BoxShadowContainerUnitPanelTests : PanelTestBase
    {
        private VisualElement Mount(string classes, string cardClasses)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: classes, children: new VNode?[] { V.Div(name: "card", className: cardClasses) }));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            return card;
        }

        private ShadowSpec Layer(VisualElement card)
            => _mounted.Root.Reconciler.Context.ShadowBindings[card].Layers[0].MeasuredOn(card);

        [TestCase("cqw")]
        [TestCase("cqi")]
        public void Given_AnInlineContainerUnit_When_InsideAContainer_Then_ItIsAPercentOfTheContentWidth(string unit)
        {
            // Act
            var card = Mount(StyleResponsiveScope.ContainerClass + " w-[400px] p-[20px]", "w-[10px] h-[10px] shadow-[0_0_0_1" + unit + "_#000]");

            // Assert
            Assert.That(Layer(card).Spread, Is.EqualTo(3.6f).Within(0.01f));
        }

        [Test]
        public void Given_ALayerWithAContainerUnitAndAModifier_When_InsideAContainer_Then_ItIsDrawnAtTheModifiersAlpha()
        {
            // Act
            var card = Mount(StyleResponsiveScope.ContainerClass + " w-[400px] p-[20px]",
                "w-[10px] h-[10px] shadow-[0_0_4px_1cqw_#000]/50");

            // Assert
            Assert.That(Layer(card).Color.a, Is.EqualTo(0.5f).Within(0.01f));
        }

        [TestCase("cqw")]
        [TestCase("cqi")]
        [TestCase("cqh")]
        [TestCase("cqb")]
        public void Given_AContainerUnitWithNoContainerAbove_When_Measured_Then_ItIsTheSmallViewportUnit(string unit)
        {
            // Act — the panel's width for cqw and cqi, its height for cqh and cqb, where the viewport units are the small ones.
            var card = Mount("w-[400px]", "w-[10px] h-[10px] shadow-[0_0_0_1" + unit + "_#000]");
            var viewport = unit is "cqw" or "cqi" ? "vw" : "vh";
            var spread = Layer(card).Spread;
            _mounted.Dispose();
            _window.rootVisualElement.Clear();
            var again = Mount("w-[400px]", "w-[10px] h-[10px] shadow-[0_0_0_1" + viewport + "_#000]");

            // Assert
            Assert.That(spread, Is.EqualTo(Layer(again).Spread).Within(0.01f));
        }

        [Test]
        public void Given_AWidthInContainerWidthUnits_When_InsideAContainer_Then_ItIsAPercentOfTheContentWidth()
        {
            // Act — Chromium computes 180px for 50cqw in a container 400px wide with 20px of padding.
            var card = Mount(StyleResponsiveScope.ContainerClass + " w-[400px] p-[20px]", "w-[50cqw] h-[10px]");
            for (var pass = 0; pass < 3; pass++)
            {
                // The poll that re-reads a length after its container moves runs every 16 ms.
                System.Threading.Thread.Sleep(20);
                EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);
                EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            }

            // Assert
            Assert.That(card.style.width.value.value, Is.EqualTo(180f).Within(0.01f));
        }
    }
}
