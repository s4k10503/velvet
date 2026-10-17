using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two base utilities that write one longhand, one resolved inline (a bracket value, a fraction, a
    /// negated step, a colour with an opacity, a logical side) and one a stylesheet class: the one Tailwind emits
    /// later holds it, whichever is written first. A token the later classes write in full is left out of the list.
    /// A variant and an important token are ranked by their own layer, and a token the classes write only in part
    /// stays whole.
    /// </summary>
    [TestFixture]
    internal sealed class InlineSheetOrderTests
    {
        private static VisualElement MountOne(Reconciler reconciler, VisualElement root, string className)
        {
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(className) });
            return root.ElementAt(0);
        }

        [TestCase("w-px w-[10px]", "w-px")]
        [TestCase("w-[10px] w-px", "w-px")]
        [TestCase("w-full w-[10px]", "w-full")]
        [TestCase("w-[10px] w-full", "w-full")]
        public void Given_AnInlineWidthAndAWidthClassTailwindEmitsLater_When_Reconciled_Then_TheClassHoldsTheWidth(
            string className, string winner)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.width.keyword, element.ClassListContains(winner)),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("w-1/2 w-4")]
        [TestCase("w-4 w-1/2")]
        public void Given_AFractionWidthAndAStepClassTailwindEmitsLater_When_Reconciled_Then_TheClassHoldsTheWidth(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.width.keyword, element.ClassListContains("w-4")),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("mt-[10px] mt-auto", "mt-auto")]
        [TestCase("mt-auto mt-[10px]", "mt-auto")]
        [TestCase("-mt-2 mt-1", "mt-1")]
        [TestCase("mt-1 -mt-2", "mt-1")]
        public void Given_AnInlineMarginAndAMarginClassTailwindEmitsLater_When_Reconciled_Then_TheClassHoldsTheMargin(
            string className, string winner)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.marginTop.keyword, element.ClassListContains(winner)),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("bg-[#ff0000] bg-red-500", "bg-red-500")]
        [TestCase("bg-red-500 bg-[#ff0000]", "bg-red-500")]
        [TestCase("bg-red-500/50 bg-sky-500", "bg-sky-500")]
        [TestCase("bg-sky-500 bg-red-500/50", "bg-sky-500")]
        public void Given_AnInlineBackgroundAndAPaletteClassTailwindEmitsLater_When_Reconciled_Then_TheClassHoldsTheColour(
            string className, string winner)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.backgroundColor.keyword, element.ClassListContains(winner)),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("text-lg/7 text-sm")]
        [TestCase("text-sm text-lg/7")]
        [TestCase("text-[14px]/6 text-sm")]
        public void Given_ASlashSizeAndANamedSizeTailwindEmitsLater_When_Reconciled_Then_TheClassHoldsTheFontSize(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.fontSize.keyword, element.ClassListContains("text-sm")),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("border-s-2 border-l-4")]
        [TestCase("border-l-4 border-s-2")]
        public void Given_ALogicalBorderWidthAndThePhysicalClassOfItsSide_When_Reconciled_Then_TheClassHoldsTheWidth(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.borderLeftWidth.keyword, element.ClassListContains("border-l-4")),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [TestCase("border-[3px] border-l-4")]
        [TestCase("border-l-4 border-[3px]")]
        public void Given_AnInlineBorderWidthAndTheClassOfOneSide_When_Reconciled_Then_TheClassHoldsThatSideAndTheTokenTheRest(
            string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            var style = element.style;
            Assert.That(
                (style.borderTopWidth.value, style.borderRightWidth.value, style.borderBottomWidth.value,
                    style.borderLeftWidth.keyword, element.ClassListContains("border-l-4")),
                Is.EqualTo((3f, 3f, 3f, StyleKeyword.Null, true)));
        }

        // GREEN_ON_BASE(characterization): the base normaliser already writes ml-8 inline and drops ms-4.
        // Keeping ms-4 inline beside it reddens both orders, since it then holds the left margin at 16.
        [TestCase("ml-8 ms-4")]
        [TestCase("ms-4 ml-8")]
        public void Given_AMlClassAndAnMsClass_When_Reconciled_Then_TheMlHoldsTheLeftMarginInline(string className)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That((element.style.marginLeft.value.value, element.ClassListContains("ml-8")),
                Is.EqualTo((32f, false)));
        }

        // GREEN_ON_BASE(characterization): the base lets the inline token hold every such pair, which is Tailwind's answer where the inline token is emitted later.
        // Dropping the inline token whatever the order reddens it.
        [TestCase("w-4 w-[10px]", 10f)]
        [TestCase("w-[10px] w-4", 10f)]
        [TestCase("w-0 w-1/2", 50f)]
        [TestCase("w-1/2 w-0", 50f)]
        public void Given_AnInlineWidthTailwindEmitsAfterTheStepClass_When_Reconciled_Then_TheInlineWidthHolds(
            string className, float expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            var element = MountOne(reconciler, root, className);

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(expected));
        }

        [Test]
        public void Given_EveryClassTheBundledStylesheetsDefine_When_ParsedAsALayer_Then_NoneIsOneTheInlineGateRefuses()
        {
            // Arrange
            var classes = ((System.Collections.IDictionary)typeof(StyleUtilityProperties)
                .GetField("ByClassName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!).Keys.Cast<string>();

            // Act
            var taken = classes.Where(cls => !StyleArbitraryValueResolver.IsInlineResolved(cls)
                && StyleArbitraryValueResolver.TryParseLayerQuietly(cls, out _)).ToArray();

            // Assert
            Assert.That(string.Join(" ", taken), Is.Empty);
        }

        [Test]
        public void Given_TheWinningClassOfAPairRemoved_When_Reconciled_Then_TheInlineTokenHoldsTheWidth()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("w-px w-[10px]") };
            var newTree = new VNode[] { V.Div("w-[10px]") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var element = root.ElementAt(0);
            var before = element.style.width.keyword;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, element.style.width.value.value, element.ClassListContains("w-px")),
                Is.EqualTo((StyleKeyword.Null, 10f, false)));
        }

        [Test]
        public void Given_TheLosingTokenOfAPairRemoved_When_Reconciled_Then_TheClassStillHoldsTheWidth()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("w-px w-[10px]") };
            var newTree = new VNode[] { V.Div("w-px") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var element = root.ElementAt(0);
            var before = element.style.width.keyword;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, element.style.width.keyword, element.ClassListContains("w-px")),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, true)));
        }

        // GREEN_ON_BASE(characterization): the base drops ms-4 beside ml-8 and returns the margin to the class once ms-4 goes.
        // Leaving the inline write behind on removal reddens it.
        [Test]
        public void Given_TheMsClassOfAnMlPairRemoved_When_Reconciled_Then_TheMlClassHoldsTheMarginAgain()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div("ml-8 ms-4") };
            var newTree = new VNode[] { V.Div("ml-8") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var element = root.ElementAt(0);
            var before = element.style.marginLeft.value.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, element.style.marginLeft.keyword, element.ClassListContains("ml-8")),
                Is.EqualTo((32f, StyleKeyword.Null, true)));
        }

        [TestCase("w-px w-[10px]", "w-px")]
        [TestCase("w-[10px] w-px", "w-px")]
        [TestCase("w-1/2 w-4", "w-4")]
        [TestCase("-mt-2 mt-1", "mt-1")]
        [TestCase("mt-[10px] mt-auto", "mt-auto")]
        [TestCase("bg-[#ff0000] bg-red-500", "bg-red-500")]
        [TestCase("bg-red-500/50 bg-sky-500", "bg-sky-500")]
        [TestCase("border-s-2 border-l-4", "border-l-4")]
        [TestCase("rounded-[2px] rounded-lg", "rounded-lg")]
        [TestCase("rounded-lg rounded-[2px]", "rounded-lg")]
        [TestCase("flex w-px w-[10px] p-2", "flex w-px p-2")]
        [TestCase("w-[10px] w-px w-[20px]", "w-px")]
        [TestCase("border-[3px] border-x-2 border-y-4", "border-x-2 border-y-4")]
        [TestCase("text-lg/7 text-sm", "text-sm")]
        [TestCase("text-sm text-lg/7", "text-sm")]
        [TestCase("text-[14px]/6 text-sm", "text-sm")]
        [TestCase("w-px w-[10px] size-[5px]", "w-[1px] size-[5px]")]
        public void Given_AnInlineTokenTheLaterClassesWriteInFull_When_ClassNamesAreParsed_Then_TheTokenIsLeftOut(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        [TestCase("border-[3px] border-l-4", "border-t-[3px] border-r-[3px] border-b-[3px] border-l-4")]
        [TestCase("border-l-4 border-[3px]", "border-l-4 border-t-[3px] border-r-[3px] border-b-[3px]")]
        [TestCase("border-[3px] border-l-4 border-t-2", "border-r-[3px] border-b-[3px] border-l-4 border-t-2")]
        [TestCase("border-[3px] border-x-2", "border-t-[3px] border-b-[3px] border-x-2")]
        public void Given_AnInlineBorderWidthTheLaterClassesWriteInPart_When_ClassNamesAreParsed_Then_ItIsCutDownToTheSidesTheyLeave(
            string className, string expected)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(expected));
        }

        private static IEnumerable<(string Name, StyleUtilityRule Rule)> BundledClasses()
        {
            foreach (var name in StyleUtilityTableProbe.ClassNames())
            {
                StyleUtilityProperties.TryGet(name, out var rule);
                yield return (name, rule);
            }
        }

        // GREEN_ON_BASE(construction): both sides here are the bundled sheets and the inline families' longhand table, which agree.
        // The flex shorthand is left to StyleSpacingPrecedence with the spacing families, so grow-0 writing part of it is not
        // counted here. Add `.border-t-red-500 { border-top-color: red; }` to `_borders.uss`, regenerate `StyleUtilityProperties.g.cs`, and this is what reddens.
        [Test]
        public void Given_TheMultiLonghandInlineFamiliesNoOtherPassOrders_When_ComparedWithTheBundledClasses_Then_NoClassWritesPartOfOne()
        {
            // Arrange
            var families = Enum.GetValues(typeof(ArbitraryProperty)).Cast<ArbitraryProperty>()
                .Where(property => property != ArbitraryProperty.BorderWidth
                    && !StyleArbitraryValueResolver.IsSpacingRadiusOrFlexProperty(property))
                .ToArray();

            // Act
            var partial = BundledClasses()
                .Where(utility => utility.Rule.Gate == StyleUtilityGate.None)
                .SelectMany(utility => families
                    .Where(property =>
                    {
                        var written = StyleArbitraryLonghands.Of(property);
                        return utility.Rule.Properties.Overlaps(written)
                            && utility.Rule.Properties.Union(written) != utility.Rule.Properties;
                    })
                    .Select(property => utility.Name + " writes part of " + property))
                .ToArray();

            // Assert
            Assert.That(string.Join("; ", partial), Is.Empty);
        }

        // GREEN_ON_BASE(construction): both sides here are the bundled sheets and the inline dispatch gate, which agree on the base too.
        // Add `.ms-4 { margin-left: 1px; }` to `_spacing.uss`, regenerate `StyleUtilityProperties.g.cs`, and this is what reddens.
        [Test]
        public void Given_TheBundledClassNames_When_AskedWhetherTheyResolveInline_Then_NoneDoes()
        {
            // Act
            var claimed = BundledClasses()
                .Select(utility => utility.Name)
                .Where(StyleArbitraryValueResolver.IsInlineResolved)
                .ToArray();

            // Assert
            Assert.That(string.Join("; ", claimed), Is.Empty);
        }

        // GREEN_ON_BASE(construction): both sides here are the bundled sheets and the readers of StyleSpacingPrecedence, which agree.
        // Drop the `("grow-0", ArbitraryProperty.FlexGrow, "grow-[0]")` row from `s_flexFactorClasses` and this is what reddens,
        // as adding `.w-fit { width: auto; }` to `_sizing.uss` and regenerating `StyleUtilityProperties.g.cs` does.
        [Test]
        public void Given_TheBundledClassesThatWritePartOfASpacingSizeInsetOrRadiusFamily_When_ReadAsStylesheetClasses_Then_EachIsRead()
        {
            // Arrange
            var families = Enum.GetValues(typeof(ArbitraryProperty)).Cast<ArbitraryProperty>()
                .Where(StyleArbitraryValueResolver.IsSpacingRadiusOrFlexProperty)
                .ToArray();

            // Act
            var unread = BundledClasses()
                .Where(utility => utility.Rule.Gate == StyleUtilityGate.None)
                .Where(utility => families.Any(property =>
                {
                    var written = StyleArbitraryLonghands.Of(property);
                    return utility.Rule.Properties.Overlaps(written)
                        && utility.Rule.Properties.Union(written) != utility.Rule.Properties;
                }))
                .Select(utility => utility.Name)
                .Where(name => !StyleArbitraryValueResolver.TryReadStylesheetClass(name, out _, out _))
                .ToArray();

            // Assert
            Assert.That(string.Join("; ", unread), Is.Empty);
        }

        // GREEN_ON_BASE(construction): both sides here are the bundled sheets and the names StyleRuleOrder sorts them by, which agree on the base too.
        // Change `.rotate-n6 { rotate: -6deg; }` to `rotate: -7deg;` in `_transforms.uss` and this is what reddens.
        [Test]
        public void Given_TheNegatedRotateClasses_When_TheirSheetRulesAreRead_Then_EachWritesTheNegativeOfTheDegreesItsNameGives()
        {
            // Arrange
            var sheet = File.ReadAllText(Path.GetFullPath("Packages/com.velvet.core/Runtime/Styles/_transforms.uss"));
            var degrees = BundledClasses()
                .Select(utility => Regex.Match(utility.Name, "^rotate-n(\\d+)$"))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)
                .ToArray();

            // Act
            var wrong = degrees.Where(value => !sheet.Contains(".rotate-n" + value + " { rotate: -" + value + "deg; }"));

            // Assert
            Assert.That((degrees.Length > 0, string.Join("; ", wrong)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(construction): both sides here are the bundled sheets and the names StyleRuleOrder sorts them by, which agree on the base too.
        // Change `--duration-fast: 0.1s;` to `0.2s` in `_tokens.uss` and this is what reddens.
        [TestCase("fast", "100", "0.1s")]
        [TestCase("normal", "150", "0.15s")]
        [TestCase("slow", "200", "0.2s")]
        public void Given_ADurationName_When_ComparedWithTheStepItIsOrderedAs_Then_BothSheetsGiveTheSameTime(
            string name, string step, string time)
        {
            // Arrange
            var tokens = File.ReadAllText(Path.GetFullPath("Packages/com.velvet.core/Runtime/Styles/_tokens.uss"));
            var steps = File.ReadAllText(Path.GetFullPath("Packages/com.velvet.core/Runtime/Styles/_state_variants.uss"));

            // Act
            var agree = tokens.Contains("--duration-" + name + ": " + time + ";")
                && steps.Contains(".duration-" + step + " { transition-duration: " + time + "; }");

            // Assert
            Assert.That(agree, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base rewrites or drops nothing for these, so each list reaches the reconciler as written.
        // Each is a pair where the inline token already holds what Tailwind gives it, or one ranked by its own layer
        // (a variant, an important token, a class gated on a pseudo-class) or one that writes no longhand the
        // classes compete for (a filter), so dropping every inline token that has a stylesheet class beside it reddens it.
        [TestCase("w-4 w-[10px]")]
        [TestCase("w-[10px] w-4")]
        [TestCase("mt-1 mt-[10px]")]
        [TestCase("w-0 w-1/2")]
        [TestCase("md:w-[10px] w-px")]
        [TestCase("!w-[10px] w-px")]
        [TestCase("w-[10px]! w-px")]
        [TestCase("w-[10px] !w-px")]
        [TestCase("bg-[#ffffff] hover-bg-surface")]
        [TestCase("scale-100 -scale-[0.5]")]
        [TestCase("scale-95 -scale-x-[1]")]
        [TestCase("text-lg text-xl/7")]
        [TestCase("rotate-[10deg] rotate-n6")]
        [TestCase("rotate-n6 -rotate-6")]
        [TestCase("duration-[300ms] duration-fast")]
        [TestCase("blur-[4px] w-px")]
        [TestCase("w-px blur-sm")]
        [TestCase("border-[3px] border-2")]
        [TestCase("border-[3px] w-px")]
        [TestCase("-border-[3px] border-l-4")]
        public void Given_AnInlineTokenNoLaterClassOutranks_When_ClassNamesAreParsed_Then_TheyAreAsWritten(string className)
        {
            // Act
            var tokens = V.ParseClassNames(className);

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(className));
        }
    }
}
