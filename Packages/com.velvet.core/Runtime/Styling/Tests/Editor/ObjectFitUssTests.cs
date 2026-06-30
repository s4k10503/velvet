using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the object-fit utilities (<c>_effects.uss</c>), mapped onto the
    /// modern <c>background-size</c> property for an element showing an image as background-image:
    /// <c>object-contain</c> → contain, <c>object-cover</c> → cover, <c>object-fill</c> → 100% 100% (stretch).
    /// Each mounts a leaf in a real <see cref="EditorWindow"/> panel with the bundled stylesheet, forces a
    /// layout pass, then reads <c>resolvedStyle.backgroundSize</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ObjectFitUssTests : PanelTestBase
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
        public void Given_ObjectContain_When_Resolved_Then_BackgroundSizeIsContain()
        {
            var leaf = MountAndResolve("object-contain");

            Assert.That(leaf.resolvedStyle.backgroundSize.sizeType, Is.EqualTo(BackgroundSizeType.Contain));
        }

        [Test]
        public void Given_ObjectCover_When_Resolved_Then_BackgroundSizeIsCover()
        {
            var leaf = MountAndResolve("object-cover");

            Assert.That(leaf.resolvedStyle.backgroundSize.sizeType, Is.EqualTo(BackgroundSizeType.Cover));
        }

        [Test]
        public void Given_ObjectFill_When_Resolved_Then_BackgroundSizeStretchesBothAxesToFull()
        {
            var leaf = MountAndResolve("object-fill");

            Assert.That((leaf.resolvedStyle.backgroundSize.x.value, leaf.resolvedStyle.backgroundSize.y.value),
                Is.EqualTo((100f, 100f)));
        }
    }
}
