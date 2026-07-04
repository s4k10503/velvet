using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the border-radius scale values
    /// (<c>_tokens.uss</c>): <c>rounded-lg</c> is now 8px (was 16), <c>rounded-3xl</c> 24px (was 45),
    /// the bare <c>rounded</c> / <c>rounded-t</c> DEFAULT (4px) classes are net-new. Each mounts a leaf
    /// in a real <see cref="EditorWindow"/> panel with the bundled stylesheet, forces a layout pass, then
    /// reads <c>resolvedStyle</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TailwindRadiusScaleUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [Test]
        public void Given_RoundedLgClass_When_Resolved_Then_BorderRadiusIs8()
        {
            // Arrange/Act — rounded-lg == 0.5rem == 8px (Velvet previously baked 16px).
            var leaf = MountAndResolve("rounded-lg");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Rounded3xlClass_When_Resolved_Then_BorderRadiusIs24()
        {
            // Arrange/Act — rounded-3xl == 1.5rem == 24px (Velvet previously baked 45px).
            var leaf = MountAndResolve("rounded-3xl");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(24f));
        }

        [Test]
        public void Given_BareRoundedClass_When_Resolved_Then_BorderRadiusIs4()
        {
            // Arrange/Act — the bare `rounded` DEFAULT (0.25rem == 4px) had no Velvet class before.
            var leaf = MountAndResolve("rounded");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(4f));
        }

        [Test]
        public void Given_BareRoundedTClass_When_Resolved_Then_TopLeftRadiusIs4()
        {
            // Arrange/Act — the bare per-side `rounded-t` DEFAULT sets the two top corners to 4px.
            var leaf = MountAndResolve("rounded-t");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(4f));
        }
    }
}
