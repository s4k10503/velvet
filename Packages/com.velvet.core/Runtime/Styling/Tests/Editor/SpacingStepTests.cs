using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind v4's bare spacing value: every utility on the spacing scale takes any non-negative
    /// multiple of 0.25 written in canonical decimal form (<c>p-13</c>, <c>gap-4.5</c>, <c>w-0.25</c>) and
    /// resolves it to that many 4px steps. A step the bundled stylesheets declare a rule for keeps its class;
    /// the others resolve inline, as the bracket forms do.
    /// </summary>
    [TestFixture]
    internal sealed class SpacingStepTests
    {
        [TestCase("13", 52f)]
        [TestCase("4.5", 18f)]
        [TestCase("0.25", 1f)]
        [TestCase("0.75", 3f)]
        [TestCase("17.75", 71f)]
        [TestCase("100", 400f)]
        [TestCase("1.5", 6f)]
        public void Given_ACanonicalMultipleOfAQuarter_When_ReadAsASpacingStep_Then_ItIsThatManyFourPixelSteps(
            string suffix, float expectedPx)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out var px);

            // Assert
            Assert.That((ok, px), Is.EqualTo((true, expectedPx)));
        }

        // GREEN_ON_BASE(characterization): the base declines every one of these suffixes, and so must this change.
        // Reading the suffix with float.TryParse and testing for a multiple of 0.25, instead of comparing its
        // spelling, accepts `.5`, `4.50`, `4.0`, `05` and `1e2` and reddens this.
        [TestCase(".5")]
        [TestCase("4.50")]
        [TestCase("4.0")]
        [TestCase("05")]
        [TestCase("00")]
        [TestCase("4.")]
        [TestCase("4.1")]
        [TestCase("0.125")]
        [TestCase("-1")]
        [TestCase("+1")]
        [TestCase("1e2")]
        [TestCase("1,5")]
        [TestCase("99999999999")]
        [TestCase("")]
        public void Given_ASuffixThatIsNotACanonicalMultipleOfAQuarter_When_ReadAsASpacingStep_Then_ItIsDeclined(
            string suffix)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        private static readonly object[] StepClassCases =
        {
            new object[] { "p-13", ArbitraryProperty.Padding, 52f },
            new object[] { "px-4.5", ArbitraryProperty.PaddingX, 18f },
            new object[] { "py-0.25", ArbitraryProperty.PaddingY, 1f },
            new object[] { "pt-17.75", ArbitraryProperty.PaddingTop, 71f },
            new object[] { "pr-13", ArbitraryProperty.PaddingRight, 52f },
            new object[] { "pb-13", ArbitraryProperty.PaddingBottom, 52f },
            new object[] { "pl-13", ArbitraryProperty.PaddingLeft, 52f },
            new object[] { "m-13", ArbitraryProperty.Margin, 52f },
            new object[] { "mx-13", ArbitraryProperty.MarginX, 52f },
            new object[] { "my-13", ArbitraryProperty.MarginY, 52f },
            new object[] { "mt-13", ArbitraryProperty.MarginTop, 52f },
            new object[] { "mr-13", ArbitraryProperty.MarginRight, 52f },
            new object[] { "mb-13", ArbitraryProperty.MarginBottom, 52f },
            new object[] { "ml-13", ArbitraryProperty.MarginLeft, 52f },
            new object[] { "-m-13", ArbitraryProperty.Margin, -52f },
            new object[] { "-mt-4.5", ArbitraryProperty.MarginTop, -18f },
            new object[] { "w-13", ArbitraryProperty.Width, 52f },
            new object[] { "h-13", ArbitraryProperty.Height, 52f },
            new object[] { "size-13", ArbitraryProperty.Size, 52f },
            new object[] { "min-w-13", ArbitraryProperty.MinWidth, 52f },
            new object[] { "min-h-13", ArbitraryProperty.MinHeight, 52f },
            new object[] { "max-w-13", ArbitraryProperty.MaxWidth, 52f },
            new object[] { "max-h-13", ArbitraryProperty.MaxHeight, 52f },
            new object[] { "basis-13", ArbitraryProperty.FlexBasis, 52f },
            new object[] { "top-13", ArbitraryProperty.Top, 52f },
            new object[] { "right-13", ArbitraryProperty.Right, 52f },
            new object[] { "bottom-13", ArbitraryProperty.Bottom, 52f },
            new object[] { "left-13", ArbitraryProperty.Left, 52f },
            new object[] { "inset-13", ArbitraryProperty.Inset, 52f },
            new object[] { "inset-x-13", ArbitraryProperty.InsetX, 52f },
            new object[] { "inset-y-13", ArbitraryProperty.InsetY, 52f },
            new object[] { "-top-4", ArbitraryProperty.Top, -16f },
            new object[] { "-left-13", ArbitraryProperty.Left, -52f },
            new object[] { "-inset-4.5", ArbitraryProperty.Inset, -18f },
            new object[] { "-inset-x-px", ArbitraryProperty.InsetX, -1f },
            new object[] { "-inset-y-2", ArbitraryProperty.InsetY, -8f },
            new object[] { "translate-x-13", ArbitraryProperty.TranslateX, 52f },
            new object[] { "translate-y-4.5", ArbitraryProperty.TranslateY, 18f },
            new object[] { "-translate-x-13", ArbitraryProperty.TranslateX, -52f },
            new object[] { "-translate-y-0.25", ArbitraryProperty.TranslateY, -1f },
            new object[] { "ms-13", ArbitraryProperty.MarginLeft, 52f },
            new object[] { "-me-4.5", ArbitraryProperty.MarginRight, -18f },
            new object[] { "ps-13", ArbitraryProperty.PaddingLeft, 52f },
            new object[] { "start-13", ArbitraryProperty.Left, 52f },
            new object[] { "inset-be-4.5", ArbitraryProperty.Bottom, 18f },
            new object[] { "p-30", ArbitraryProperty.Padding, 120f },
            new object[] { "w-19", ArbitraryProperty.Width, 76f },
        };

        private static string[] StepClassNames()
            => StepClassCases.Select(row => (string)((object[])row)[0]).ToArray();

        [TestCaseSource(nameof(StepClassCases))]
        public void Given_AStepClassNoStylesheetRuleDeclares_When_Parsed_Then_ResolvesThePropertyAtThatManyPixels(
            string className, object property, float expectedPx)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert
            Assert.That(
                (ok, style.Property, style.Value, style.Unit),
                Is.EqualTo((true, (ArbitraryProperty)property, expectedPx, LengthUnit.Pixel)));
        }

        [TestCaseSource(nameof(StepClassNames))]
        public void Given_AStepClassNoStylesheetRuleDeclares_When_DispatchGateChecked_Then_ItIsInlineResolved(
            string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.IsInlineResolved(className);

            // Assert
            Assert.That(claimed, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base declines each of these, an off-scale step and a utility Tailwind does not negate alike.
        // Reading the suffix with float.TryParse reddens `p-.5`, `p-4.50`, `p-05`, `w-1e2` and `top-4.`, accepting
        // any decimal reddens `mt-4.1`, and a true `Negatable` on every row of `s_spacingFamilies` reddens the
        // negated ones.
        [TestCase("p-.5")]
        [TestCase("p-4.50")]
        [TestCase("p-05")]
        [TestCase("mt-4.1")]
        [TestCase("w-1e2")]
        [TestCase("top-4.")]
        [TestCase("-p-13")]
        [TestCase("-px-4.5")]
        [TestCase("-w-13")]
        [TestCase("-size-13")]
        [TestCase("-basis-13")]
        public void Given_AClassTheScaleDoesNotTake_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.IsInlineResolved(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        [TestCase("-max-w-13")]
        [TestCase("-min-w-4")]
        [TestCase("-max-h-0.5")]
        [TestCase("-min-h-px")]
        public void Given_ANegatedMinOrMaxSize_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.IsInlineResolved(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        // GREEN_ON_BASE(characterization): a step the bundled stylesheets declare a rule for stays a class, as it is on the base.
        // Claiming it would move its cascade layer from class to inline; dropping the `SuffixInKeys` test in
        // `TryParseSpacingStep` does that and reddens this.
        [TestCase("p-4")]
        [TestCase("p-1-5")]
        [TestCase("w-96")]
        [TestCase("top-px")]
        [TestCase("inset-x-0")]
        [TestCase("basis-0-5")]
        public void Given_AStepWithAStylesheetRule_When_ParsedAndGated_Then_NeitherClaimsItAndItKeepsItsClass(
            string className)
        {
            // Act
            var claimed = StyleArbitraryValueResolver.IsInlineResolved(className);
            var parsed = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That((claimed, parsed), Is.EqualTo((false, false)));
        }

        [TestCase("gap-13", 52f, GapAxis.Auto)]
        [TestCase("gap-x-4.5", 18f, GapAxis.Horizontal)]
        [TestCase("gap-y-0.25", 1f, GapAxis.Vertical)]
        [TestCase("space-x-13", 52f, GapAxis.Horizontal)]
        [TestCase("-space-y-4.5", -18f, GapAxis.Vertical)]
        public void Given_AGapOrSpaceStepOffTheStylesheetScale_When_Parsed_Then_ResolvesThatManyPixels(
            string className, float expectedGap, GapAxis expectedAxis)
        {
            // Act
            var ok = StyleGapClass.TryParse(className, out var gap, out var axis);

            // Assert
            Assert.That((ok, gap, axis), Is.EqualTo((true, expectedGap, expectedAxis)));
        }

        // GREEN_ON_BASE(characterization): the base declines each of these suffixes too, and a suffix that is no canonical step stays declined.
        // Reading `4.50` as a step reddens it.
        [TestCase("gap-4.50")]
        [TestCase("gap-x-.5")]
        [TestCase("space-y-05")]
        [TestCase("gap-4.1")]
        public void Given_AGapOrSpaceSuffixThatIsNotACanonicalStep_When_Parsed_Then_ItIsDeclined(string className)
        {
            // Act
            var ok = StyleGapClass.TryParse(className, out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AStepClassNoStylesheetRuleDeclares_When_Reconciled_Then_WritesItInlineAndKeepsItOffTheClassList()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("p-13") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var element = root.ElementAt(0);
            Assert.That(
                (element.style.paddingTop.value.value, element.style.paddingLeft.value.value, element.ClassListContains("p-13")),
                Is.EqualTo((52f, 52f, false)));
        }

        [Test]
        public void Given_ANegatedPositionStep_When_Reconciled_Then_WritesTheNegativeOffsetInline()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("-top-4") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.top.value.value, Is.EqualTo(-16f));
        }

        [Test]
        public void Given_AStepClassRemoved_When_Reconciled_Then_ClearsTheInlineStyle()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("w-13") };
            var newTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        private static float Side(VisualElement element, string side) => side switch
        {
            "paddingTop" => element.style.paddingTop.value.value,
            "paddingLeft" => element.style.paddingLeft.value.value,
            "marginTop" => element.style.marginTop.value.value,
            "top" => element.style.top.value.value,
            "left" => element.style.left.value.value,
            "marginLeft" => element.style.marginLeft.value.value,
            "width" => element.style.width.value.value,
            _ => throw new System.ArgumentOutOfRangeException(nameof(side), side, null),
        };

        // Tailwind emits a shorthand ahead of the longhands it contains, so the longhand holds its side whichever
        // is written first and whichever resolves inline (a stylesheet class is rewritten to join an inline one).
        // A shorthand written after, or a stylesheet class beside an inline value, would hold 52 instead.
        [TestCase("p-13 pt-2", "paddingTop", 8f)]
        [TestCase("pt-2 p-13", "paddingTop", 8f)]
        [TestCase("pt-1.5 p-13", "paddingTop", 6f)]
        [TestCase("p-[52px] pt-2", "paddingTop", 8f)]
        [TestCase("pt-2 p-[52px]", "paddingTop", 8f)]
        [TestCase("m-13 mt-2", "marginTop", 8f)]
        [TestCase("inset-13 top-2", "top", 8f)]
        [TestCase("px-13 pl-2", "paddingLeft", 8f)]
        [TestCase("!p-13 !pt-2", "paddingTop", 8f)]
        [TestCase("!pt-2 !p-13", "paddingTop", 8f)]
        [TestCase("w-4 size-13", "width", 16f)]
        [TestCase("ps-13 pl-2", "paddingLeft", 8f)]
        [TestCase("pl-2 ps-13", "paddingLeft", 8f)]
        [TestCase("pl-[8px] ps-13", "paddingLeft", 8f)]
        [TestCase("ms-13 ml-2", "marginLeft", 8f)]
        [TestCase("start-13 left-2", "left", 8f)]
        [TestCase("p-13 ps-2", "paddingLeft", 8f)]
        [TestCase("ps-2 p-13", "paddingLeft", 8f)]
        public void Given_AShorthandAndALonghandOfOneFamily_When_Reconciled_Then_TheLonghandHoldsItsSide(
            string className, string side, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div(className) };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Side(root.ElementAt(0), side), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AShorthandStepAndAnotherSideOfItsLonghand_When_Reconciled_Then_TheShorthandKeepsTheRest()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("pt-2 p-13") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.paddingBottom.value.value, Is.EqualTo(52f));
        }

        [Test]
        public void Given_ALonghandRemovedBesideAnInlineShorthand_When_Reconciled_Then_TheShorthandReturnsToTheSide()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("p-13 pt-2") };
            var newTree = new VNode[] { V.Div("p-13") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.paddingTop.value.value, Is.EqualTo(52f));
        }

        [Test]
        public void Given_EqualLayerVariantPayloadsOfAShorthandStepAndALonghand_When_Enabled_Then_TheLonghandHoldsTheSide()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            StyleVariantPayload.Apply(element, new[] { "pt-[8px]", "p-13" }, true, StyleLayerPriority.Hover);

            // Assert
            Assert.That(element.style.paddingTop.value.value, Is.EqualTo(8f));
        }

        [TestCase("p-13 pt-2", "p-13 pt-[8px]")]
        [TestCase("pt-2 p-13", "pt-[8px] p-13")]
        [TestCase("hover:p-13 hover:pt-2", "hover:p-13 hover:pt-[8px]")]
        [TestCase("sm:hover:p-13 sm:hover:pt-2", "sm:hover:p-13 sm:hover:pt-[8px]")]
        [TestCase("!p-13 !pt-2", "!p-13 !pt-[8px]")]
        [TestCase("!p-13 pt-2!", "!p-13 pt-[8px]!")]
        [TestCase("px-13 pl-2", "px-13 pl-[8px]")]
        [TestCase("p-13 py-1.5 pt-2", "p-13 py-1.5 pt-[8px]")]
        [TestCase("m-13 mx-auto", "m-13 mx-[auto]")]
        [TestCase("m-[8px] mt-auto", "m-[8px] mt-[auto]")]
        [TestCase("size-13 w-full", "size-13 w-[100%]")]
        [TestCase("size-[40px] w-full", "size-[40px] w-[100%]")]
        [TestCase("h-full size-13", "h-[100%] size-13")]
        [TestCase("hover:focus:pt-2 focus:hover:p-13", "hover:focus:pt-[8px] focus:hover:p-13")]
        [TestCase("ps-13 pl-2", "pl-[8px]")]
        [TestCase("pl-2 ps-13", "pl-[8px]")]
        [TestCase("ps-13 pl-[8px] flex", "pl-[8px] flex")]
        [TestCase("size-13 w-4", "size-13 w-[16px]")]
        [TestCase("p-[52px] pt-2 flex", "p-[52px] pt-[8px] flex")]
        public void Given_AStylesheetClassBesideAnInlineShorthandOfItsFamily_When_ClassNamesAreParsed_Then_TheClassIsWrittenInline(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base rewrites no class, so each list below reaches the reconciler as written.
        // Each is a pair where leaving the class alone gives Tailwind's answer already or the two do not
        // contend, so a rewrite of every stylesheet class beside an inline one reddens it.
        [TestCase("p-4 pt-[2px]")]
        [TestCase("p-4 pt-13")]
        [TestCase("px-4 py-13")]
        [TestCase("p-13 p-4")]
        [TestCase("p-13 hover:pt-2")]
        [TestCase("!p-13 pt-2")]
        [TestCase("p-4 pt-2")]
        [TestCase("size-4 w-13")]
        [TestCase("hover:ps-13 hover:pl-2")]
        public void Given_AClassListThatNeedsNoRewrite_When_ClassNamesAreParsed_Then_TheyAreAsWritten(string className)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(className));
        }

        [Test]
        public void Given_AutoMarginOnASideBesideAnInlineMarginShorthand_When_Reconciled_Then_TheSideIsAuto()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("m-13 mx-auto") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_AutoMarginTopBesideABracketMarginShorthand_When_Reconciled_Then_TheTopIsAuto()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("m-[8px] mt-auto") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.marginTop.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [TestCase("size-13 w-full")]
        [TestCase("size-[40px] w-full")]
        public void Given_FullWidthBesideAnInlineSize_When_Reconciled_Then_TheWidthIsAllOfTheParent(string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div(className) };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var width = root.ElementAt(0).style.width.value;
            Assert.That((width.value, width.unit), Is.EqualTo((100f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_FullHeightWrittenBeforeAnInlineSize_When_Reconciled_Then_TheHeightIsAllOfTheParent()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("h-full size-13") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var height = root.ElementAt(0).style.height.value;
            Assert.That((height.value, height.unit), Is.EqualTo((100f, LengthUnit.Percent)));
        }

        [TestCase("mx-[auto]", ArbitraryProperty.MarginX)]
        [TestCase("w-[auto]", ArbitraryProperty.Width)]
        [TestCase("basis-[auto]", ArbitraryProperty.FlexBasis)]
        [TestCase("top-[auto]", ArbitraryProperty.Top)]
        [TestCase("size-[auto]", ArbitraryProperty.Size)]
        [TestCase("inset-[auto]", ArbitraryProperty.Inset)]
        public void Given_AnAutoBracketOnAnAutoFamily_When_Parsed_Then_ResolvesTheAutoKeyword(
            string className, object property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert
            Assert.That((ok, style.Property, style.Auto), Is.EqualTo((true, (ArbitraryProperty)property, true)));
        }

        // GREEN_ON_BASE(characterization): the base declines an auto bracket everywhere, and padding, a minimum and a maximum take none.
        // Dropping the padding test from `TakesAuto` accepts `p-[auto]`, and dropping its minimum and maximum test
        // accepts `max-w-[auto]`; either reddens this.
        [TestCase("p-[auto]")]
        [TestCase("pt-[auto]")]
        [TestCase("-mt-[auto]")]
        [TestCase("max-w-[auto]")]
        [TestCase("min-h-[auto]")]
        public void Given_AnAutoBracketOnPaddingMinMaxOrNegated_When_Parsed_Then_ItIsDeclined(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("pt-[8px]", "p-[52px]", 52f)]
        [TestCase("pt-[8px]", "p-13", 52f)]
        public void Given_ABaseLonghandAndAPoseShorthand_When_TheMotionMerges_Then_ThePoseHoldsTheTop(
            string className, string pose, float expected)
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = pose };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: className, variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): an inline pose shorthand already outranks a base class longhand, as the base has no rank.
        // Rewriting the base class inline too, as the narrower of a pair, would hand the top to its longhand and redden it.
        [Test]
        public void Given_AClassBaseLonghandAndAnInlinePoseShorthand_When_TheMotionMerges_Then_ThePoseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "p-13" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "pt-2", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(52f));
        }

        [TestCase("pt-[8px] flex", "p-[52px]", "flex")]
        [TestCase("pt-[8px] pb-[8px]", "py-[52px]", "")]
        [TestCase("ps-1", "p-13", "")]
        [TestCase("p-[52px]", "pt-[8px]", "p-[52px]")]
        [TestCase("pt-[8px]", "pt-[10px]", "")]
        [TestCase("pt-[8px]", "hover:p-[52px]", "pt-[8px]")]
        [TestCase("!pt-[8px]", "p-[52px]", "!pt-[8px]")]
        [TestCase("pt-[8px]", "flex", "pt-[8px]")]
        [TestCase("pt-2", "p-[52px]", "pt-2")]
        public void Given_BaseAndPoseClasses_When_TheBaseIsFilteredForThePose_Then_OnlyTheOverriddenSpacingGoes(
            string baseClasses, string pose, string expected)
        {
            // Act
            var kept = StyleSpacingPrecedence.WithoutOverriddenBase(
                V.ParseClassNames(baseClasses), V.ParseClassNames(pose));

            // Assert
            Assert.That(string.Join(" ", kept), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnInlineBaseShorthandAndAPoseClassLonghand_When_TheMotionMerges_Then_ThePoseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "pt-2" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "p-13", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(8f));
        }

        [Test]
        public void Given_AnInlineBaseShorthandAndAPoseClassLonghand_When_TheMotionMerges_Then_TheBaseKeepsTheOtherSides()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "pt-2" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "p-13", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingLeft.value.value, Is.EqualTo(52f));
        }

        // GREEN_ON_BASE(characterization): a label that names no pose leaves the base as written, which the base does too.
        // A merge that dropped the base without a pose to override it would redden this.
        [Test]
        public void Given_AnInlineBaseShorthandAndNoPoseNaming_When_TheMotionMerges_Then_TheBaseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "pt-2" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "p-13", variants: variants, animate: "off"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(52f));
        }

        [Test]
        public void Given_AnInlinePhysicalBaseAndALogicalPose_When_TheMotionMerges_Then_ThePoseHoldsTheLeft()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "ps-13" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "pl-[8px]", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingLeft.value.value, Is.EqualTo(52f));
        }

        [TestCase("p-13", "pt-2", "pt-[8px]")]
        [TestCase("p-13", "mx-auto", "mx-auto")]
        [TestCase("m-13", "mx-auto", "mx-[auto]")]
        [TestCase("size-13", "w-full", "w-[100%]")]
        [TestCase("p-13", "pt-2 flex", "pt-[8px] flex")]
        [TestCase("p-4", "pt-2", "pt-2")]
        [TestCase("pt-[8px]", "p-4", "p-4")]
        [TestCase("p-13", "hover:pt-2", "hover:pt-2")]
        [TestCase("!p-13", "pt-2", "pt-2")]
        public void Given_BaseAndPoseClasses_When_TheNarrowerPoseClassIsInlined_Then_OnlyAClassBesideAnInlineBaseChanges(
            string baseClasses, string pose, string expected)
        {
            // Act
            var inlined = StyleSpacingPrecedence.InlineNarrowerPose(
                V.ParseClassNames(baseClasses), V.ParseClassNames(pose));

            // Assert
            Assert.That(string.Join(" ", inlined), Is.EqualTo(expected));
        }

        // Every hold composes the applied set here, so a base token the pose overrides must stay out of it or
        // the swap would play from the base's value instead of the held pose's.
        [TestCase("pt-[8px]", "p-[10px]", "p-[52px]", "p-[52px]")]
        [TestCase("pt-[8px] flex", "p-[10px]", "", "flex")]
        [TestCase("pt-[8px]", "", "p-[52px]", "p-[52px]")]
        [TestCase("p-[52px]", "pt-[10px]", "", "p-[52px]")]
        public void Given_ABaseSpacingTokenAndAPoseOrHeldToken_When_AHoldComposesTheAppliedSet_Then_TheOverriddenBaseStaysOut(
            string baseClasses, string variantClasses, string held, string expected)
        {
            // Act
            var composed = FiberNodePatcher.ComposeWithHeldTokens(
                V.ParseClassNames(baseClasses), V.ParseClassNames(variantClasses), V.ParseClassNames(held));

            // Assert
            Assert.That(string.Join(" ", composed), Is.EqualTo(expected));
        }
    }
}
