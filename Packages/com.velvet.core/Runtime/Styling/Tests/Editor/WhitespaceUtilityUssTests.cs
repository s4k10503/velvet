using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the whitespace-pre / whitespace-pre-wrap utilities (<c>_typography.uss</c>)
    /// — the two whitespace-* values that map straight onto a <see cref="WhiteSpace"/> enum member.
    /// whitespace-pre-line is NOT covered here: it has no USS rule of its own (a display-string rewrite
    /// instead — see <c>StyleTextEffectClassTests</c> / <c>StyleTextEffectPanelTests</c>). Mounts a leaf
    /// inside a real <see cref="UnityEditor.EditorWindow"/> panel with the bundled
    /// <c>StyleUtilities.uss</c> attached, forces a layout pass so the cascade resolves, then reads
    /// <c>resolvedStyle</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class WhitespaceUtilityUssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [Test]
        public void Given_WhitespacePre_When_Resolved_Then_WhiteSpaceIsPre()
        {
            var leaf = MountAndResolve("whitespace-pre");

            Assert.That(leaf.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Pre));
        }

        [Test]
        public void Given_WhitespacePreWrap_When_Resolved_Then_WhiteSpaceIsPreWrap()
        {
            var leaf = MountAndResolve("whitespace-pre-wrap");

            Assert.That(leaf.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.PreWrap));
        }
    }
}
