using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the order of a shorthand and a longhand of one spacing family: Tailwind emits the shorthand
    /// ahead of the longhands it contains, so the longhand holds its side whichever is written first and
    /// whichever of the two resolves inline. A stylesheet class that takes part is written inline, and a
    /// <c>V.Motion</c> pose overrides the node's own spacing.
    /// </summary>
    [TestFixture]
    internal sealed class SpacingPrecedenceTests
    {
        internal static float Side(VisualElement element, string side) => side switch
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
        [TestCase("pt-2 p-[52px]", "paddingTop", 8f)]
        [TestCase("p-[52px] pt-2", "paddingTop", 8f)]
        [TestCase("pt-1-5 p-[52px]", "paddingTop", 6f)]
        [TestCase("m-[52px] mt-2", "marginTop", 8f)]
        [TestCase("inset-[52px] top-2", "top", 8f)]
        [TestCase("px-[52px] pl-2", "paddingLeft", 8f)]
        [TestCase("!p-[52px] !pt-2", "paddingTop", 8f)]
        [TestCase("!pt-2 !p-[52px]", "paddingTop", 8f)]
        [TestCase("w-4 size-[52px]", "width", 16f)]
        [TestCase("ps-[52px] pl-2", "paddingLeft", 8f)]
        [TestCase("pl-2 ps-[52px]", "paddingLeft", 8f)]
        [TestCase("pl-[8px] ps-[52px]", "paddingLeft", 8f)]
        [TestCase("ms-[52px] ml-2", "marginLeft", 8f)]
        [TestCase("start-[52px] left-2", "left", 8f)]
        [TestCase("p-[52px] ps-2", "paddingLeft", 8f)]
        [TestCase("ps-2 p-[52px]", "paddingLeft", 8f)]
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
            var tree = new VNode[] { V.Div("pt-2 p-[52px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var style = root.ElementAt(0).style;
            Assert.That((style.paddingTop.value.value, style.paddingBottom.value.value), Is.EqualTo((8f, 52f)));
        }

        [Test]
        public void Given_ALonghandRemovedBesideAnInlineShorthand_When_Reconciled_Then_TheShorthandReturnsToTheSide()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("p-[52px] pt-2") };
            var newTree = new VNode[] { V.Div("p-[52px]") };
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), oldTree);
            var before = root.ElementAt(0).style.paddingTop.value.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, root.ElementAt(0).style.paddingTop.value.value), Is.EqualTo((8f, 52f)));
        }

        [Test]
        public void Given_EqualLayerVariantPayloadsOfAShorthandAndALonghand_When_Enabled_Then_TheLonghandHoldsTheSide()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            StyleVariantPayload.Apply(element, new[] { "pt-[8px]", "p-[52px]" }, true, StyleLayerPriority.Hover);

            // Assert
            Assert.That(element.style.paddingTop.value.value, Is.EqualTo(8f));
        }

        [TestCase("p-[52px] pt-2", "p-[52px] pt-[8px]")]
        [TestCase("pt-2 p-[52px]", "pt-[8px] p-[52px]")]
        [TestCase("hover:p-[52px] hover:pt-2", "hover:p-[52px] hover:pt-[8px]")]
        [TestCase("sm:hover:p-[52px] sm:hover:pt-2", "sm:hover:p-[52px] sm:hover:pt-[8px]")]
        [TestCase("!p-[52px] !pt-2", "!p-[52px] !pt-[8px]")]
        [TestCase("!p-[52px] pt-2!", "!p-[52px] pt-[8px]!")]
        [TestCase("px-[52px] pl-2", "px-[52px] pl-[8px]")]
        [TestCase("p-[52px] py-[6px] pt-2", "p-[52px] py-[6px] pt-[8px]")]
        [TestCase("m-[52px] mx-auto", "m-[52px] mx-[auto]")]
        [TestCase("m-[8px] mt-auto", "m-[8px] mt-[auto]")]
        [TestCase("size-[52px] w-full", "size-[52px] w-[100%]")]
        [TestCase("h-full size-[52px]", "h-[100%] size-[52px]")]
        [TestCase("hover:focus:pt-2 focus:hover:p-[52px]", "hover:focus:pt-[8px] focus:hover:p-[52px]")]
        [TestCase("ps-[52px] pl-2", "pl-[8px]")]
        [TestCase("pl-2 ps-[52px]", "pl-[8px]")]
        [TestCase("ps-[52px] pl-[8px] flex", "pl-[8px] flex")]
        [TestCase("size-[52px] w-4", "size-[52px] w-[16px]")]
        [TestCase("p-[52px] pt-2 flex", "p-[52px] pt-[8px] flex")]
        [TestCase("p-[52px] pt-0", "p-[52px] pt-[0px]")]
        [TestCase("p-[52px] pt-px", "p-[52px] pt-[1px]")]
        [TestCase("size-[52px] w-9", "size-[52px] w-[36px]")]
        [TestCase("[&>*]:p-[52px] [&>*]:pt-2", "[&>*]:p-[52px] [&>*]:pt-[8px]")]
        [TestCase("pt-[1px] ps-[52px] pl-2", "pt-[1px] pl-[8px]")]
        [TestCase("pl-2 pl-4 ps-[1px]", "pl-2 pl-[16px]")]
        [TestCase("pl-4 pl-2 ps-[1px]", "pl-[16px] pl-2")]
        public void Given_AStylesheetClassBesideAnInlineShorthandOfItsFamily_When_ClassNamesAreParsed_Then_TheClassIsWrittenInline(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        // The bang behind the variants, as Tailwind spells it, is read as important and written back there.
        [Test]
        public void Given_AnImportantClassBehindAVariantBesideAnImportantShorthand_When_Parsed_Then_TheClassIsWrittenInline()
        {
            // Act
            var tokens = V.ParseClassNames("sm:!p-[52px] sm:!pt-2");

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo("sm:!p-[52px] sm:!pt-[8px]"));
        }

        // GREEN_ON_BASE(characterization): the base rewrites no class, so each list below reaches the reconciler as written.
        // Each is a pair where leaving the class alone gives Tailwind's answer already or the two do not
        // contend, so a rewrite of every stylesheet class beside an inline one reddens it.
        [TestCase("p-4 pt-[2px]")]
        [TestCase("p-4 pt-[52px]")]
        [TestCase("px-4 py-[52px]")]
        [TestCase("p-[52px] p-4")]
        [TestCase("p-[52px] hover:pt-2")]
        [TestCase("!p-[52px] pt-2")]
        [TestCase("p-4 pt-2")]
        [TestCase("size-4 w-[52px]")]
        [TestCase("hover:ps-[52px] hover:pl-2")]
        [TestCase("[&>*]:hover:p-[52px] [&>*]:focus:pt-2")]
        [TestCase("[&>a]:[&>b]:p-[52px] [&>b]:[&>a]:pt-2")]
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
            var tree = new VNode[] { V.Div("m-[52px] mx-auto") };

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

        [TestCase("size-[52px] w-full")]
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
            var tree = new VNode[] { V.Div("h-full size-[52px]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            var height = root.ElementAt(0).style.height.value;
            Assert.That((height.value, height.unit), Is.EqualTo((100f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_AnAutoBracketOnAMinimumWidth_When_Reconciled_Then_TheMinimumWidthIsAuto()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var tree = new VNode[] { V.Div("min-w-[auto]") };

            // Act
            reconciler.Reconcile(root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(root.ElementAt(0).style.minWidth.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [TestCase("mx-[auto]", ArbitraryProperty.MarginX)]
        [TestCase("min-w-[auto]", ArbitraryProperty.MinWidth)]
        [TestCase("min-h-[auto]", ArbitraryProperty.MinHeight)]
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

        // GREEN_ON_BASE(characterization): the base declines an auto bracket everywhere, and padding and a maximum take none.
        // Dropping the padding test from `TakesAuto` accepts `p-[auto]`, and dropping its maximum test
        // accepts `max-w-[auto]`; either reddens this.
        [TestCase("p-[auto]")]
        [TestCase("pt-[auto]")]
        [TestCase("-mt-[auto]")]
        [TestCase("max-w-[auto]")]
        [TestCase("max-h-[auto]")]
        public void Given_AnAutoBracketOnPaddingMaxOrNegated_When_Parsed_Then_ItIsDeclined(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base lets the later inline pose hold the top by arrival, and so must this change.
        [Test]
        public void Given_ABaseLonghandAndAPoseShorthand_When_TheMotionMerges_Then_ThePoseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "p-[52px]" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "pt-[8px]", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(52f));
        }

        // GREEN_ON_BASE(characterization): an inline pose outranks a base class on the base too, and this change must not undo it.
        [Test]
        public void Given_AClassBaseLonghandAndAnInlinePoseShorthand_When_TheMotionMerges_Then_ThePoseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "p-[52px]" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "pt-2", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(52f));
        }

        [TestCase("pt-[8px] flex", "p-[52px]", "flex")]
        [TestCase("pt-[8px] pb-[8px]", "py-[52px]", "")]
        [TestCase("ps-1", "p-[52px]", "")]
        [TestCase("p-[52px]", "pt-[8px]", "p-[52px]")]
        [TestCase("pt-[8px]", "pt-[10px]", "")]
        [TestCase("pt-[8px]", "hover:p-[52px]", "pt-[8px]")]
        [TestCase("!pt-[8px]", "p-[52px]", "!pt-[8px]")]
        [TestCase("pt-[8px]", "flex", "pt-[8px]")]
        [TestCase("pt-2", "p-[52px]", "pt-2")]
        [TestCase("pt-[8px] ab", "p-[52px] cd", "ab")]
        [TestCase("pt-[8px]", "hover:", "pt-[8px]")]
        [TestCase("flex ab pt-[8px]", "p-[52px] cd", "flex ab")]
        [TestCase("flex pt-[8px]", "grid p-[52px]", "flex")]
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
        public void Given_AnInlineBaseShorthandAndAPoseClassLonghand_When_TheMotionMerges_Then_ThePoseHoldsItsSideAndTheBaseTheRest()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "pt-2" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "p-[52px]", variants: variants, animate: "on"));

            // Assert
            var style = root.Q<VisualElement>("leaf").style;
            Assert.That((style.paddingTop.value.value, style.paddingLeft.value.value), Is.EqualTo((8f, 52f)));
        }

        // GREEN_ON_BASE(characterization): with no pose applied the base shorthand holds the top there too.
        [Test]
        public void Given_AnInlineBaseShorthandAndNoPoseNaming_When_TheMotionMerges_Then_TheBaseHoldsTheTop()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "pt-2" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "p-[52px]", variants: variants, animate: "off"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingTop.value.value, Is.EqualTo(52f));
        }

        // GREEN_ON_BASE(characterization): the later inline logical pose holds the left by arrival on the base, and so must this change.
        [Test]
        public void Given_AnInlinePhysicalBaseAndALogicalPose_When_TheMotionMerges_Then_ThePoseHoldsTheLeft()
        {
            // Arrange
            var root = new VisualElement();
            var variants = new System.Collections.Generic.Dictionary<string, MotionVariant> { ["on"] = "ps-[52px]" };

            // Act
            using var mounted = V.Mount(root, V.Motion(name: "leaf", className: "pl-[8px]", variants: variants, animate: "on"));

            // Assert
            Assert.That(root.Q<VisualElement>("leaf").style.paddingLeft.value.value, Is.EqualTo(52f));
        }

        [TestCase("p-[52px]", "pt-2", "pt-[8px]")]
        [TestCase("p-[52px]", "mx-auto", "mx-auto")]
        [TestCase("m-[52px]", "mx-auto", "mx-[auto]")]
        [TestCase("size-[52px]", "w-full", "w-[100%]")]
        [TestCase("p-[52px]", "pt-2 flex", "pt-[8px] flex")]
        [TestCase("p-4", "pt-2", "pt-2")]
        [TestCase("pt-[8px]", "p-4", "p-4")]
        [TestCase("p-[52px]", "hover:pt-2", "hover:pt-2")]
        [TestCase("!p-[52px]", "pt-2", "pt-2")]
        [TestCase("hover:p-[52px]", "hover:pt-2", "hover:pt-[8px]")]
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

        [TestCase("w-1/2")]
        [TestCase("p-")]
        [TestCase("")]
        public void Given_ANameThatIsNotASpacingStep_When_ReadAsAStep_Then_ItIsDeclined(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryReadSpacingStep(className, out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("flex")]
        [TestCase("grid")]
        public void Given_ADeclaredClassOutsideTheSpacingFamilies_When_ReadAsAStylesheetClass_Then_ItIsDeclined(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryReadStylesheetClass(className, out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("pt-2", true)]
        [TestCase("-mt-2", true)]
        [TestCase("flex", false)]
        public void Given_AToken_When_AskedWhetherItCanBeASpacingUtility_Then_OnlyAFamilyPrefixAnswersYes(string token, bool expected)
        {
            // Act
            var startsWithPrefix = StyleArbitraryValueResolver.StartsWithSpacingPrefix(token);

            // Assert
            Assert.That(startsWithPrefix, Is.EqualTo(expected));
        }

        // TryReadStylesheetClass tests TakesAuto only for a name the sheets declare, so while none of these is
        // declared that test is never what turns a class away. Declaring one makes it matter and needs a case of its own.
        [TestCase("p-auto")]
        [TestCase("px-auto")]
        [TestCase("py-auto")]
        [TestCase("pt-auto")]
        [TestCase("pr-auto")]
        [TestCase("pb-auto")]
        [TestCase("pl-auto")]
        [TestCase("max-w-auto")]
        [TestCase("max-h-auto")]
        public void Given_PaddingOrMaximumFamily_When_TheSheetsAreAskedForItsAutoClass_Then_NoneIsDeclared(string className)
        {
            // Act
            var declared = StyleUtilityProperties.TryGet(className, out _);

            // Assert
            Assert.That(declared, Is.False);
        }

        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingX)]
        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingY)]
        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingTop)]
        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingRight)]
        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingBottom)]
        [TestCase(ArbitraryProperty.Padding, ArbitraryProperty.PaddingLeft)]
        [TestCase(ArbitraryProperty.PaddingX, ArbitraryProperty.PaddingLeft)]
        [TestCase(ArbitraryProperty.PaddingX, ArbitraryProperty.PaddingRight)]
        [TestCase(ArbitraryProperty.PaddingY, ArbitraryProperty.PaddingTop)]
        [TestCase(ArbitraryProperty.PaddingY, ArbitraryProperty.PaddingBottom)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginX)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginY)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginTop)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginRight)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginBottom)]
        [TestCase(ArbitraryProperty.Margin, ArbitraryProperty.MarginLeft)]
        [TestCase(ArbitraryProperty.MarginX, ArbitraryProperty.MarginLeft)]
        [TestCase(ArbitraryProperty.MarginX, ArbitraryProperty.MarginRight)]
        [TestCase(ArbitraryProperty.MarginY, ArbitraryProperty.MarginTop)]
        [TestCase(ArbitraryProperty.MarginY, ArbitraryProperty.MarginBottom)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.InsetX)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.InsetY)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.Top)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.Right)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.Bottom)]
        [TestCase(ArbitraryProperty.Inset, ArbitraryProperty.Left)]
        [TestCase(ArbitraryProperty.InsetX, ArbitraryProperty.Left)]
        [TestCase(ArbitraryProperty.InsetX, ArbitraryProperty.Right)]
        [TestCase(ArbitraryProperty.InsetY, ArbitraryProperty.Top)]
        [TestCase(ArbitraryProperty.InsetY, ArbitraryProperty.Bottom)]
        [TestCase(ArbitraryProperty.Size, ArbitraryProperty.Height)]
        [TestCase(ArbitraryProperty.Size, ArbitraryProperty.Width)]
        public void Given_AShorthandAndALonghandItContains_When_Compared_Then_TheShorthandSortsAhead(
            object shorthand, object longhand)
        {
            // Act
            var order = StyleSpacingPrecedence.CompareCascade((ArbitraryProperty)shorthand, (ArbitraryProperty)longhand);

            // Assert
            Assert.That(order, Is.LessThan(0));
        }
    }
}
