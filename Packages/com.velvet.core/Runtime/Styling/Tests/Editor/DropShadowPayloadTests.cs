using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A drop-shadow layer no parser produced reaches the compose without throwing.
    /// </summary>
    internal sealed class DropShadowPayloadTests
    {
        [Test]
        public void Given_ADropShadowLayerWithNoPayload_When_Applied_Then_NothingIsComposed()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.FilterDropShadow, 7f, LengthUnit.Pixel));

            // Assert
            Assert.That(element.style.filter.value?.Count ?? 0, Is.EqualTo(0));
        }
    }
}
