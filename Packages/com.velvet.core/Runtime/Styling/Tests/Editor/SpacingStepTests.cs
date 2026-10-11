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
        [TestCase("-max-w-13")]
        public void Given_AClassTheScaleDoesNotTake_When_ParsedAndGated_Then_NeitherClaimsIt(string className)
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
    }
}
