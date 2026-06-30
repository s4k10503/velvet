using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for the USS additions that have no C# parse path: the
    /// <c>mx-auto</c>/<c>my-auto</c> centering margins (<c>_spacing.uss</c>), the <c>white</c>/<c>black</c>
    /// color classes (<c>_typography.uss</c>/<c>_backgrounds.uss</c>/<c>_borders.uss</c>), the numeric
    /// <c>duration-{ms}</c> scale (<c>_state_variants.uss</c>), the static position scale
    /// (<c>_layout.uss</c>), and the extended spacing/sizing scale plus <c>border-8</c>
    /// (<c>_spacing.uss</c>/<c>_sizing.uss</c>/<c>_borders.uss</c>). Each mounts a leaf in a real
    /// <see cref="EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c>, forces a layout pass,
    /// then reads <c>resolvedStyle</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TailwindBatch1UssTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        protected override Rect WindowSize => new Rect(0, 0, 600, 600);

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        private VisualElement MountChildAndResolve(string parentClassName, string childClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: parentClassName, V.Div(name: "leaf", className: childClassName)));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        // --- B-1a: mx-auto / my-auto centering ---

        [Test]
        public void Given_MxAutoChildInRow_When_Resolved_Then_ChildIsHorizontallyCentered()
        {
            // Arrange/Act — a 100px child in a 200px row: mx-auto absorbs the 100px of free space as equal
            // left/right margins, so the child's laid-out x offset within the parent is 50px (centered).
            // (resolvedStyle.marginLeft reports an auto margin as 0, so the layout offset is the real probe.)
            var leaf = MountChildAndResolve("flex w-[200px]", "w-[100px] mx-auto");

            // Assert
            Assert.That(leaf.layout.x, Is.EqualTo(50f).Within(0.5f));
        }

        [Test]
        public void Given_MyAutoChildInColumn_When_Resolved_Then_ChildIsVerticallyCentered()
        {
            // Arrange/Act — a 100px child in a 200px column: my-auto absorbs the 100px of free space as equal
            // top/bottom margins, so the child's laid-out y offset within the parent is 50px (centered).
            var leaf = MountChildAndResolve("flex flex-col h-[200px]", "h-[100px] my-auto");

            // Assert
            Assert.That(leaf.layout.y, Is.EqualTo(50f).Within(0.5f));
        }

        // --- B-1b: white / black color classes ---

        [Test]
        public void Given_TextWhiteClass_When_Resolved_Then_ColorIsWhite()
        {
            // Arrange/Act
            var leaf = MountAndResolve("text-white");

            // Assert
            Assert.That(leaf.resolvedStyle.color, Is.EqualTo(Color.white));
        }

        [Test]
        public void Given_TextBlackClass_When_Resolved_Then_ColorIsBlack()
        {
            // Arrange/Act
            var leaf = MountAndResolve("text-black");

            // Assert
            Assert.That(leaf.resolvedStyle.color, Is.EqualTo(Color.black));
        }

        [Test]
        public void Given_BgBlackClass_When_Resolved_Then_BackgroundColorIsBlack()
        {
            // Arrange/Act — the default background is transparent, so opaque black is a discriminating result.
            var leaf = MountAndResolve("bg-black");

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor, Is.EqualTo(Color.black));
        }

        [Test]
        public void Given_BorderWhiteClass_When_Resolved_Then_BorderColorIsWhite()
        {
            // Arrange/Act
            var leaf = MountAndResolve("border-white");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopColor, Is.EqualTo(Color.white));
        }

        // --- B-2: numeric transition-duration scale ---

        [Test]
        public void Given_Duration150Class_When_Resolved_Then_TransitionDurationIsPointOneFiveSeconds()
        {
            // Arrange/Act — duration-150 = 150ms = 0.15s.
            var leaf = MountAndResolve("duration-150");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.EqualTo(0.15f).Within(1e-5f));
        }

        [Test]
        public void Given_Duration1000Class_When_Resolved_Then_TransitionDurationIsOneSecond()
        {
            // Arrange/Act — the top of the scale, duration-1000 = 1s.
            var leaf = MountAndResolve("duration-1000");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.EqualTo(1f).Within(1e-5f));
        }

        // --- B-4: static position scale ---

        [Test]
        public void Given_Top4Class_When_Resolved_Then_TopIs16()
        {
            // Arrange/Act — --space-4 == 16px (top-4 = 1rem).
            var leaf = MountAndResolve("absolute top-4");

            // Assert
            Assert.That(leaf.resolvedStyle.top, Is.EqualTo(16f));
        }

        [Test]
        public void Given_Left2Point5Class_When_Resolved_Then_LeftIs10()
        {
            // Arrange/Act — left-2.5 spelled in the dash convention (left-2-5) == 10px.
            var leaf = MountAndResolve("absolute left-2-5");

            // Assert
            Assert.That(leaf.resolvedStyle.left, Is.EqualTo(10f));
        }

        [Test]
        public void Given_InsetX6Class_When_Resolved_Then_LeftAndRightAre24()
        {
            // Arrange/Act — inset-x-* sets left + right; --space-6 == 24px.
            var leaf = MountAndResolve("absolute inset-x-6");

            // Assert
            Assert.That((leaf.resolvedStyle.left, leaf.resolvedStyle.right), Is.EqualTo((24f, 24f)));
        }

        [Test]
        public void Given_Inset4Class_When_Resolved_Then_AllFourEdgesAre16()
        {
            // Arrange/Act — inset-* sets all four edges; --space-4 == 16px.
            var leaf = MountAndResolve("absolute inset-4");

            // Assert
            Assert.That(
                (leaf.resolvedStyle.top, leaf.resolvedStyle.right, leaf.resolvedStyle.bottom, leaf.resolvedStyle.left),
                Is.EqualTo((16f, 16f, 16f, 16f)));
        }

        // --- B-5: extended spacing/sizing scale + border-8 ---

        [Test]
        public void Given_P96Class_When_Resolved_Then_PaddingTopIs384()
        {
            // Arrange/Act — --space-96 == 384px (the new top of the scale).
            var leaf = MountAndResolve("p-96");

            // Assert
            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(384f));
        }

        [Test]
        public void Given_W44Class_When_Resolved_Then_WidthIs176()
        {
            // Arrange/Act — 44 is net-new on the curve (it sits between 40 and 48); --space-44 == 176px.
            var leaf = MountAndResolve("w-44");

            // Assert
            Assert.That(leaf.resolvedStyle.width, Is.EqualTo(176f));
        }

        [Test]
        public void Given_P3Point5Class_When_Resolved_Then_PaddingTopIs14()
        {
            // Arrange/Act — the only missing half-step; p-3.5 (dash form p-3-5) == 14px.
            var leaf = MountAndResolve("p-3-5");

            // Assert
            Assert.That(leaf.resolvedStyle.paddingTop, Is.EqualTo(14f));
        }

        [Test]
        public void Given_Border8Class_When_Resolved_Then_BorderTopWidthIs8()
        {
            // Arrange/Act — border-8 (Velvet previously stopped at border-4).
            var leaf = MountAndResolve("border-8");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopWidth, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Basis44Class_When_Resolved_Then_FlexBasisIs176()
        {
            // Arrange/Act — basis-* derives from the same --space-* scale extended here; --space-44 == 176px.
            var leaf = MountAndResolve("basis-44");

            // Assert
            Assert.That(leaf.resolvedStyle.flexBasis.value, Is.EqualTo(176f));
        }

        [Test]
        public void Given_Gap44Class_When_Parsed_Then_GapResolvesTo176()
        {
            // Arrange/Act — gap-*/space-* resolve through StyleGapClass (the C# mirror of the --space-* scale),
            // which was extended alongside the USS tokens; 44 == 176px.
            var ok = StyleGapClass.TryParse("gap-44", out var gap, out _);

            // Assert
            Assert.That((ok, gap), Is.EqualTo((true, 176f)));
        }

        [Test]
        public void Given_ColorOpacityModifierClass_When_Mounted_Then_BackgroundResolvesWithAlpha()
        {
            // Arrange/Act — bg-red-500/50 carries no '[', so this also proves the reconciler routes the
            // opacity-modifier form to the inline resolver instead of the (non-matching) USS class list.
            var leaf = MountAndResolve("bg-red-500/50");

            // Assert
            Assert.That(leaf.resolvedStyle.backgroundColor.a, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Given_NegativeStaticMargin_When_Mounted_Then_AppliesNegativeInlineMargin()
        {
            // Arrange/Act — -mt-2 has no USS class (selectors can't start with '-'), so this proves the
            // reconciler routes it to the inline resolver. --space-2 == 8px, negated.
            var leaf = MountAndResolve("-mt-2");

            // Assert
            Assert.That(leaf.resolvedStyle.marginTop, Is.EqualTo(-8f));
        }

        [Test]
        public void Given_MtAutoChildInColumn_When_Resolved_Then_ChildIsPushedToTheBottom()
        {
            // mt-auto puts ALL free space on top: a 100px child in a 300px column is pushed fully down, so its
            // laid-out y offset is 200px. (resolvedStyle.marginTop reports an auto margin as 0, so — like the
            // mx-auto / my-auto cases above — the layout offset is the real probe.)
            var leaf = MountChildAndResolve("flex flex-col w-[100px] h-[300px]", "w-[100px] h-[100px] mt-auto");

            Assert.That(leaf.layout.y, Is.EqualTo(200f).Within(0.5f));
        }

        [Test]
        public void Given_TextStartClass_When_Resolved_Then_AlignsToTheLeftEdge()
        {
            // Logical text-start maps to the LTR physical left edge (UI Toolkit has no writing-direction).
            var leaf = MountAndResolve("text-start");

            Assert.That(leaf.resolvedStyle.unityTextAlign, Is.EqualTo(TextAnchor.MiddleLeft));
        }

        [Test]
        public void Given_TransitionOpacityClass_When_Resolved_Then_TransitionsOpacityOnly()
        {
            var leaf = MountAndResolve("transition-opacity");
            var properties = leaf.resolvedStyle.transitionProperty.Select(p => p.ToString()).ToArray();

            Assert.That(properties, Is.EqualTo(new[] { "opacity" }));
        }

        [Test]
        public void Given_ArbitraryDurationMs_When_Resolved_Then_AppliesTransitionDurationInSeconds()
        {
            // duration-[400ms] carries a TIME value (not a length) and resolves to a 0.4s transition-duration.
            var leaf = MountAndResolve("duration-[400ms]");

            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.EqualTo(0.4f).Within(1e-5f));
        }
    }
}
