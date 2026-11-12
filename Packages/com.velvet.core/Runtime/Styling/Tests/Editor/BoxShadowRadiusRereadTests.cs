using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a shadow is baked against the corner radius its caster shows when it is painted, where the radius
    /// moved with no geometry change and no patch to re-read it on.
    /// </summary>
    internal sealed class BoxShadowRadiusRereadTests : PanelTestBase
    {
        private static readonly MethodInfo s_boxOf =
            typeof(BoxShadowSilhouette).GetMethod("BoxOf", BindingFlags.NonPublic | BindingFlags.Static)!;

        // GREEN_ON_BASE(characterization): the base reads a caster's corner radius again for each paint.
        [Test]
        public void Given_AShadowCasterWhoseRadiusChangesInline_When_ItsShadowBoxIsTaken_Then_ItHasTheNewRadius()
        {
            // Arrange
            var element = new VisualElement { style = { width = 40f, height = 40f, borderTopLeftRadius = 4f } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            var classes = new[] { "shadow-lg" };
            StyleShadowClass.TryExtract(classes, out var layers);
            var binding = BoxShadowSilhouette.Attach(element, layers, classes);
            var attached = binding.CornerRadii.x;
            element.style.borderTopLeftRadius = 10f;
            ForcePanelUpdate(element.panel);

            // Act
            var box = (ShadowBox)s_boxOf.Invoke(null, new object[] { element, binding, 40f, 40f });

            // Assert — the radius read at attach is the control.
            Assert.That(attached == 4f ? box.Radii.x : float.NaN, Is.EqualTo(10f));
        }
    }
}
