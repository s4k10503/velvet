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

        // A flex shorthand result as one string: recognized, grow, shrink, basis and its unit, "auto" for a NaN
        // basis. The property is told by the longhands it writes, which the base can read too.
        private static string Flex(bool ok, ArbitraryStyle s)
            => !ok || StyleArbitraryLonghands.Of(s.Property) != s_flexLonghands
                ? "unrecognized"
                : $"{s.Value} {s.Value3} " + (float.IsNaN(s.Value2) ? "auto" : $"{s.Value2}{s.Unit2}");

        [TestCase("flex-12", "12 1 0Percent")]
        [TestCase("flex-0", "0 1 0Percent")]
        [TestCase("flex-3/4", "1 1 75Percent")]
        [TestCase("flex-[2_3_10px]", "2 3 10Pixel")]
        [TestCase("flex-[10px_2_3]", "2 3 10Pixel")]
        [TestCase("flex-[2_0]", "2 0 0Percent")]
        [TestCase("flex-[2_auto]", "2 1 auto")]
        [TestCase("flex-[2_AUTO]", "2 1 auto")]
        [TestCase("flex-[50%]", "1 1 50Percent")]
        [TestCase("flex-[none]", "0 0 auto")]
        [TestCase("flex-[NONE]", "0 0 auto")]
        [TestCase("flex-[auto]", "1 1 auto")]
        [TestCase("flex-[initial]", "0 1 auto")]
        [TestCase("flex-[unset]", "0 1 auto")]
        [TestCase("flex-[revert]", "0 1 auto")]
        [TestCase("flex-[1_1_0]", "1 1 0Pixel")]
        [TestCase("flex-[2_0%]", "2 1 0Percent")]
        public void Given_AFlexShorthandUtility_When_Parsed_Then_ItResolvesGrowShrinkAndBasis(string cls, string expected)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert — grow, shrink, then the basis and its unit.
            Assert.That(Flex(ok, s), Is.EqualTo(expected));
        }

        [TestCase("flex-[2_3_2em]")]
        [TestCase("flex-[50vw_2_3]")]
        [TestCase("flex-[2_3_calc(50%+10px)]")]
        public void Given_AFlexShorthandWithABasisTheElementMeasures_When_Parsed_Then_ItCarriesTheFactorsAndTheMeasuredBasis(
            string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert — grow and shrink, then whether the basis is left to the element to measure.
            Assert.That(Flex(ok, s) + $" {s.Expression != null}", Is.EqualTo("2 3 0Pixel True"));
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
            var keys = (FiberNodePatcher.ValueKey("flex-[2_1]"), FiberNodePatcher.ValueKey("flex-[2_3]"));

            // Assert
            Assert.That(keys.Item1, Is.Not.EqualTo(keys.Item2));
        }

        [Test]
        public void Given_AFlexAutoBracket_When_AppliedToElement_Then_TheBasisIsAuto()
        {
            // Arrange
            var el = new VisualElement();
            var ok = StyleArbitraryValueResolver.TryParse("flex-[auto]", out var style);

            // Act
            StyleArbitraryValueResolver.Apply(el, in style);

            // Assert
            Assert.That((ok, el.style.flexBasis.keyword), Is.EqualTo((true, StyleKeyword.Auto)));
        }

        [Test]
        public void Given_AFlexShorthand_When_Cleared_Then_TheThreeLonghandsRevertToNull()
        {
            // Arrange
            var el = new VisualElement();
            StyleArbitraryValueResolver.TryParse("flex-[2_3_10px]", out var style);
            StyleArbitraryValueResolver.Apply(el, style);
            var applied = el.style.flexShrink.value;

            // Act
            StyleArbitraryValueResolver.Clear(el, style.Property);

            // Assert — the applied shrink rides along, since a fresh element already reads Null.
            Assert.That((applied, el.style.flexGrow.keyword, el.style.flexShrink.keyword, el.style.flexBasis.keyword),
                Is.EqualTo((3f, StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
        }

        // The element's inline grow, shrink and basis as one string.
        private static string InlineFlex(VisualElement element)
        {
            var style = element.style;
            return $"{style.flexGrow.value} {style.flexShrink.value} "
                + $"{style.flexBasis.value.value}{style.flexBasis.value.unit}";
        }

        private static VisualElement MountDiv(Velvet.TestUtilities.ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className) });
            return scope.Root[0];
        }

        [Test]
        public void Given_ADivCarryingABareFlexNumber_When_Reconciled_Then_TheElementCarriesTheThreeLonghands()
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-3");

            // Assert
            Assert.That(InlineFlex(div), Is.EqualTo("3 1 0Percent"));
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
        [Test]
        public void Given_AFlexNoneClassBeforeAFlexNumberInOneClassName_When_Mounted_Then_TheClassSetsAllThree()
        {
            // Arrange — Tailwind orders flex-none after flex-2, so the class wins wherever it is listed.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, "flex-none flex-2");

            // Assert
            var style = div.style;
            Assert.That((style.flexGrow.keyword, style.flexShrink.keyword, style.flexBasis.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null, StyleKeyword.Null)));
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
        [TestCase("flex-2 shrink-0", "2 0 0Percent")]
        [TestCase("shrink-0 flex-2", "2 0 0Percent")]
        [TestCase("grow-0 flex-[3_2]", "0 2 0Percent")]
        [TestCase("grow-[3] flex-2", "3 1 0Percent")]
        [TestCase("basis-4 flex-2", "2 1 16Pixel")]
        [TestCase("basis-[20px] flex-[2_1_10px]", "2 1 20Pixel")]
        public void Given_AFlexLonghandBesideTheShorthand_When_Mounted_Then_TheLonghandTakesItsPart(
            string className, string expected)
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            var div = MountDiv(scope, className);

            // Assert
            Assert.That(InlineFlex(div), Is.EqualTo(expected));
        }

        private static readonly Dictionary<string, MotionVariant> s_narrowPose = new()
        {
            ["open"] = "w-[100px] flex-2",
            ["closed"] = "",
        };

        [Test]
        public void Given_AMotionWhosePoseRepeatsItsClassNamesShorthand_When_MountedAtThePose_Then_ThePoseSetsTheGrow()
        {
            // Arrange — a pose overrides the node's own className, as Framer Motion's animate does its style.
            using var scope = new Velvet.TestUtilities.ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Motion(className: "w-[200px] flex-[3]", variants: s_narrowPose, animate: "open",
                        transition: StyleTransitionConfig.None),
                });

            // Assert
            var motion = scope.Root[0];
            Assert.That((motion.style.width.value.value, motion.style.flexGrow.value), Is.EqualTo((100f, 2f)));
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
