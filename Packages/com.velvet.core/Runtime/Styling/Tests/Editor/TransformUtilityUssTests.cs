using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the plain transform utilities (<c>_transforms.uss</c>) and the
    /// <c>.transition-transform</c> fix (<c>_effects.uss</c>). These mount a leaf inside a real
    /// <see cref="EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c> attached, force a
    /// layout pass so the cascade resolves, then read <c>resolvedStyle</c>. UITK 6.x cannot transition the
    /// combined <c>transform</c>; the animatable transform is the independent <c>translate</c> / <c>scale</c>
    /// / <c>rotate</c> properties, which is what <c>.transition-transform</c> must enumerate. GWT, one assert
    /// per case.
    /// </summary>
    [TestFixture]
    internal sealed class TransformUtilityUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        private Label MountAndResolveLabel(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: className, text: "x"));
            var leaf = _window.rootVisualElement.Q<Label>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        [Test]
        public void Given_Scale105Class_When_Resolved_Then_SetsUniformScale()
        {
            // Arrange / Act
            var leaf = MountAndResolveLabel("scale-105");

            // Assert
            Assert.That((leaf.resolvedStyle.scale.value.x, leaf.resolvedStyle.scale.value.y),
                Is.EqualTo((1.05f, 1.05f)));
        }

        [Test]
        public void Given_TransitionTransformClass_When_Resolved_Then_TransitionsIndependentTransformProperties()
        {
            // Arrange / Act
            var leaf = MountAndResolveLabel("transition-transform");
            var properties = leaf.resolvedStyle.transitionProperty.Select(p => p.ToString()).ToArray();

            // Assert — the independent transform properties are transitioned, not the (non-animatable) `transform`.
            Assert.That(properties, Is.EquivalentTo(new[] { "translate", "scale", "rotate" }));
        }
    }
}
