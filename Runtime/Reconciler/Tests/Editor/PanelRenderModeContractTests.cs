using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the engine fact <c>portals.md</c> builds the layer panels' limit on: a panel renders either as a
    /// screen-space overlay or in world space, so UI that must sit among scene geometry is a world-space panel.
    /// A mode between the two would put a layer among the geometry, and this fails when the engine adds one.
    /// </summary>
    internal sealed class PanelRenderModeContractTests
    {
        // GREEN_ON_BASE(characterization): reads the engine's enum, which no change of this branch touches.
        [Test]
        public void Given_TheEnginesPanelRenderModes_When_TheyAreListed_Then_OnlyOverlayAndWorldSpaceExist()
        {
            // Arrange
            var expected = "ScreenSpaceOverlay,WorldSpace";

            // Act
            var listed = string.Join(",", System.Enum.GetNames(typeof(PanelRenderMode)));

            // Assert
            Assert.That(listed, Is.EqualTo(expected));
        }
    }
}
