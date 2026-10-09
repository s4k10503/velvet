using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Tailwind's per-side border color utilities: <c>border-t-</c>, <c>border-r-</c>, <c>border-b-</c>,
    /// <c>border-l-</c>, <c>border-x-</c>, <c>border-y-</c> and the logical <c>border-s-</c>, <c>border-e-</c>,
    /// <c>border-bs-</c>, <c>border-be-</c>, each followed by a palette name, a bracketed color, or either of those
    /// with an opacity modifier.
    /// <list type="bullet">
    /// <item>Each family parses to the color of the sides it names, the logical ones read left to right and top
    /// to bottom as the logical width utilities are.</item>
    /// <item>A width, a color with no fixed value and an out-of-range modifier are not taken as a color.</item>
    /// <item>A per-side spelling is routed to the inline resolver; a width stays on the class list.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each assertion reads the longhands the parsed property writes rather than naming the property, so this
    /// file builds on a tree without the per-side members and its cases can be measured red there.
    /// </remarks>
    [TestFixture]
    internal sealed class BorderSideColorTests
    {
        #region Parse

        private static readonly object[] ParsedCases =
        {
            new object[] { "border-t-red-500", "BorderTopColor", "EF4444FF" },
            new object[] { "border-r-blue-500", "BorderRightColor", "3B82F6FF" },
            new object[] { "border-b-emerald-600", "BorderBottomColor", "059669FF" },
            new object[] { "border-l-amber-400", "BorderLeftColor", "FBBF24FF" },
            new object[] { "border-x-red-500", "BorderLeftColor+BorderRightColor", "EF4444FF" },
            new object[] { "border-y-red-500", "BorderBottomColor+BorderTopColor", "EF4444FF" },
            new object[] { "border-s-red-500", "BorderLeftColor", "EF4444FF" },
            new object[] { "border-e-red-500", "BorderRightColor", "EF4444FF" },
            new object[] { "border-bs-red-500", "BorderTopColor", "EF4444FF" },
            new object[] { "border-be-red-500", "BorderBottomColor", "EF4444FF" },
            new object[] { "border-b-white", "BorderBottomColor", "FFFFFFFF" },
            new object[] { "border-t-black", "BorderTopColor", "000000FF" },
            new object[] { "border-x-transparent", "BorderLeftColor+BorderRightColor", "00000000" },
            new object[] { "border-b-[#00ff00]", "BorderBottomColor", "00FF00FF" },
            new object[] { "border-y-[rgb(255,0,0)]", "BorderBottomColor+BorderTopColor", "FF0000FF" },
            new object[] { "border-s-[#0000ff]", "BorderLeftColor", "0000FFFF" },
            new object[] { "border-be-[#ff0000]", "BorderBottomColor", "FF0000FF" },
            new object[] { "border-t-red-500/40", "BorderTopColor", "EF444466" },
            new object[] { "border-x-[#00ff00]/40", "BorderLeftColor+BorderRightColor", "00FF0066" },
            new object[] { "border-e-red-500/[0.4]", "BorderRightColor", "EF444466" },
        };

        [TestCaseSource(nameof(ParsedCases))]
        public void Given_PerSideBorderColorClass_When_Parsed_Then_ResolvesTheColorOfTheSidesItNames(
            string className, string longhands, string rgba)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert — `ok` is part of the assertion so a regression that stops recognising the class goes red.
            Assert.That((ok, LonghandsOf(style.Property), ColorUtility.ToHtmlStringRGBA(style.Color)),
                Is.EqualTo((true, longhands, rgba)));
        }

        private static readonly string[] DeclinedClasses =
        {
            "border-t-current",    // no fixed value to write inline
            "border-x-inherit",
            "border-t-nope",       // not a color
            "border-b-red-500/101", // past the modifier's range
            "border-l-red-550",    // not a palette step
            "border-r-nope/40",    // a modifier on a color that is not one
        };

        // GREEN_ON_BASE(characterization): the base resolves no per-side color, so these refusals already hold
        // there. What reddens a case is `StyleBorderSideColor.TryParse` accepting what the palette refuses, as it
        // would if it returned true without asking `VelvetPalette.TryResolveColorToken`.
        [TestCaseSource(nameof(DeclinedClasses))]
        public void Given_PerSideClassWithNoColorItCanWrite_When_Parsed_Then_NothingResolves(string className)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already reads a per-side bracket length as a width. What
        // reddens it is `StyleBorderSideColor.TryParse` claiming a bracket `StyleColorValueParser.TryParseColor`
        // refuses, which would take the width away from the physical utility.
        [TestCase("border-t-[3px]", "BorderTopWidth")]
        [TestCase("border-s-[3px]", "BorderLeftWidth")]
        public void Given_PerSideBracketLength_When_Parsed_Then_ItStaysAWidth(string className, string longhands)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(className, out var style);

            // Assert
            Assert.That((ok, LonghandsOf(style.Property)), Is.EqualTo((true, longhands)));
        }

        #endregion

        #region Dispatch

        [TestCase("border-t-red-500")]
        [TestCase("border-x-white")]
        [TestCase("border-s-transparent")]
        [TestCase("border-be-slate-300")]
        public void Given_APerSidePaletteColorClass_When_AskedWhetherInlineResolved_Then_ItIs(string className)
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved(className);

            // Assert
            Assert.That(inline, Is.True);
        }

        // GREEN_ON_BASE(characterization): these are the stylesheet's own classes, which the base leaves on the
        // class list too. What reddens a case is the dispatch gate claiming a token whose value
        // `VelvetPalette.TryResolveColorToken` refuses.
        [TestCase("border-t-2")]
        [TestCase("border-x-4")]
        [TestCase("border-b")]
        [TestCase("border-t-current")]
        public void Given_AClassTheStylesheetResolves_When_AskedWhetherInlineResolved_Then_ItIsNot(
            string className)
        {
            // Act
            var inline = StyleArbitraryValueResolver.IsInlineResolved(className);

            // Assert
            Assert.That(inline, Is.False);
        }

        #endregion

        private static string LonghandsOf(ArbitraryProperty property)
        {
            var written = StyleArbitraryLonghands.Of(property);
            return string.Join("+", Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>()
                .Where(written.Contains)
                .Select(longhand => longhand.ToString())
                .OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// A mounted per-side border color class, off a panel: the side it colors, how it ranks against the
    /// <c>border-{color}</c> shorthand in Tailwind's property order, and how variants and the important modifier
    /// reach it.
    /// </summary>
    [TestFixture]
    internal sealed class BorderSideColorMountTests
    {
        private const string Red500 = "EF4444FF";
        private const string Blue = "0000FFFF";

        private readonly System.Collections.Generic.List<MountedTree> _others = new();
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp() => _root = new VisualElement();

        [TearDown]
        public void TearDown()
        {
            VelvetTheme.IsDark = false;
            _mounted?.Dispose();
            _mounted = null;
            foreach (var other in _others)
            {
                other.Dispose();
            }
            _others.Clear();
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

        private static string Rgba(StyleColor color) => ColorUtility.ToHtmlStringRGBA(color.value);

        [Test]
        public void Given_AShorthandColorThenABottomColor_When_Mounted_Then_OnlyTheBottomIsWrittenInline()
        {
            // Act
            var leaf = MountLeaf("border-2 border-slate-300 border-b-red-500");

            // Assert — the top is left to the border-slate-300 class.
            Assert.That((Rgba(leaf.style.borderBottomColor), leaf.style.borderTopColor.keyword,
                    leaf.ClassListContains("border-slate-300")),
                Is.EqualTo((Red500, StyleKeyword.Null, true)));
        }

        private VisualElement MountOther(VisualElement root, string className)
        {
            _others.Add(V.Mount(root, V.Div(name: "leaf", className: className)));
            return root.Q<VisualElement>("leaf");
        }

        [Test]
        public void Given_ABottomColorAndABracketShorthandInEitherOrder_When_Mounted_Then_TheBottomColorHoldsTheBottom()
        {
            // Act — both resolve inline at the base rank, where Tailwind's property order puts border-color first.
            var forward = MountLeaf("border-b-red-500 border-[#0000ff]");
            var reversed = MountOther(new VisualElement(), "border-[#0000ff] border-b-red-500");

            // Assert
            Assert.That((Rgba(forward.style.borderBottomColor), Rgba(reversed.style.borderBottomColor)),
                Is.EqualTo((Red500, Red500)));
        }

        [Test]
        public void Given_ALeftColorAndAnInlineStartColorInEitherOrder_When_Mounted_Then_TheLeftColorHoldsTheLeft()
        {
            // Act — border-inline-start-color sorts ahead of border-left-color.
            var forward = MountLeaf("border-l-[#0000ff] border-s-red-500");
            var reversed = MountOther(new VisualElement(), "border-s-red-500 border-l-[#0000ff]");

            // Assert
            Assert.That((Rgba(forward.style.borderLeftColor), Rgba(reversed.style.borderLeftColor)),
                Is.EqualTo((Blue, Blue)));
        }

        [Test]
        public void Given_AnInlineAxisColor_When_Mounted_Then_TheLeftAndRightAreWrittenAndTheTopIsNot()
        {
            // Act
            var leaf = MountLeaf("border-x-red-500");

            // Assert
            Assert.That((Rgba(leaf.style.borderLeftColor), Rgba(leaf.style.borderRightColor),
                    leaf.style.borderTopColor.keyword, leaf.ClassListContains("border-x-red-500")),
                Is.EqualTo((Red500, Red500, StyleKeyword.Null, false)));
        }

        [Test]
        public void Given_ABlockStartBracketColor_When_Mounted_Then_TheTopIsWritten()
        {
            // Act
            var leaf = MountLeaf("border-bs-[#0000ff]");

            // Assert
            Assert.That((Rgba(leaf.style.borderTopColor), leaf.style.borderBottomColor.keyword),
                Is.EqualTo((Blue, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AnInlineEndColorWithAnOpacityModifier_When_Mounted_Then_TheRightTakesTheAlpha()
        {
            // Act
            var leaf = MountLeaf("border-e-red-500/40");

            // Assert
            Assert.That(Rgba(leaf.style.borderRightColor), Is.EqualTo("EF444466"));
        }

        [Test]
        public void Given_AHoverBottomColor_When_Hovered_Then_TheBottomIsColoredOnlyWhileHovered()
        {
            // Arrange
            var leaf = MountLeaf("border-slate-300 hover:border-b-red-500");
            var before = leaf.style.borderBottomColor.keyword;

            // Act
            Hover(leaf);

            // Assert
            Assert.That((before, Rgba(leaf.style.borderBottomColor)), Is.EqualTo((StyleKeyword.Null, Red500)));
        }

        [Test]
        public void Given_ABaseBottomColorAndAHoverShorthandColor_When_Hovered_Then_TheShorthandClassTakesTheBottom()
        {
            // Arrange — the hover rule outranks the base one, so its border-color reaches the bottom too.
            var leaf = MountLeaf("border-b-red-500 hover:border-blue-500");
            var before = Rgba(leaf.style.borderBottomColor);

            // Act
            Hover(leaf);

            // Assert
            Assert.That((before, leaf.style.borderBottomColor.keyword, leaf.ClassListContains("border-blue-500")),
                Is.EqualTo((Red500, StyleKeyword.Null, true)));
        }

        [Test]
        public void Given_AnImportantBottomColorAndAHoverShorthandColor_When_Hovered_Then_TheImportantBottomHolds()
        {
            // Arrange
            var leaf = MountLeaf("!border-b-red-500 hover:border-blue-500");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((Rgba(leaf.style.borderBottomColor), leaf.ClassListContains("border-blue-500")),
                Is.EqualTo((Red500, true)));
        }

        [Test]
        public void Given_AHoverBottomColorBeforeAHoverBracketShorthand_When_Hovered_Then_TheBottomColorWins()
        {
            // Arrange — border-color sorts ahead of border-bottom-color, so the per-side rule is emitted later.
            var leaf = MountLeaf("hover:border-b-red-500 hover:border-[#0000ff]");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((Rgba(leaf.style.borderBottomColor), Rgba(leaf.style.borderTopColor)),
                Is.EqualTo((Red500, Blue)));
        }

        [Test]
        public void Given_ADarkBottomColor_When_TheThemeTurnsDark_Then_TheBottomIsColoredOnlyThen()
        {
            // Arrange
            var leaf = MountLeaf("dark:border-b-red-500");
            var before = leaf.style.borderBottomColor.keyword;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((before, Rgba(leaf.style.borderBottomColor)), Is.EqualTo((StyleKeyword.Null, Red500)));
        }

        [Test]
        public void Given_AHoverInlineStartColorBeforeAHoverBracketShorthand_When_Hovered_Then_TheStartColorWins()
        {
            // Arrange — border-color sorts ahead of border-inline-start-color.
            var leaf = MountLeaf("hover:border-s-red-500 hover:border-[#0000ff]");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((Rgba(leaf.style.borderLeftColor), Rgba(leaf.style.borderRightColor)),
                Is.EqualTo((Red500, Blue)));
        }

        [Test]
        public void Given_AHoverLeftColorBeforeAHoverInlineStartColor_When_Hovered_Then_TheLeftColorWins()
        {
            // Arrange — border-inline-start-color sorts ahead of border-left-color, so border-l is emitted later
            // whichever of the two the className writes first.
            var leaf = MountLeaf("hover:border-l-[#0000ff] hover:border-s-red-500");

            // Act
            Hover(leaf);

            // Assert
            Assert.That(Rgba(leaf.style.borderLeftColor), Is.EqualTo(Blue));
        }

        [Test]
        public void Given_AHoverBlockStartColorBeforeAHoverBlockAxisColor_When_Hovered_Then_TheBlockStartColorWins()
        {
            // Arrange — border-block-color sorts ahead of border-block-start-color.
            var leaf = MountLeaf("hover:border-bs-[#0000ff] hover:border-y-red-500");

            // Act
            Hover(leaf);

            // Assert
            Assert.That((Rgba(leaf.style.borderTopColor), Rgba(leaf.style.borderBottomColor)),
                Is.EqualTo((Blue, Red500)));
        }
    }

    /// <summary>
    /// A <c>group-hover:</c> per-side border color, which needs a panel for the relational variant to find its
    /// <c>group</c> source.
    /// </summary>
    [TestFixture]
    internal sealed class BorderSideColorGroupVariantPanelTests : PanelTestBase
    {
        [Test]
        public void Given_AGroupHoverBottomColor_When_TheGroupIsHovered_Then_TheBottomIsColoredOnlyThen()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div("group", V.Div(name: "child", className: "group-hover:border-b-red-500")));
            var source = _window.rootVisualElement.Q<VisualElement>(className: "group");
            var child = _window.rootVisualElement.Q<VisualElement>("child");
            var before = child.style.borderBottomColor.keyword;

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                source.SimulateEvent(over);
            }

            // Assert
            Assert.That((before, ColorUtility.ToHtmlStringRGBA(child.style.borderBottomColor.value)),
                Is.EqualTo((StyleKeyword.Null, "EF4444FF")));
        }
    }
}
