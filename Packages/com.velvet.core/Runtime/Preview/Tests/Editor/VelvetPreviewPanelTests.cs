using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewPanelTests
    {
        // The preview window takes a panel without settings to be at a scale of one.
        [Test]
        public void Given_NoPanelSettings_When_APreviewPanelIsBuilt_Then_ItIsAConstantPixelSizePanelAtScaleOne()
        {
            // Arrange
            const int width = 64;
            const int height = 32;

            // Act
            using var panel = new VelvetPreviewPanel(null, width, height);
            var settings = (panel.Root.panel as IRuntimePanel)?.panelSettings;

            // Assert
            Assert.That(
                (settings?.scaleMode, settings?.scale),
                Is.EqualTo(((PanelScaleMode?)PanelScaleMode.ConstantPixelSize, (float?)1f)));
        }
    }
}
