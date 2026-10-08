using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind's logical-direction utilities (<c>ms-*</c>, <c>pe-*</c>, <c>start-*</c>,
    /// <c>inset-bs-*</c>, <c>border-s-*</c>, <c>rounded-ss-*</c>, ...), which resolve as the physical utility
    /// they equal when the inline axis runs left to right and the block axis top to bottom.
    /// <list type="bullet">
    /// <item>Each family parses to the physical property it stands for, for the scale, the bracket form, the
    /// negated form where Tailwind negates, and for an inset the fractions and <c>full</c>.</item>
    /// <item>A family does not take a form Tailwind gives it no utility for, and a class that only starts like
    /// a logical one is left to its own owner.</item>
    /// <item>A mounted logical class writes inline style and stays off the class list, under a variant, under
    /// the important modifier, and as <c>auto</c>.</item>
    /// <item>Two rules of one variant rank order by the logical property Tailwind sorts the logical utility
    /// by, and a physical shorthand sorts ahead of it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class LogicalUtilityTests
    {
        #region Parse

        private static readonly object[] ParsedCases =
        {
            new object[] { "ms-4", ArbitraryProperty.MarginLeft, 16f, LengthUnit.Pixel },
            new object[] { "me-2", ArbitraryProperty.MarginRight, 8f, LengthUnit.Pixel },
            new object[] { "mbs-1", ArbitraryProperty.MarginTop, 4f, LengthUnit.Pixel },
            new object[] { "mbe-px", ArbitraryProperty.MarginBottom, 1f, LengthUnit.Pixel },
            new object[] { "ms-0", ArbitraryProperty.MarginLeft, 0f, LengthUnit.Pixel },
            new object[] { "ms-2-5", ArbitraryProperty.MarginLeft, 10f, LengthUnit.Pixel },
            new object[] { "-ms-4", ArbitraryProperty.MarginLeft, -16f, LengthUnit.Pixel },
            new object[] { "-me-px", ArbitraryProperty.MarginRight, -1f, LengthUnit.Pixel },
            new object[] { "ms-[10px]", ArbitraryProperty.MarginLeft, 10f, LengthUnit.Pixel },
            new object[] { "-ms-[3px]", ArbitraryProperty.MarginLeft, -3f, LengthUnit.Pixel },
            new object[] { "me-[5%]", ArbitraryProperty.MarginRight, 5f, LengthUnit.Percent },
            new object[] { "ps-4", ArbitraryProperty.PaddingLeft, 16f, LengthUnit.Pixel },
            new object[] { "pe-2", ArbitraryProperty.PaddingRight, 8f, LengthUnit.Pixel },
            new object[] { "pbs-3", ArbitraryProperty.PaddingTop, 12f, LengthUnit.Pixel },
            new object[] { "pbe-0", ArbitraryProperty.PaddingBottom, 0f, LengthUnit.Pixel },
            new object[] { "ps-[7px]", ArbitraryProperty.PaddingLeft, 7f, LengthUnit.Pixel },
            new object[] { "start-4", ArbitraryProperty.Left, 16f, LengthUnit.Pixel },
            new object[] { "end-8", ArbitraryProperty.Right, 32f, LengthUnit.Pixel },
            new object[] { "inset-s-px", ArbitraryProperty.Left, 1f, LengthUnit.Pixel },
            new object[] { "inset-e-0", ArbitraryProperty.Right, 0f, LengthUnit.Pixel },
            new object[] { "inset-bs-2", ArbitraryProperty.Top, 8f, LengthUnit.Pixel },
            new object[] { "inset-be-1-5", ArbitraryProperty.Bottom, 6f, LengthUnit.Pixel },
            new object[] { "-start-4", ArbitraryProperty.Left, -16f, LengthUnit.Pixel },
            new object[] { "start-1/2", ArbitraryProperty.Left, 50f, LengthUnit.Percent },
            new object[] { "end-3/4", ArbitraryProperty.Right, 75f, LengthUnit.Percent },
            new object[] { "inset-bs-full", ArbitraryProperty.Top, 100f, LengthUnit.Percent },
            new object[] { "-inset-be-full", ArbitraryProperty.Bottom, -100f, LengthUnit.Percent },
            new object[] { "-start-1/4", ArbitraryProperty.Left, -25f, LengthUnit.Percent },
            new object[] { "inset-e-[5px]", ArbitraryProperty.Right, 5f, LengthUnit.Pixel },
            new object[] { "border-s", ArbitraryProperty.BorderLeftWidth, 1f, LengthUnit.Pixel },
            new object[] { "border-e-2", ArbitraryProperty.BorderRightWidth, 2f, LengthUnit.Pixel },
            new object[] { "border-bs-4", ArbitraryProperty.BorderTopWidth, 4f, LengthUnit.Pixel },
            new object[] { "border-be-8", ArbitraryProperty.BorderBottomWidth, 8f, LengthUnit.Pixel },
            new object[] { "border-s-0", ArbitraryProperty.BorderLeftWidth, 0f, LengthUnit.Pixel },
            new object[] { "border-s-[3px]", ArbitraryProperty.BorderLeftWidth, 3f, LengthUnit.Pixel },
            new object[] { "rounded-s-lg", ArbitraryProperty.BorderLeftRadius, 8f, LengthUnit.Pixel },
            new object[] { "rounded-e", ArbitraryProperty.BorderRightRadius, 4f, LengthUnit.Pixel },
            new object[] { "rounded-ss-md", ArbitraryProperty.BorderTopLeftRadius, 6f, LengthUnit.Pixel },
            new object[] { "rounded-se-none", ArbitraryProperty.BorderTopRightRadius, 0f, LengthUnit.Pixel },
            new object[] { "rounded-es-xl", ArbitraryProperty.BorderBottomLeftRadius, 12f, LengthUnit.Pixel },
            new object[] { "rounded-ee-full", ArbitraryProperty.BorderBottomRightRadius, 9999f, LengthUnit.Pixel },
            new object[] { "rounded-s-[12px]", ArbitraryProperty.BorderLeftRadius, 12f, LengthUnit.Pixel },
        };

        [TestCaseSource(nameof(ParsedCases))]
        public void Given_LogicalClass_When_Parsed_Then_ResolvesThePhysicalPropertyValueAndUnit(
            string className, object property, float value, LengthUnit unit)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert — `ok` is part of the assertion so a regression that stops recognising the class goes red.
            Assert.That((ok, style.Property, style.Value, style.Unit),
                Is.EqualTo((true, (ArbitraryProperty)property, value, unit)));
        }

        private static readonly object[] AutoCases =
        {
            new object[] { "ms-auto", ArbitraryProperty.MarginLeft },
            new object[] { "me-auto", ArbitraryProperty.MarginRight },
            new object[] { "mbs-auto", ArbitraryProperty.MarginTop },
            new object[] { "mbe-auto", ArbitraryProperty.MarginBottom },
            new object[] { "start-auto", ArbitraryProperty.Left },
            new object[] { "inset-be-auto", ArbitraryProperty.Bottom },
        };

        [TestCaseSource(nameof(AutoCases))]
        public void Given_AutoLogicalClass_When_Parsed_Then_ResolvesTheKeywordOnThePhysicalProperty(
            string className, object property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert
            Assert.That((ok, style.Property), Is.EqualTo((true, (ArbitraryProperty)property)));
        }

        private static readonly string[] DeclinedClasses =
        {
            "ps-auto",       // padding has no auto
            "-ps-4",         // of these families Tailwind negates margin and inset
            "-border-s-2",
            "-rounded-s-lg",
            "ms-1/2",        // a margin takes no fraction
            "ps-full",
            "rounded-s-1/2",
            "ms-13",         // not on the spacing scale
            "-start-auto",   // Tailwind has no negative auto
            "-ms-auto",
            "start-1/0",     // no percent stands for a zero denominator
            "start-01/2",    // a fraction's halves carry no leading zero
            "border-s-3",    // not on the border-width scale
            "rounded-s-huge",
            "ms-",
            "ms-[]",
        };

        // GREEN_ON_BASE(characterization): the base resolves none of these classes either, so the refusals
        // already hold there. What shows a case can fail is `IsNegatable` answering true for every scale in
        // `StyleLogicalUtilities.Family`, which resolves `-ps-4`, `-border-s-2` and `-rounded-s-lg`.
        [TestCaseSource(nameof(DeclinedClasses))]
        public void Given_ClassTheFamilyDoesNotTake_When_Parsed_Then_NothingResolves(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        #endregion

        #region Dispatch

        [TestCase("ms-4")]
        [TestCase("-ms-4")]
        [TestCase("ps-0")]
        [TestCase("start-1/2")]
        [TestCase("inset-s-auto")]
        [TestCase("border-s")]
        [TestCase("rounded-e")]
        [TestCase("rounded-ss-lg")]
        public void Given_LogicalClassWithNoBracket_When_AskedWhetherInlineResolved_Then_ItIs(string className)
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved(className);

            // Assert
            Assert.That(inline, Is.True);
        }

        // GREEN_ON_BASE(characterization): these classes only start the way a logical one does and are other
        // utilities' to resolve, so the base leaves them off the inline path too. What shows a case can fail
        // is `TryFindFamily` in `StyleLogicalUtilities` dropping its `body[name.Length] == '-'` test, which
        // claims `rounded-sm` for `rounded-s`.
        [TestCase("rounded-sm")]
        [TestCase("border-solid")]
        [TestCase("inset-shadow-sm")]
        [TestCase("pe-none")]
        [TestCase("start")]
        public void Given_ClassThatOnlyStartsLikeALogicalOne_When_AskedWhetherInlineResolved_Then_ItIsNot(
            string className)
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved(className);

            // Assert
            Assert.That(inline, Is.False);
        }

        #endregion

        #region Rule order

        [Test]
        public void Given_AMarginAndALogicalMarginOfOneVariant_When_Ordered_Then_TheLogicalOneIsEmittedFirst()
        {
            // Arrange — Tailwind sorts margin-inline-start ahead of margin-left, so ml-8 is emitted later and
            // wins the edge whichever of the two the className writes first.
            var classNames = new[] { "hover:ml-8", "hover:ms-4" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(classNames);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 1, 0 }));
        }

        [Test]
        public void Given_RadiiOfOneVariantFromShorthandToCorner_When_Ordered_Then_TheyFollowTailwindsPropertyOrder()
        {
            // Arrange — border-radius, then border-start-start-radius and border-end-start-radius, then
            // border-top-left-radius.
            var classNames = new[] { "hover:rounded-tl-lg", "hover:rounded-s-none", "hover:rounded-lg" };

            // Act
            var ordinals = StyleRuleOrder.OrdinalsOf(classNames);

            // Assert
            Assert.That(ordinals, Is.EqualTo(new[] { 2, 1, 0 }));
        }

        #endregion
    }

    /// <summary>
    /// A mounted logical class, off a panel: the inline style it writes, what it leaves off the class list, and
    /// how it ranks against the physical utility it equals.
    /// </summary>
    [TestFixture]
    internal sealed class LogicalUtilityMountTests
    {
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private VisualElement MountLeaf(string className)
        {
            _mounted = V.Mount(_root, V.Div(name: "leaf", className: className));
            return _root.Q<VisualElement>("leaf");
        }

        private static void Hover(VisualElement element)
        {
            using var over = PointerOverEvent.GetPooled();
            element.SimulateEvent(over);
        }

        [Test]
        public void Given_AMsClass_When_Mounted_Then_TheMarginLeftIsInlineAndTheClassIsNotListed()
        {
            // Act
            var leaf = MountLeaf("ms-4");

            // Assert
            Assert.That((leaf.style.marginLeft.value.value, leaf.ClassListContains("ms-4")),
                Is.EqualTo((16f, false)));
        }

        [Test]
        public void Given_ANegatedMeClass_When_Mounted_Then_TheMarginRightIsNegative()
        {
            // Act
            var leaf = MountLeaf("-me-2");

            // Assert
            Assert.That(leaf.style.marginRight.value.value, Is.EqualTo(-8f));
        }

        [Test]
        public void Given_APsBracketClass_When_Mounted_Then_ThePaddingLeftIsInline()
        {
            // Act
            var leaf = MountLeaf("ps-[12px]");

            // Assert
            Assert.That(leaf.style.paddingLeft.value.value, Is.EqualTo(12f));
        }

        [Test]
        public void Given_AStartFraction_When_Mounted_Then_TheLeftIsAnInlinePercent()
        {
            // Act
            var leaf = MountLeaf("start-1/2");

            // Assert
            Assert.That((leaf.style.left.value.value, leaf.style.left.value.unit),
                Is.EqualTo((50f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_AnInsetBlockEndClass_When_Mounted_Then_TheBottomIsInline()
        {
            // Act
            var leaf = MountLeaf("inset-be-4");

            // Assert
            Assert.That(leaf.style.bottom.value.value, Is.EqualTo(16f));
        }

        [Test]
        public void Given_ABorderStartClass_When_Mounted_Then_TheLeftBorderWidthIsInline()
        {
            // Act
            var leaf = MountLeaf("border-s-4");

            // Assert
            Assert.That(leaf.style.borderLeftWidth.value, Is.EqualTo(4f));
        }

        [Test]
        public void Given_AMsAutoClass_When_Mounted_Then_TheMarginLeftIsAuto()
        {
            // Act
            var leaf = MountLeaf("ms-auto");

            // Assert
            Assert.That(leaf.style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Auto));
        }

        [Test]
        public void Given_AHoverMsClass_When_Hovered_Then_TheMarginLeftAppliesOnlyWhileHovered()
        {
            // Arrange
            var leaf = MountLeaf("hover:ms-4");
            var before = leaf.style.marginLeft.keyword;

            // Act
            Hover(leaf);

            // Assert
            Assert.That((before, leaf.style.marginLeft.value.value), Is.EqualTo((StyleKeyword.Null, 16f)));
        }

        [Test]
        public void Given_AnImportantMsClassAndAHoverMl_When_Hovered_Then_TheImportantMarginKeepsTheEdge()
        {
            // Arrange
            var leaf = MountLeaf("!ms-4 hover:ml-8");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.marginLeft.value.value, leaf.ClassListContains("ml-8")),
                Is.EqualTo((16f, false)));
        }

        [Test]
        public void Given_ABaseMlClassAndABaseMsClass_When_Mounted_Then_TheInlineMsHoldsTheEdge()
        {
            // Act — two base utilities on one edge tie, and an inline value outranks a stylesheet class.
            var leaf = MountLeaf("ml-8 ms-4");

            // Assert
            Assert.That((leaf.style.marginLeft.value.value, leaf.ClassListContains("ml-8")),
                Is.EqualTo((16f, true)));
        }

        [Test]
        public void Given_AHoverMlAndAHoverMs_When_Hovered_Then_TheMlClassWinsTheEdge()
        {
            // Arrange — ml-8 is emitted after ms-4, so the class outranks the inline margin-inline-start.
            var leaf = MountLeaf("hover:ms-4 hover:ml-8");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.marginLeft.keyword, leaf.ClassListContains("ml-8")),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [Test]
        public void Given_AHoverMlAndAHoverMsWrittenTheOtherWayRound_When_Hovered_Then_TheMlClassStillWinsTheEdge()
        {
            // Arrange
            var leaf = MountLeaf("hover:ml-8 hover:ms-4");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.marginLeft.keyword, leaf.ClassListContains("ml-8")),
                Is.EqualTo((StyleKeyword.Null, true)));
        }

        [Test]
        public void Given_AHoverMarginAndAHoverMs_When_Hovered_Then_TheLogicalMarginWinsItsEdge()
        {
            // Arrange — margin sorts ahead of margin-inline-start, so ms-2 is emitted later than m-4.
            var leaf = MountLeaf("hover:m-4 hover:ms-2");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.marginLeft.value.value, leaf.ClassListContains("m-4")),
                Is.EqualTo((8f, true)));
        }

        [Test]
        public void Given_AHoverPaddingXAndAHoverPs_When_Hovered_Then_TheLogicalPaddingWinsItsEdge()
        {
            // Arrange — padding-inline sorts ahead of padding-inline-start.
            var leaf = MountLeaf("hover:px-4 hover:ps-2");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.paddingLeft.value.value, leaf.ClassListContains("px-4")),
                Is.EqualTo((8f, true)));
        }

        [Test]
        public void Given_AHoverInsetAndAHoverStart_When_Hovered_Then_TheLogicalInsetWinsItsEdge()
        {
            // Arrange — inset sorts ahead of inset-inline-start.
            var leaf = MountLeaf("hover:inset-0 hover:start-4");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.left.value.value, leaf.ClassListContains("inset-0")),
                Is.EqualTo((16f, true)));
        }

        [Test]
        public void Given_AHoverBorderAndAHoverBorderStart_When_Hovered_Then_TheLogicalWidthWinsItsEdge()
        {
            // Arrange — border-width sorts ahead of border-inline-start-width.
            var leaf = MountLeaf("hover:border-2 hover:border-s-4");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((leaf.style.borderLeftWidth.value, leaf.ClassListContains("border-2")),
                Is.EqualTo((4f, true)));
        }
    }

    /// <summary>
    /// The scales <see cref="StyleLogicalUtilities"/> carries in C#, against the stylesheet declarations of the
    /// physical utilities they stand in for.
    /// </summary>
    [TestFixture]
    internal sealed class LogicalUtilityStyleSheetMirrorTests
    {
        private const string BordersSheetPath = "Packages/com.velvet.core/Runtime/Styles/_borders.uss";
        private const string TokensSheetPath = "Packages/com.velvet.core/Runtime/Styles/_tokens.uss";

        [Test]
        public void Given_TheBorderLeftWidthsTheSheetDeclares_When_BorderStartIsParsedAtEach_Then_TheWidthsAreTheSame()
        {
            // Arrange — `.border-l` and `.border-l-N` each write the left width alone.
            var sheet = File.ReadAllText(Path.GetFullPath(BordersSheetPath));
            var declared = Regex.Matches(sheet,
                    @"^\.border-l(?<suffix>(-\d+)?) \{ border-left-width: (?<px>\d+)px; \}", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => (Suffix: match.Groups["suffix"].Value, Px: float.Parse(match.Groups["px"].Value)))
                .ToArray();

            // Act
            var resolved = declared
                .Select(width => StyleArbitraryValueResolver.TryParse("border-s" + width.Suffix, out var style)
                    ? (width.Suffix, style.Value)
                    : (width.Suffix, float.NaN))
                .ToArray();

            // Assert
            Assert.That(resolved, Is.EqualTo(declared).And.Not.Empty);
        }

        [Test]
        public void Given_TheFullRadiusTheTokenSheetDeclares_When_RoundedStartFullIsParsed_Then_TheRadiusIsTheSame()
        {
            // Arrange
            var sheet = File.ReadAllText(Path.GetFullPath(TokensSheetPath));
            var declared = float.Parse(Regex.Match(sheet, @"--radius-full:\s*(?<px>\d+)px").Groups["px"].Value);

            // Act
            StyleArbitraryValueResolver.TryParse("rounded-s-full", out var style);

            // Assert
            Assert.That(style.Value, Is.EqualTo(declared));
        }
    }
}
