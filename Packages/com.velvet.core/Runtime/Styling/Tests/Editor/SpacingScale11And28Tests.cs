using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Coverage for the spacing-scale steps 11 (2.75rem = 44px) and 28 (7rem = 112px), which
    /// were the only two standard steps missing across every family. Verified at all three layers that consume
    /// the scale: the bundled USS (resolvedStyle), the arbitrary-value resolver's static-scale dict (negative
    /// margins), and the gap polyfill's scale dict. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SpacingScale11And28Tests : PanelTestBase
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
        public void Given_P11Class_When_Resolved_Then_PaddingIs44()
        {
            // p-11 == 2.75rem == 44px.
            var leaf = MountAndResolve("p-11");

            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(44f));
        }

        [Test]
        public void Given_P28Class_When_Resolved_Then_PaddingIs112()
        {
            // p-28 == 7rem == 112px.
            var leaf = MountAndResolve("p-28");

            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(112f));
        }

        [Test]
        public void Given_W11Class_When_Resolved_Then_WidthIs44()
        {
            var leaf = MountAndResolve("w-11");

            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(44f));
        }

        [Test]
        public void Given_NegativeMargin11_When_Parsed_Then_ResolvesMarginTopNegative44()
        {
            // The static-scale resolver path (negative margins / translate presets) must know step 11.
            var ok = StyleArbitraryValueResolver.TryParse("-mt-11", out var s);

            Assume.That(ok, Is.True, "Precondition: -mt-11 resolves on the static scale");
            Assert.That((s.Property, s.Value), Is.EqualTo((ArbitraryProperty.MarginTop, -44f)));
        }

        [Test]
        public void Given_Gap28_When_Parsed_Then_ResolvesHundredTwelvePx()
        {
            // The gap polyfill's scale dict must also know step 28.
            var ok = StyleGapClass.TryParse("gap-28", out var gap, out _);

            Assume.That(ok, Is.True, "Precondition: gap-28 resolves in the gap polyfill");
            Assert.That(gap, Is.EqualTo(112f));
        }
    }
}
