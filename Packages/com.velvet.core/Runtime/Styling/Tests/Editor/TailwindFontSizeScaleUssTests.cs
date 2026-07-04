using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the font-size scale in <c>_tokens.uss</c>:
    /// <c>text-lg</c> is now 18px (was 20), <c>text-2xl</c> 24px (was 30), <c>text-4xl</c> 36px (was 42),
    /// and the small steps grow slightly (<c>text-xs</c> 12 / <c>text-sm</c> 14). These assert the FRAMEWORK
    /// default with only the bundled stylesheet attached; the demo pins its own larger scale separately. GWT,
    /// one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TailwindFontSizeScaleUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        private VisualElement MountLabelAndResolve(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: className, text: "x"));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        [Test]
        public void Given_TextLgClass_When_Resolved_Then_FontSizeIs18()
        {
            // Arrange / Act — text-lg == 1.125rem == 18px (Velvet previously baked 20px).
            var leaf = MountLabelAndResolve("text-lg");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(18f));
        }

        [Test]
        public void Given_Text2xlClass_When_Resolved_Then_FontSizeIs24()
        {
            // Arrange / Act — text-2xl == 1.5rem == 24px (Velvet previously baked 30px).
            var leaf = MountLabelAndResolve("text-2xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(24f));
        }

        [Test]
        public void Given_Text4xlClass_When_Resolved_Then_FontSizeIs36()
        {
            // Arrange / Act — text-4xl == 2.25rem == 36px (Velvet previously baked 42px).
            var leaf = MountLabelAndResolve("text-4xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(36f));
        }

        [Test]
        public void Given_TextXsClass_When_Resolved_Then_FontSizeIs12()
        {
            // Arrange / Act — text-xs == 0.75rem == 12px (Velvet previously baked 11px).
            var leaf = MountLabelAndResolve("text-xs");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(12f));
        }
    }
}
