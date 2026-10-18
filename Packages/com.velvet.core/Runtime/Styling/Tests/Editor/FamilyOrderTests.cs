using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the order the base tokens of a family that a C# resolver folds from the whole class list are put in:
    /// the order Tailwind emits them, so the token the fold ends on is the one Tailwind emits last whichever is
    /// written first. Tokens of another family, variant tokens and tokens of the other importance keep their places.
    /// A slash text size is ordered with the plain inline font size beside it, and the nine-slice insets, which
    /// Tailwind has no utility for, follow the sort key.
    /// </summary>
    [TestFixture]
    internal sealed class FamilyOrderTests
    {
        private static VisualElement MountOne(Reconciler reconciler, VisualElement root, string className)
        {
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(className) });
            return root.ElementAt(0);
        }

        // One or more rows for the font facets, the text effects (transform, decoration, wrapping, leading), the sizes
        // beside them, z-index, gap and space, grid columns, divide, shadow, ring, clip-path, skew, the gradient
        // stops, pointer events, the background image, the border line style, the animate-* modes and the nine-slice
        // insets.
        [TestCase("font-light font-bold", "font-bold font-light")]
        [TestCase("not-italic italic", "italic not-italic")]
        [TestCase("font-[700] font-[600]", "font-[600] font-[700]")]
        [TestCase("font-bold font-[700]", "font-[700] font-bold")]
        [TestCase("font-sans font-mono", "font-mono font-sans")]
        [TestCase("flex font-light font-bold", "flex font-bold font-light")]
        [TestCase("!font-light !font-bold", "!font-bold !font-light")]
        [TestCase("font-light !font-bold font-bold", "font-bold !font-bold font-light")]
        [TestCase("font-light hover:font-bold font-bold", "font-bold hover:font-bold font-light")]
        [TestCase("font-light bold-italic", "bold-italic font-light")]
        [TestCase("uppercase lowercase", "lowercase uppercase")]
        [TestCase("underline line-through", "line-through underline")]
        [TestCase("text-wrap text-nowrap", "text-nowrap text-wrap")]
        [TestCase("leading-6 leading-4", "leading-4 leading-6")]
        [TestCase("text-[16px] text-[14px]/6", "text-[14px]/6 text-[16px]")]
        [TestCase("text-[14px] text-[16px]/6", "text-[16px]/6 text-[14px]")]
        [TestCase("text-[16px] text-sm/6", "text-sm/6 text-[16px]")]
        [TestCase("text-sm/6 text-lg/7", "text-lg/7 text-sm/6")]
        [TestCase("text-[16px]/6 text-[14px]/6", "text-[14px]/6 text-[16px]/6")]
        [TestCase("!text-[16px] !text-[14px]/6", "!text-[14px]/6 !text-[16px]")]
        [TestCase("flex text-[16px] text-[14px]/6 p-2", "flex text-[14px]/6 text-[16px] p-2")]
        [TestCase("z-20 z-10", "z-10 z-20")]
        [TestCase("-z-20 -z-10", "-z-10 -z-20")]
        [TestCase("w-[2px] h-[1px] z-20 z-10", "w-[2px] h-[1px] z-10 z-20")]
        [TestCase("gap-x-4 gap-2", "gap-2 gap-x-4")]
        [TestCase("gap-y-4 gap-x-2", "gap-x-2 gap-y-4")]
        [TestCase("space-y-2 space-x-4", "space-x-4 space-y-2")]
        [TestCase("grid-cols-3 grid-cols-2", "grid-cols-2 grid-cols-3")]
        [TestCase("grid-cols-3 grid", "grid grid-cols-3")]
        [TestCase("divide-y-2 divide-x-2", "divide-x-2 divide-y-2")]
        [TestCase("shadow-sm shadow-lg", "shadow-lg shadow-sm")]
        [TestCase("ring-4 ring-2", "ring-2 ring-4")]
        [TestCase("clip-path-none clip-path-[inset(0)]", "clip-path-[inset(0)] clip-path-none")]
        [TestCase("skew-y-3 skew-x-3", "skew-x-3 skew-y-3")]
        [TestCase("to-red-500 from-blue-500", "from-blue-500 to-red-500")]
        [TestCase("pointer-events-none pointer-events-auto", "pointer-events-auto pointer-events-none")]
        [TestCase("bg-linear-[45deg] bg-linear-to-r", "bg-linear-to-r bg-linear-[45deg]")]
        [TestCase("bg-conic bg-linear-to-r", "bg-linear-to-r bg-conic")]
        [TestCase("bg-conic-90 bg-linear-90", "bg-linear-90 bg-conic-90")]
        [TestCase("bg-gradient-to-r bg-linear-to-l", "bg-linear-to-l bg-gradient-to-r")]
        [TestCase("!bg-conic !bg-linear-90", "!bg-linear-90 !bg-conic")]
        [TestCase("bg-conic from-red-500", "from-red-500 bg-conic")]
        [TestCase("bg-conic via-green-500", "via-green-500 bg-conic")]
        [TestCase("bg-conic to-red-500", "to-red-500 bg-conic")]
        [TestCase("bg-linear-to-r -bg-linear-45", "-bg-linear-45 bg-linear-to-r")]
        [TestCase("outline-2 ring-0", "ring-0 outline-2")]
        [TestCase("!outline-2 !ring-0", "!ring-0 !outline-2")]
        [TestCase("bg-[addr:b] bg-[addr:a]", "bg-[addr:a] bg-[addr:b]")]
        [TestCase("border-solid border-dashed", "border-dashed border-solid")]
        [TestCase("animate-spin animate-pulse", "animate-pulse animate-spin")]
        [TestCase("slice-t-[2] slice-y-[8]", "slice-y-[8] slice-t-[2]")]
        [TestCase("slice-l-[2] slice-[6] slice-y-[8]", "slice-[6] slice-y-[8] slice-l-[2]")]
        [TestCase("!slice-t-[2] !slice-y-[8]", "!slice-y-[8] !slice-t-[2]")]
        [TestCase("slice-t-[2] slice-t-[3]", "slice-t-[3]")]
        public void Given_TwoTokensOfAFamilyAResolverFolds_When_ClassNamesAreParsed_Then_TheyAreInTheOrderTailwindEmitsThem(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base leaves every token of these lists as written.
        // Each is already in Tailwind's order, or ranked by another layer (a variant, the other importance), or in
        // a family no resolver folds, so putting every token of a prefix in one order reddens it.
        [TestCase("font-bold font-light")]
        [TestCase("italic not-italic")]
        [TestCase("font-[700] font-bold")]
        [TestCase("font-light !font-bold")]
        [TestCase("hover:font-light hover:font-bold")]
        [TestCase("-z-10 z-10")]
        [TestCase("border-dashed border-solid")]
        [TestCase("animate-hue animate-none")]
        [TestCase("text-[14px]/6 text-[16px]")]
        [TestCase("text-sm/6 text-[16px]")]
        [TestCase("text-lg/7 text-sm/6")]
        [TestCase("text-[14px]/6 text-[16px]/6")]
        [TestCase("bg-linear-to-r bg-linear-[45deg]")]
        [TestCase("bg-linear-to-r bg-conic")]
        [TestCase("-bg-linear-45 bg-conic")]
        [TestCase("bg-linear-90 bg-conic-90")]
        [TestCase("bg-linear-to-l bg-gradient-to-r")]
        [TestCase("shadow-lg drop-shadow-sm")]
        [TestCase("ring-0 outline-2")]
        [TestCase("slice-y-[8] slice-t-[2]")]
        [TestCase("slice-t-[2] !slice-y-[8]")]
        [TestCase("w-[2px] h-[1px] bg-[#ff0000]")]
        public void Given_TokensAlreadyInTheOrderTailwindEmitsThem_When_ClassNamesAreParsed_Then_TheyAreAsWritten(
            string className)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(className));
        }

        [Test]
        public void Given_ADropShadowWrittenBeforeABoxShadow_When_ClassNamesAreParsed_Then_TheyAreAsWritten()
        {
            // Act
            var tokens = V.ParseClassNames("drop-shadow-sm shadow-lg");

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo("drop-shadow-sm shadow-lg"));
        }

        // One row for each reader that folds the live class list: the clip shape, and the layout gates LiveClasses
        // hands to (pointer events, gap, grid columns, divide). A token a later render adds sorts ahead of the ones
        // already there in each, which the diff appends it behind; the expected list is the order Tailwind emits them in.
        [TestCase("pointer-events-none", "pointer-events-none pointer-events-auto", "pointer-events-auto pointer-events-none")]
        [TestCase("clip-path-none", "clip-path-none clip-path-[inset(10%)]", "clip-path-[inset(10%)] clip-path-none")]
        [TestCase("clip-path-[inset(10%)]", "clip-path-[inset(10%)] clip-path-[circle(40%)]", "clip-path-[circle(40%)] clip-path-[inset(10%)]")]
        [TestCase("gap-x-4", "gap-x-4 gap-2", "gap-2 gap-x-4")]
        [TestCase("grid-cols-3", "grid-cols-3 grid-cols-2", "grid-cols-2 grid-cols-3")]
        [TestCase("divide-y-2", "divide-y-2 divide-x-2", "divide-x-2 divide-y-2")]
        public void Given_ATokenAddedByALaterRenderThatSortsAheadOfTheOnesThere_When_Reconciled_Then_TheLiveClassListIsInTheOrderAFreshMountGives(
            string first, string second, string expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var firstTree = new VNode[] { V.Div(name: "t", className: first) };
            var secondTree = new VNode[] { V.Div(name: "t", className: second) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), firstTree);

            // Act
            reconciler.Reconcile(root, firstTree, secondTree);

            // Assert
            var written = second.Split(' ');
            var live = root.Q<VisualElement>("t").GetClasses().Where(cls => written.Contains(cls));
            Assert.That(string.Join(" ", live), Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABracketClipAddedByALaterRenderBesideClipPathNone_When_TheLiveClipIsRead_Then_NoClipResolves()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var firstTree = new VNode[] { V.Div(name: "t", className: "clip-path-none") };
            var secondTree = new VNode[] { V.Div(name: "t", className: "clip-path-none clip-path-[inset(10%)]") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), firstTree);

            // Act
            reconciler.Reconcile(root, firstTree, secondTree);

            // Assert
            Assert.That(StyleClipPathClass.TryExtractLive(root.Q<VisualElement>("t"), out _), Is.False);
        }

        [Test]
        public void Given_AVariantPayloadOnTheLiveListBesideAClipAddedByALaterRender_When_Reconciled_Then_ThePayloadStaysLast()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var firstTree = new VNode[] { V.Div(name: "t", className: "clip-path-[inset(10%)]") };
            var secondTree = new VNode[] { V.Div(name: "t", className: "clip-path-[inset(10%)] clip-path-[circle(40%)]") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), firstTree);
            // What a lit hover:clip-path-none payload leaves on the live list: the bare token, appended after the base.
            root.Q<VisualElement>("t").AddToClassList("clip-path-none");

            // Act
            reconciler.Reconcile(root, firstTree, secondTree);

            // Assert
            Assert.That(root.Q<VisualElement>("t").GetClasses().Last(cls => cls.StartsWith("clip-path-")),
                Is.EqualTo("clip-path-none"));
        }

        [TestCase("font-light font-bold", VelvetFontWeight.Light)]
        [TestCase("font-[700] font-[600]", (VelvetFontWeight)700)]
        [TestCase("font-light font-[700]", VelvetFontWeight.Light)]
        public void Given_TwoFontWeights_When_TheFontIsExtracted_Then_TheOneTailwindEmitsLastHoldsTheWeight(
            string className, VelvetFontWeight expected)
        {
            // Act
            StyleFontClass.TryExtract(V.ParseClassNames(className), out var intent);

            // Assert
            Assert.That(intent.Weight, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ItalicAndNotItalicWrittenNotItalicFirst_When_TheFontIsExtracted_Then_NotItalicHolds()
        {
            // Act
            StyleFontClass.TryExtract(V.ParseClassNames("not-italic italic"), out var intent);

            // Assert
            Assert.That((intent.HasItalic, intent.Italic), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoFontFamilies_When_TheFontIsExtracted_Then_TheOneTailwindEmitsLastHoldsTheFamily()
        {
            // Act
            StyleFontClass.TryExtract(V.ParseClassNames("font-sans font-mono"), out var intent);

            // Assert
            Assert.That(intent.Family, Is.EqualTo("sans"));
        }

        [Test]
        public void Given_LowercaseWrittenAfterUppercase_When_TheTextEffectIsParsed_Then_UppercaseHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("uppercase lowercase"));

            // Assert
            Assert.That(effect.Transform, Is.EqualTo(TextTransformKind.Upper));
        }

        // GREEN_ON_BASE(characterization): the base lets the class written last hold the transform, which here is uppercase.
        [Test]
        public void Given_UppercaseWrittenAfterLowercase_When_TheTextEffectIsParsed_Then_UppercaseHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("lowercase uppercase"));

            // Assert
            Assert.That(effect.Transform, Is.EqualTo(TextTransformKind.Upper));
        }

        // GREEN_ON_BASE(characterization): the base lets the class written last hold the decoration, which here is underline.
        [Test]
        public void Given_UnderlineWrittenAfterOverline_When_TheTextEffectIsParsed_Then_UnderlineHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("overline underline"));

            // Assert
            Assert.That(effect.Decoration, Is.EqualTo(TextDecorationKind.Underline));
        }

        [Test]
        public void Given_OverlineWrittenAfterUnderline_When_TheTextEffectIsParsed_Then_UnderlineHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("underline overline"));

            // Assert
            Assert.That(effect.Decoration, Is.EqualTo(TextDecorationKind.Underline));
        }

        // GREEN_ON_BASE(characterization): the base lets the class written last hold the decoration, which here is no-underline.
        [Test]
        public void Given_NoUnderlineWrittenAfterLineThrough_When_TheTextEffectIsParsed_Then_NoUnderlineHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("line-through no-underline"));

            // Assert
            Assert.That(effect.Decoration, Is.EqualTo(TextDecorationKind.None));
        }

        // GREEN_ON_BASE(characterization): the base already sorts no-underline after line-through, so the reset holds when line-through is written last.
        [Test]
        public void Given_LineThroughWrittenAfterNoUnderline_When_TheTextEffectIsParsed_Then_NoUnderlineHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("no-underline line-through"));

            // Assert
            Assert.That(effect.Decoration, Is.EqualTo(TextDecorationKind.None));
        }

        [Test]
        public void Given_TwoLeadings_When_TheTextEffectIsParsed_Then_TheOneTailwindEmitsLastHolds()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames("leading-6 leading-4"));

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Pixel, 24f)));
        }

        [Test]
        public void Given_TwoZIndexes_When_TheStackingIsExtracted_Then_TheOneTailwindEmitsLastHolds()
        {
            // Act
            StyleZIndexClass.TryExtract(V.ParseClassNames("z-20 z-10"), out var z);

            // Assert
            Assert.That(z, Is.EqualTo(20));
        }

        [Test]
        public void Given_TwoGridColumnCounts_When_TheGridIsExtracted_Then_TheOneTailwindEmitsLastHolds()
        {
            // Act
            StyleGridClass.TryExtract(V.ParseClassNames("grid-cols-3 grid-cols-2"), out var columns);

            // Assert
            Assert.That(columns, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base reads a slash size's line height off the token wherever it sits.
        // Dropping the slash token beside a later plain size reddens it.
        [TestCase("text-[16px] text-[14px]/6")]
        [TestCase("text-[14px]/6 text-[16px]")]
        [TestCase("text-sm/6 text-[16px]")]
        public void Given_ASlashSizeBesideAPlainSize_When_TheTextEffectIsParsed_Then_TheLineHeightIsTheSlashTokens(
            string className)
        {
            // Act
            var effect = StyleTextEffectClass.Parse(V.ParseClassNames(className));

            // Assert
            Assert.That(effect.Leading, Is.EqualTo(new LeadingValue(LeadingUnit.Pixel, 24f)));
        }

        [TestCase("text-[16px] text-[14px]/6")]
        [TestCase("text-[16px] text-sm/6")]
        public void Given_APlainSizeWrittenBeforeASlashSize_When_Reconciled_Then_ThePlainSizeHoldsTheFontSize(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.fontSize.value.value, Is.EqualTo(16f));
        }

        // GREEN_ON_BASE(characterization): the base lets the size written last hold the font size, which here is the plain one.
        [TestCase("text-[14px]/6 text-[16px]")]
        [TestCase("text-sm/6 text-[16px]")]
        public void Given_APlainSizeWrittenAfterASlashSize_When_Reconciled_Then_ThePlainSizeHoldsTheFontSize(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.fontSize.value.value, Is.EqualTo(16f));
        }

        [Test]
        public void Given_ANarrowerSliceInsetWrittenBeforeAWiderOne_When_Reconciled_Then_TheNarrowerHoldsTheTop()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "slice-t-[2] slice-y-[8]");

            // Assert
            Assert.That((element.style.unitySliceTop.value, element.style.unitySliceBottom.value), Is.EqualTo((2, 8)));
        }

        // GREEN_ON_BASE(characterization): the base lets the inset written last hold the top, which here is the narrower one.
        [Test]
        public void Given_ANarrowerSliceInsetWrittenAfterAWiderOne_When_Reconciled_Then_TheNarrowerHoldsTheTop()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, "slice-y-[8] slice-t-[2]");

            // Assert
            Assert.That((element.style.unitySliceTop.value, element.style.unitySliceBottom.value), Is.EqualTo((2, 8)));
        }
    }
}
