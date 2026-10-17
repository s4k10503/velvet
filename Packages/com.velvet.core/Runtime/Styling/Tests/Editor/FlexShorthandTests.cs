using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind v4's <c>flex</c> shorthand utilities: <c>flex-&lt;N&gt;</c>, <c>flex-&lt;a&gt;/&lt;b&gt;</c>
    /// and <c>flex-[…]</c>, what each parses to, what it writes, and how it ranks against the grow, shrink and
    /// basis utilities and the bundled <c>flex-*</c> classes beside it in one className.
    /// </summary>
    [TestFixture]
    internal sealed class FlexShorthandTests
    {
        private static readonly StyleLonghandSet s_flexLonghands = StyleLonghandSet.Of(StyleLonghand.FlexGrow)
            .Union(StyleLonghandSet.Of(StyleLonghand.FlexShrink)).Union(StyleLonghandSet.Of(StyleLonghand.FlexBasis));

        // What each flex shorthand utility parses to: grow, shrink, and the basis with its unit ("auto" for a NaN
        // basis). A basis the element measures carries a placeholder here and an Expression besides.
        private static readonly (string Class, float Grow, float Shrink, string Basis)[] s_parses =
        {
            ("flex-12", 12f, 1f, "0Percent"),
            ("flex-0", 0f, 1f, "0Percent"),
            ("flex-3/4", 1f, 1f, "75Percent"),
            ("flex-[2_3_10px]", 2f, 3f, "10Pixel"),
            ("flex-[10px_2_3]", 2f, 3f, "10Pixel"),
            ("flex-[2_0]", 2f, 0f, "0Percent"),
            ("flex-[2_auto]", 2f, 1f, "auto"),
            ("flex-[2_AUTO]", 2f, 1f, "auto"),
            ("flex-[50%]", 1f, 1f, "50Percent"),
            ("flex-[none]", 0f, 0f, "auto"),
            ("flex-[NONE]", 0f, 0f, "auto"),
            ("flex-[auto]", 1f, 1f, "auto"),
            ("flex-[initial]", 0f, 1f, "auto"),
            ("flex-[unset]", 0f, 1f, "auto"),
            ("flex-[revert]", 0f, 1f, "auto"),
            ("flex-[1_1_0]", 1f, 1f, "0Pixel"),
            ("flex-[2_0%]", 2f, 1f, "0Percent"),
            ("flex-[2_3_2em]", 2f, 3f, "0Pixel"),
            ("flex-[50vw_2_3]", 2f, 3f, "0Pixel"),
            ("flex-[2_3_calc(50%+10px)]", 2f, 3f, "0Pixel"),
        };

        public static IEnumerable<TestCaseData> ParsedClasses => s_parses.Select(row => new TestCaseData(row.Class));
        public static IEnumerable<TestCaseData> ParsedGrows => s_parses.Select(row => new TestCaseData(row.Class, row.Grow));
        public static IEnumerable<TestCaseData> ParsedShrinks => s_parses.Select(row => new TestCaseData(row.Class, row.Shrink));
        public static IEnumerable<TestCaseData> ParsedBases => s_parses.Select(row => new TestCaseData(row.Class, row.Basis));

        [TestCaseSource(nameof(ParsedClasses))]
        public void Given_AFlexShorthandUtility_When_Parsed_Then_ItWritesTheThreeFlexLonghands(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert — the property is told by the longhands it writes, which the base can read too.
            Assert.That(ok && StyleArbitraryLonghands.Of(s.Property) == s_flexLonghands, Is.True);
        }

        [TestCaseSource(nameof(ParsedGrows))]
        public void Given_AFlexShorthandUtility_When_Parsed_Then_ItResolvesTheGrow(string cls, float expected)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That(ok ? s.Value : float.NaN, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(ParsedShrinks))]
        public void Given_AFlexShorthandUtility_When_Parsed_Then_ItResolvesTheShrink(string cls, float expected)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That(ok ? s.Value3 : float.NaN, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(ParsedBases))]
        public void Given_AFlexShorthandUtility_When_Parsed_Then_ItResolvesTheBasisAndItsUnit(string cls, string expected)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert — "auto" for a NaN basis.
            Assert.That(!ok ? "unrecognized" : float.IsNaN(s.Value2) ? "auto" : $"{s.Value2}{s.Unit2}", Is.EqualTo(expected));
        }

        [TestCase("flex-[2_3_2em]")]
        [TestCase("flex-[50vw_2_3]")]
        [TestCase("flex-[2_3_calc(50%+10px)]")]
        public void Given_AFlexShorthandWithABasisTheElementMeasures_When_Parsed_Then_TheBasisIsLeftToAnExpression(
            string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That(ok && s.Expression != null, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base parses no flex shorthand utility, so each is declined there.
        // Each row names the check that rejects it: dropping `denominator != 0` in TryGetFlexPreset reddens flex-1/0,
        // and dropping `number < 0f` in TakeFlexNumber reddens flex-[-1].
        [TestCase("flex-1/0")]          // a zero denominator
        [TestCase("flex-1/x")]          // a denominator that is not a whole number
        [TestCase("flex-x/2")]          // a numerator that is not a whole number
        [TestCase("flex-01")]           // a leading zero, which Tailwind's bare values do not spell
        [TestCase("flex-[2_10px_3]")]   // grow and shrink are one pair, so nothing may sit between them
        [TestCase("flex-[-1]")]         // a negative factor
        [TestCase("flex-[1_1_1]")]      // a third number that is not a zero basis
        [TestCase("flex-[10px_20px]")]  // two bases
        [TestCase("flex-[1_1_0_0]")]    // a second unitless zero after the basis
        [TestCase("flex-[1_1_-10px]")]  // a negative basis
        [TestCase("flex-[column]")]     // a direction keyword, which the flex shorthand rejects
        [TestCase("-flex-[2]")]         // a negated shorthand
        public void Given_AFlexShorthandTheGrammarRejects_When_Parsed_Then_ItDeclinesToParse(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base routes flex-1 to its USS class, as it does every bare flex.
        // What reddens it is dropping the `grow != 1` exclusion in TryGetFlexPreset, which claims it inline.
        [Test]
        public void Given_FlexOne_When_TheInlineGateIsAsked_Then_ItIsLeftToTheClassList()
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved("flex-1");

            // Assert
            Assert.That(inline, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base parses no flex bracket, so each token is its own key. What
        // reddens it is dropping Value3 from FiberNodePatcher.ValueKey, which leaves the shrink out of the key.
        [Test]
        public void Given_TwoFlexBracketsDifferingInTheShrink_When_Keyed_Then_TheKeysDiffer()
        {
            // Act
            var keyed = FiberNodePatcher.ValueKey("flex-[2_1]");

            // Assert
            Assert.That(keyed, Is.Not.EqualTo(FiberNodePatcher.ValueKey("flex-[2_3]")));
        }

        [Test]
        public void Given_AFlexAutoBracket_When_AppliedToElement_Then_TheBasisIsAuto()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("flex-[auto]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That(el.style.flexBasis.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [TestCase("grow")]
        [TestCase("shrink")]
        [TestCase("basis")]
        public void Given_AFlexShorthand_When_Cleared_Then_TheLonghandRevertsToNull(string longhand)
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("flex-[2_3_10px]", out var style);
            StyleArbitraryValueResolver.Apply(el, style);
            Assume.That(el.style.flexShrink.value, Is.EqualTo(3f)); // a fresh element already reads Null

            // Act
            StyleArbitraryValueResolver.Clear(el, style.Property);

            // Assert
            Assert.That(KeywordOf(el, longhand), Is.EqualTo(StyleKeyword.Null));
        }

        private static StyleKeyword KeywordOf(VisualElement element, string longhand)
            => longhand switch
            {
                "grow" => element.style.flexGrow.keyword,
                "shrink" => element.style.flexShrink.keyword,
                _ => element.style.flexBasis.keyword,
            };

        private static VisualElement MountDiv(Velvet.TestUtilities.ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className) });
            return scope.Root[0];
        }

        private static string BasisOf(VisualElement element)
            => $"{element.style.flexBasis.value.value}{element.style.flexBasis.value.unit}";

        [TestCase("grow", "3")]
        [TestCase("shrink", "1")]
        [TestCase("basis", "0Percent")]
        public void Given_ADivCarryingABareFlexNumber_When_Reconciled_Then_TheElementCarriesTheLonghand(
            string longhand, string expected)
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-3");

            // Assert
            Assert.That(longhand switch
            {
                "grow" => div.style.flexGrow.value.ToString(),
                "shrink" => div.style.flexShrink.value.ToString(),
                _ => BasisOf(div),
            }, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AFlexNumberBeforeAFlexOneInOneClassName_When_Mounted_Then_TheShorthandSetsTheGrow()
        {
            // Arrange — Tailwind orders flex-1 before flex-2, so the inline shorthand wins wherever it is listed.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-2 flex-1");

            // Assert
            Assert.That(div.style.flexGrow.value, Is.EqualTo(2f));
        }

        // GREEN_ON_BASE(characterization): the base does not parse flex-2, so flex-none sets all three there.
        // What reddens it is Normalize skipping StyleInlineSheetOrder.WithoutOutrankedInline, which leaves the inline
        // shorthand to outrank the class.
        [TestCase("grow")]
        [TestCase("shrink")]
        [TestCase("basis")]
        public void Given_AFlexNoneClassBeforeAFlexNumberInOneClassName_When_Mounted_Then_TheClassSetsTheLonghand(
            string longhand)
        {
            // Arrange — the class wins wherever it is listed, so no inline value is left.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-none flex-2");

            // Assert
            Assert.That(KeywordOf(div, longhand), Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base does not parse flex-0, so flex-1 sets the grow there.
        // What reddens it is Normalize skipping StyleInlineSheetOrder.WithoutOutrankedInline, as in the case above.
        [Test]
        public void Given_AFlexOneClassBeforeAFlexZeroInOneClassName_When_Mounted_Then_TheClassSetsTheGrow()
        {
            // Arrange — Tailwind orders flex-0 before flex-1.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-1 flex-0");

            // Assert
            Assert.That(div.style.flexGrow.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // Tailwind declares flex before flex-shrink, flex-grow and flex-basis, so a longhand beside the shorthand
        // takes its own part whichever is listed first, a class or an inline value alike.
        private static readonly (string ClassName, float Grow, float Shrink, string Basis)[] s_beside =
        {
            ("flex-2 shrink-0", 2f, 0f, "0Percent"),
            ("shrink-0 flex-2", 2f, 0f, "0Percent"),
            ("grow-0 flex-[3_2]", 0f, 2f, "0Percent"),
            ("grow-[3] flex-2", 3f, 1f, "0Percent"),
            ("basis-4 flex-2", 2f, 1f, "16Pixel"),
            ("basis-[20px] flex-[2_1_10px]", 2f, 1f, "20Pixel"),
        };

        public static IEnumerable<TestCaseData> BesideGrows => s_beside.Select(row => new TestCaseData(row.ClassName, row.Grow));
        public static IEnumerable<TestCaseData> BesideShrinks => s_beside.Select(row => new TestCaseData(row.ClassName, row.Shrink));
        public static IEnumerable<TestCaseData> BesideBases => s_beside.Select(row => new TestCaseData(row.ClassName, row.Basis));

        [TestCaseSource(nameof(BesideGrows))]
        public void Given_AFlexLonghandBesideTheShorthand_When_Mounted_Then_TheGrowIsTheOneTheTwoLeaveIt(
            string className, float expected)
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, className);

            // Assert
            Assert.That(div.style.flexGrow.value, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(BesideShrinks))]
        public void Given_AFlexLonghandBesideTheShorthand_When_Mounted_Then_TheShrinkIsTheOneTheTwoLeaveIt(
            string className, float expected)
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, className);

            // Assert
            Assert.That(div.style.flexShrink.value, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(BesideBases))]
        public void Given_AFlexLonghandBesideTheShorthand_When_Mounted_Then_TheBasisIsTheOneTheTwoLeaveIt(
            string className, string expected)
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, className);

            // Assert
            Assert.That(BasisOf(div), Is.EqualTo(expected));
        }

        private static readonly Dictionary<string, MotionVariant> s_narrowPose = new()
        {
            ["open"] = "w-[100px] flex-2",
            ["closed"] = "",
        };

        // A pose overrides the node's own className, as Framer Motion's animate does its style.
        private static VisualElement MountMotionAtThePose(Velvet.TestUtilities.ReconcilerScope scope)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Motion(className: "w-[200px] flex-[3]", variants: s_narrowPose, animate: "open",
                        transition: StyleTransitionConfig.None),
                });
            return scope.Root[0];
        }

        [TestCase("grow-0", "grow-[0]")]
        [TestCase("shrink-0", "shrink-[0]")]
        public void Given_AFlexFactorPoseClassOverAnInlineFlexShorthandBase_When_TheNarrowerPoseIsInlined_Then_TheClassTakesItsBracketForm(
            string pose, string expected)
        {
            // Act
            var inlined = StyleSpacingPrecedence.InlineNarrowerPose(new[] { "flex-[3]" }, new[] { pose });

            // Assert
            Assert.That(string.Join(" ", inlined), Is.EqualTo(expected));
        }

        [Test]
        public void Given_AMotionWhosePoseRepeatsItsClassNamesShorthand_When_MountedAtThePose_Then_ThePoseSetsTheGrow()
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var motion = MountMotionAtThePose(scope);

            // Assert
            Assert.That(motion.style.flexGrow.value, Is.EqualTo(2f));
        }

        [Test]
        public void Given_AMotionWhosePoseRepeatsItsClassNamesShorthand_When_MountedAtThePose_Then_ThePoseSetsTheWidth()
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var motion = MountMotionAtThePose(scope);

            // Assert
            Assert.That(motion.style.width.value.value, Is.EqualTo(100f));
        }
    }

    /// <summary>
    /// Holds the flex factor classes StyleArbitraryValueResolver reads as bracket tokens against the values
    /// <c>_layout.uss</c> declares for them.
    /// </summary>
    [TestFixture]
    internal sealed class FlexFactorClassMirrorTests
    {
        private const string LayoutSheetPath = "Packages/com.velvet.core/Runtime/Styles/_layout.uss";

        [Test]
        public void Given_TheFlexFactorClassesTheSheetDeclares_When_ReadAsStylesheetClasses_Then_EachReadsAsItsDeclaredValue()
        {
            // Arrange — `.grow`, `.grow-0`, `.shrink` and `.shrink-0` each write their factor alone.
            var sheet = File.ReadAllText(Path.GetFullPath(LayoutSheetPath));
            var declared = Regex.Matches(sheet,
                    @"^\.(?<name>(grow|shrink)(-\d+)?) \{ flex-(grow|shrink): (?<value>\d+); \}", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => $"{match.Groups["name"].Value}={match.Groups["value"].Value}")
                .ToArray();

            // Act
            var read = declared
                .Select(entry => entry.Substring(0, entry.IndexOf('=')))
                .Select(name => StyleArbitraryValueResolver.TryReadStylesheetClass(name, out _, out var form)
                    && StyleArbitraryValueResolver.TryParse(form, out var style)
                        ? $"{name}={style.Value}"
                        : $"{name}=unread")
                .ToArray();

            // Assert
            Assert.That(read, Is.EqualTo(declared).And.Not.Empty);
        }
    }
}
