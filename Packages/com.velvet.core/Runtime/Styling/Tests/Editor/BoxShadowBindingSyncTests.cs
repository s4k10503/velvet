using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a shadow binding takes from a patch off the panel, where its corner radius comes from the class list,
    /// and that the bake material is dropped with the bakes when a play session starts without a domain reload.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowBindingSyncTests
    {
        // GREEN_ON_BASE(characterization): the base reads an off-panel shadow's radius from the patch's classes.
        [Test]
        public void Given_AShadowedElementOffThePanel_When_APatchAddsRoundedLg_Then_TheShadowCornerTakesItsRadius()
        {
            // Arrange — rounded-lg's shadow corner radius mirrors the --radius-lg token, 8px.
            var element = new VisualElement();
            StyleShadowClass.TryExtract(new[] { "shadow-lg" }, out var layers);
            var binding = BoxShadowSilhouette.Attach(element, layers, new[] { "shadow-lg" });

            // Act
            BoxShadowSilhouette.Sync(element, binding,
                new BoxShadowSyncRequest { Layers = layers, ClassNames = new[] { "shadow-lg", "rounded-lg" } });

            // Assert
            Assert.That(binding.CornerRadii.x, Is.EqualTo(8f));
        }

        // GREEN_ON_BASE(characterization): the base drops its bake material when a play session resets the caches.
        [Test]
        public void Given_ABakeMaterial_When_ThePlaySessionResetsTheCaches_Then_TheMaterialIsDropped()
        {
            // Arrange
            const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
            var field = typeof(BoxShadowBaker).GetField("s_material", Static)!;
            if (field.GetValue(null) == null)
            {
                field.SetValue(null, new Material(Shader.Find("Hidden/Internal-Colored")));
            }

            // Act
            typeof(BoxShadowBaker).GetMethod("ResetStaticCaches", Static)!.Invoke(null, null);

            // Assert
            Assert.That(field.GetValue(null), Is.Null);
        }
    }
}
