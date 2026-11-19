using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where a filtered shadow caster's bounds-spacer sits once laid out: an absolute child's left resolves
    /// against the caster's padding box, so the spacer moves left by the caster's left border to cover the same paint.
    /// </summary>
    internal sealed class BoxShadowSpacerBorderTests : PanelTestBase
    {
        // GREEN_ON_BASE(characterization): the base shifts a shadow spacer into its caster's padding box.
        [Test]
        public void Given_ALaidOutShadowCaster_When_ItsClassesGainALeftBorder_Then_TheSpacerMovesLeftByTheBorder()
        {
            // Arrange
            var element = new VisualElement { style = { width = 40f, height = 40f } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            var plainClasses = new[] { "shadow-lg" };
            StyleShadowClass.TryExtract(plainClasses, out var layers);
            var binding = BoxShadowSilhouette.Attach(element, layers, plainClasses);
            BoxShadowSilhouette.SetWantSpacer(element, binding, true, plainClasses);
            var plainLeft = binding.BoundsSpacer!.style.left.value.value;

            // Act
            BoxShadowSilhouette.SetWantSpacer(element, binding, true, new[] { "shadow-lg", "border-l-4" });

            // Assert
            Assert.That(plainLeft - binding.BoundsSpacer!.style.left.value.value, Is.EqualTo(4f).Within(1e-4f));
        }
    }
}
