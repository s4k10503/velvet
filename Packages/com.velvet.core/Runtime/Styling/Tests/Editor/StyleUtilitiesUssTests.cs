using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Resolved-style coverage for USS-only utility presets that have no C# parse path, spanning three
    /// concerns that share the same harness (a leaf mounted in a real
    /// <see cref="EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c> attached, a forced layout
    /// pass, then a <c>resolvedStyle</c> read):
    /// <list type="bullet">
    /// <item>The Tailwind-parity batch: the <c>mx-auto</c>/<c>my-auto</c> centering margins
    /// (<c>_spacing.uss</c>), the <c>white</c>/<c>black</c> color classes
    /// (<c>_typography.uss</c>/<c>_backgrounds.uss</c>/<c>_borders.uss</c>), the numeric <c>duration-{ms}</c>
    /// scale (<c>_state_variants.uss</c>), the static position scale (<c>_layout.uss</c>), and the extended
    /// spacing/sizing scale plus <c>border-8</c> (<c>_spacing.uss</c>/<c>_sizing.uss</c>/<c>_borders.uss</c>);
    /// the font-size scale in <c>_tokens.uss</c> (the FRAMEWORK default with only the bundled stylesheet
    /// attached — the demo pins its own larger scale separately); the border-radius scale values in
    /// <c>_tokens.uss</c>; the spacing-scale steps 11 (2.75rem = 44px) and 28 (7rem = 112px), the only two
    /// standard steps missing across every family, verified at all three layers that consume the scale (the
    /// bundled USS, the arbitrary-value resolver's static-scale dict, and the gap polyfill's scale dict); and
    /// the tracking-* (letter-spacing) scale, pinned to Tailwind's em values baked at the 16px root font size
    /// (UI Toolkit letter-spacing has no em unit, so the scale is fixed px, exact at the default text
    /// size).</item>
    /// <item>The reverse flex utilities (<c>_layout.uss</c>), the larger font sizes (<c>_typography.uss</c>),
    /// the <c>size-*</c> width+height shorthand and <c>basis-*</c> flex-basis presets (<c>_sizing.uss</c> /
    /// <c>_layout.uss</c>), and the <c>origin-*</c> transform-origin utilities (<c>_transforms.uss</c>); the
    /// object-fit utilities (<c>_effects.uss</c>), mapped onto the modern <c>background-size</c> property for
    /// an element showing an image as background-image; the plain transform utilities
    /// (<c>_transforms.uss</c>) and the <c>.transition-transform</c> fix (<c>_effects.uss</c>) — UITK 6.x
    /// cannot transition the combined <c>transform</c>, so the animatable transform is the independent
    /// <c>translate</c> / <c>scale</c> / <c>rotate</c> properties, which is what <c>.transition-transform</c>
    /// must enumerate; the whitespace-pre / whitespace-pre-wrap utilities (<c>_typography.uss</c>, the two
    /// whitespace-* values that map straight onto a <see cref="WhiteSpace"/> enum member — whitespace-pre-line
    /// is NOT covered here, since it has no USS rule of its own); and the default-direction contract for the
    /// bare <c>flex</c> utility — in CSS, <c>flex</c> implies <c>flex-direction: row</c>, but UI Toolkit's Yoga
    /// layout defaults a flex container to <c>column</c>, so a bare <c>flex</c> must resolve to Row to lay
    /// children out HORIZONTALLY.</item>
    /// <item>That the transition-* property utilities work standalone, like their Tailwind counterparts: each
    /// bundles a default transition-duration and timing-function alongside its transition-property. UI
    /// Toolkit's initial transition-duration is 0s, so a property-only utility (e.g. <c>transition-opacity</c>
    /// plus a hover-driven value change) never visibly animated until the developer also added a duration-*
    /// class — while the sibling <c>transition-transform</c> already bundled its own duration, leaving one of
    /// six utilities functional standalone. Explicit duration-*/ease-* classes still override (declared later
    /// in the same sheet). These assertions read only <c>transitionDuration</c> /
    /// <c>transitionTimingFunction</c>, never layout geometry, so they are unaffected by the shared 600x600
    /// window size below.</item>
    /// </list>
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleUtilitiesUssTests : PanelTestBase
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

        private VisualElement MountChildAndResolve(string parentClassName, string childClassName)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: parentClassName, V.Div(name: "leaf", className: childClassName)));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        private VisualElement MountLabelAndResolve(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Label(name: "leaf", className: className, text: "x"));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        private VisualElement MountLeaf(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "leaf", className: className));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        // --- Tailwind-parity scale batch ---

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
            // Arrange/Act — border-8 is the widest step in Velvet's border-width scale.
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

        [Test]
        public void Given_TextLgClass_When_Resolved_Then_FontSizeIs18()
        {
            // Arrange / Act — text-lg == 1.125rem == 18px.
            var leaf = MountLabelAndResolve("text-lg");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(18f));
        }

        [Test]
        public void Given_Text2xlClass_When_Resolved_Then_FontSizeIs24()
        {
            // Arrange / Act — text-2xl == 1.5rem == 24px.
            var leaf = MountLabelAndResolve("text-2xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(24f));
        }

        [Test]
        public void Given_Text4xlClass_When_Resolved_Then_FontSizeIs36()
        {
            // Arrange / Act — text-4xl == 2.25rem == 36px.
            var leaf = MountLabelAndResolve("text-4xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(36f));
        }

        [Test]
        public void Given_TextXsClass_When_Resolved_Then_FontSizeIs12()
        {
            // Arrange / Act — text-xs == 0.75rem == 12px.
            var leaf = MountLabelAndResolve("text-xs");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(12f));
        }

        [Test]
        public void Given_RoundedLgClass_When_Resolved_Then_BorderRadiusIs8()
        {
            // Arrange/Act — rounded-lg == 0.5rem == 8px.
            var leaf = MountAndResolve("rounded-lg");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Rounded3xlClass_When_Resolved_Then_BorderRadiusIs24()
        {
            // Arrange/Act — rounded-3xl == 1.5rem == 24px.
            var leaf = MountAndResolve("rounded-3xl");

            // Assert
            Assert.That(leaf.resolvedStyle.borderTopLeftRadius, Is.EqualTo(24f));
        }

        [Test]
        public void Given_BareRoundedClass_When_Resolved_Then_BorderRadiusIs4()
        {
            // Arrange/Act — the bare `rounded` DEFAULT resolves to 0.25rem == 4px.
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

        [Test]
        public void Given_TrackingWidest_When_Resolved_Then_ItIsPointOneEmAtTheSixteenPxRoot()
        {
            // Tailwind tracking-widest is 0.1em; at the 16px root that is 1.6px.
            var leaf = MountLeaf("tracking-widest");

            Assert.That(leaf.resolvedStyle.letterSpacing, Is.EqualTo(1.6f).Within(1e-3f));
        }

        [Test]
        public void Given_TrackingTight_When_Resolved_Then_ItIsNegativePointZeroTwoFiveEm()
        {
            // Tailwind tracking-tight is -0.025em; at the 16px root that is -0.4px.
            var leaf = MountLeaf("tracking-tight");

            Assert.That(leaf.resolvedStyle.letterSpacing, Is.EqualTo(-0.4f).Within(1e-3f));
        }

        // --- Sizing / flex / transform USS-only presets ---

        [Test]
        public void Given_FlexRowReverseClass_When_Resolved_Then_SetsRowReverseDirection()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-row-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse));
        }

        [Test]
        public void Given_FlexColReverseClass_When_Resolved_Then_SetsColumnReverseDirection()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-col-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.ColumnReverse));
        }

        [Test]
        public void Given_FlexWrapReverseClass_When_Resolved_Then_SetsWrapReverse()
        {
            // Arrange/Act
            var leaf = MountAndResolve("flex flex-wrap-reverse");

            // Assert
            Assert.That(leaf.resolvedStyle.flexWrap, Is.EqualTo(Wrap.WrapReverse));
        }

        [Test]
        public void Given_Text5xlClass_When_Resolved_Then_FontSizeIs48()
        {
            // Arrange/Act — Velvet's token (48px), already referenced by sample screens.
            var leaf = MountAndResolve("text-5xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(48f));
        }

        [Test]
        public void Given_Text7xlClass_When_Resolved_Then_FontSizeIs72()
        {
            // Arrange/Act
            var leaf = MountAndResolve("text-7xl");

            // Assert
            Assert.That(leaf.resolvedStyle.fontSize, Is.EqualTo(72f));
        }

        [Test]
        public void Given_Size8Class_When_Resolved_Then_WidthAndHeightAre32()
        {
            // Arrange/Act — --space-8 == 32px; size-* writes both axes.
            var leaf = MountAndResolve("size-8");

            // Assert
            Assert.That((leaf.resolvedStyle.width, leaf.resolvedStyle.height), Is.EqualTo((32f, 32f)));
        }

        [Test]
        public void Given_Basis24Class_When_Resolved_Then_FlexBasisIs96()
        {
            // Arrange/Act — --space-24 == 96px.
            var leaf = MountAndResolve("basis-24");

            // Assert
            Assert.That(leaf.resolvedStyle.flexBasis.value, Is.EqualTo(96f));
        }

        [Test]
        public void Given_BasisPxClass_When_Resolved_Then_FlexBasisIs1px()
        {
            // Arrange/Act — --space-px == 1px (off the larger Velvet token curve).
            var leaf = MountAndResolve("basis-px");

            // Assert
            Assert.That(leaf.resolvedStyle.flexBasis.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_SizeHalfFractionClass_When_Resolved_Then_WidthIsHalfTheParent()
        {
            // Arrange — a 200px parent so the 50% fraction resolves to a stable 100px.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "w-[200px] h-[200px]",
                    V.Div(name: "leaf", className: "size-1/2")));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);

            // Assert — size-1/2 sets both axes to 50% (= 100px of the 200px parent).
            Assert.That((leaf.resolvedStyle.width, leaf.resolvedStyle.height), Is.EqualTo((100f, 100f)));
        }

        [Test]
        public void Given_HeightTwoThirdsFractionClass_When_Resolved_Then_HeightIsTwoThirdsOfParent()
        {
            // Arrange — a 300px-tall parent so the single-axis h-2/3 fraction resolves to a stable 200px.
            // (size-1/2 above proves the Size fan-out path; this pins the standalone Height setter + Percent.)
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: "w-[300px] h-[300px]",
                    V.Div(name: "leaf", className: "h-2/3")));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);

            // Assert — h-2/3 == 66.667% of 300px == 200px.
            Assert.That(leaf.resolvedStyle.height, Is.EqualTo(200f).Within(0.5f));
        }

        [Test]
        public void Given_OriginTopRightClass_When_Resolved_Then_TransformOriginIsAtTheTopEdge()
        {
            // Arrange/Act — origin-top-right -> `right top`; the y component resolves to the top edge (0),
            // which is height-independent and reliably readable.
            var leaf = MountAndResolve("size-8 origin-top-right");

            // Assert
            Assert.That(leaf.resolvedStyle.transformOrigin.y, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AspectVideoClass_When_Resolved_Then_AspectRatioIs16By9()
        {
            // Arrange/Act — the USS-only aspect-video preset (16/9, stored as the reduced decimal).
            var leaf = MountAndResolve("aspect-video");

            // Assert
            Assert.That(leaf.resolvedStyle.aspectRatio.value, Is.EqualTo(16f / 9f).Within(0.001f));
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

        [Test]
        public void Given_BareFlexClass_When_StylesResolved_Then_FlexDirectionIsRow()
        {
            var host = _window.rootVisualElement;

            _mounted = V.Mount(host, V.Div(
                "flex",
                V.Div("a"),
                V.Div("b")));

            using var rowProbe = V.Mount(host, V.Div("flex flex-row"));

            // EditMode batchmode never ticks the panel's update phases, so resolvedStyle stays at
            // engine defaults until styling is applied explicitly. Force the style pass.
            ForcePanelUpdate(host.panel);

            // V.Mount renders the tree as children of the host; the "flex" div is host[0],
            // the flex-row probe is host[1].
            var flex = host[0];
            Assert.That(flex.ClassListContains("flex"), Is.True,
                "Expected the mounted element to carry the 'flex' class.");

            // Guard: prove StyleUtilities.uss actually resolves against this panel — `flex-row`
            // must yield Row. Column is also Yoga's default, so without this guard a missing
            // sheet would be indistinguishable from a missing `flex-direction` on `.flex`.
            Assert.That(host[1].resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "StyleUtilities.uss did not resolve against the test panel (flex-row should be Row).");

            Assert.That(flex.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row),
                "Tailwind parity: a bare `flex` must resolve to flex-direction: row (horizontal), " +
                "not Yoga's default column.");
        }

        // --- Transition-* default duration/timing-function ---
        //
        // Pins that the transition-* property utilities work standalone, like their Tailwind counterparts:
        // each bundles a default transition-duration and timing-function alongside its transition-property.
        // These assertions read only transitionDuration / transitionTimingFunction — size-independent, so the
        // 600x600 WindowSize shared with the sections above is inert for them.

        [Test]
        public void Given_TransitionOpacityAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act — the utility stands alone, with no duration-* class.
            var leaf = MountLeaf("transition-opacity");

            // Assert — the class animates by itself instead of resolving to the 0s initial value.
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionColorsAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act
            var leaf = MountLeaf("transition-colors");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionAllAlone_When_Resolved_Then_ItCarriesANonZeroDefaultDuration()
        {
            // Arrange / Act
            var leaf = MountLeaf("transition-all");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.GreaterThan(0f));
        }

        [Test]
        public void Given_TransitionOpacityWithExplicitDurationZero_When_Resolved_Then_TheExplicitClassWins()
        {
            // Arrange / Act — duration-* is declared after the transition-* utilities, so an
            // explicit opt-out still overrides the bundled default.
            var leaf = MountLeaf("transition-opacity duration-0");

            // Assert
            Assert.That(leaf.resolvedStyle.transitionDuration.First().value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_TransitionColorsAlone_When_Resolved_Then_ItsDefaultCurveEasesInAndOut()
        {
            // Tailwind's default transition timing is cubic-bezier(0.4, 0, 0.2, 1); UI Toolkit has no
            // cubic-bezier, so the bundled default is its closest keyword, ease-in-out (not fast-start ease-out).
            var leaf = MountLeaf("transition-colors");

            Assert.That(leaf.resolvedStyle.transitionTimingFunction.First().mode, Is.EqualTo(EasingMode.EaseInOut));
        }

        [Test]
        public void Given_AnExplicitEaseClass_When_Resolved_Then_ItStillOverridesTheDefaultCurve()
        {
            // The .ease-* utilities are declared after the transition-* defaults, so an explicit curve wins.
            var leaf = MountLeaf("transition-colors ease-linear");

            Assert.That(leaf.resolvedStyle.transitionTimingFunction.First().mode, Is.EqualTo(EasingMode.Linear));
        }
    }
}
