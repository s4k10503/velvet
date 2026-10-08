using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>text-balance</c> and <c>text-pretty</c> on a real panel, so the text is measured: they break the
    /// lines of the text a leaf DISPLAYS, leave the box at the width the cascade gave it, leave the text the
    /// leaf was given alone, and reach the leaves under the element carrying the class. Each case compares a
    /// styled leaf with a plain sibling in an identical wrapper. The wrapper is sized after mount, from the
    /// plain leaf's own measure of the text before the last word, so a last word alone on its line exists in
    /// whatever font the host renders with. Which breaks are chosen is <see cref="TextLineBreakerTests"/>'s.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextWrapBreakPanelTests : PanelTestBase
    {
        private const string Head = "aaaa bbbb cccc dddd";

        // Where the styled leaf sits: stretched in a column, as a box sized by its text (items start), inside
        // a ScrollView's inner box, or in a row beside a sibling of a declared width.
        private enum Layout
        {
            Column,
            Hug,
            Scroll,
            Row,
        }

        private static readonly Func<VisualElement, Action> s_alignStart = element =>
        {
            element.style.alignItems = Align.FlexStart;
            return null;
        };

        private static readonly Func<VisualElement, Action> s_row = element =>
        {
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.FlexStart;
            return null;
        };

        private const float SiblingWidthPx = 100f;

        private static StateUpdater<float> s_setWrapperWidth;
        private static StateUpdater<float> s_setCeiling;
        private static StateUpdater<string> s_setText;
        private static string s_text;
        private static string s_containerClass;
        private static string s_leafClass;
        private static Layout s_layout;

        protected override Rect WindowSize => new Rect(0, 0, 800, 400);

        public override void SetUp()
        {
            s_setWrapperWidth = default;
            s_setCeiling = default;
            s_setText = default;
            s_layout = Layout.Column;
            base.SetUp();
        }

        [Component(Compiler = false)]
        private static VNode RenderPair()
        {
            var (width, setWidth) = Hooks.UseState(700f);
            s_setWrapperWidth = setWidth;
            var (ceiling, setCeiling) = Hooks.UseState(0f);
            s_setCeiling = setCeiling;
            var (text, setText) = Hooks.UseState(s_text);
            s_setText = setText;
            var bound = ceiling > 0f ? $" max-w-[{ceiling}px]" : "";
            return V.Div(children: new VNode[]
            {
                Wrap($"w-[{width}px]", false, V.Label(name: "plain", className: "text-wrap" + bound, text: text)),
                Wrap($"w-[{width}px] {s_containerClass}", true,
                    V.Label(name: "styled", className: (s_leafClass + bound).Trim(), text: text)),
            });
        }

        private static VNode Wrap(string className, bool styled, VNode leaf)
        {
            switch (s_layout)
            {
                case Layout.Hug:
                    return V.Div(className: className, refCallback: s_alignStart, children: new[] { leaf });
                case Layout.Scroll:
                    return V.Div(className: className, children: new VNode[] { V.ScrollView("", leaf) });
                case Layout.Row:
                    return V.Div(className: className, refCallback: s_row, children: styled
                        ? new[] { V.Label(name: "sibling", className: $"w-[{SiblingWidthPx}px]", text: "sib"), leaf }
                        : new[] { leaf });
                default:
                    return V.Div(className: className, children: new[] { leaf });
            }
        }

        private void Settle()
        {
            for (var i = 0; i < 4; i++)
            {
                _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
                ForcePanelUpdate(_window.rootVisualElement.panel);
            }
        }

        // Mounts the pair and narrows both wrappers to the first whole-pixel content width at which the plain
        // label's own measure puts Head on one line and the full text on more, so its last word sits on a
        // line of its own. Arranged is false when no width in range does, which the cases report rather than
        // read past.
        private (Label plain, Label styled, bool arranged, string measured) MountWithOrphan(
            string lastWord, string containerClass, string leafClass, Layout layout = Layout.Column,
            bool ceiling = false, string head = Head)
        {
            s_containerClass = containerClass;
            s_leafClass = leafClass;
            s_layout = layout;
            s_text = head + " " + lastWord;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPair));
            Settle();
            var plain = _window.rootVisualElement.Q<Label>("plain");
            var oneLine = Height(plain, head, float.NaN);
            var headWidth = Mathf.Ceil(Width(plain, head));
            var fullWidth = Mathf.Ceil(Width(plain, s_text));
            var content = float.NaN;
            for (var width = headWidth; width <= fullWidth; width++)
            {
                if (Height(plain, head, width) <= oneLine + 0.5f && Height(plain, s_text, width) > oneLine + 0.5f)
                {
                    content = width;
                    break;
                }
            }
            var style = plain.resolvedStyle;
            var frame = style.marginLeft + style.marginRight + style.paddingLeft + style.paddingRight
                + style.borderLeftWidth + style.borderRightWidth;
            var arranged = !float.IsNaN(content);
            if (arranged && ceiling)
            {
                s_setCeiling.Invoke(content + frame);
                Settle();
            }
            else if (arranged)
            {
                s_setWrapperWidth.Invoke(content + frame);
                Settle();
            }
            var styled = _window.rootVisualElement.Q<Label>("styled");
            var measured = $"line {oneLine} head {headWidth} full {fullWidth} content {content} frame {frame} "
                + $"plain {plain.layout.width}x{plain.layout.height} styled {styled.layout.width}x{styled.layout.height} "
                + $"text '{styled.text}'";
            return (plain, styled, arranged, measured);
        }

        private static float Width(TextElement element, string text) =>
            element.MeasureTextSize(text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        private static float Height(TextElement element, string text, float width) =>
            element.MeasureTextSize(text, width,
                float.IsNaN(width) ? VisualElement.MeasureMode.Undefined : VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

        private string RawTextOf(Label label) => _mounted.Root.Reconciler.Context.TextRawText[label];

        private static string LastLine(string text) => text.Substring(text.LastIndexOf('\n') + 1);

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Balanced_Then_TheDisplayedTextBreaksBeforeIt()
        {
            // Arrange / Act
            var (plain, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");

            // Assert — the plain sibling shows no break of its own, so the newline is the balancing.
            Assert.That((arranged, plain.text.Contains('\n'), balanced.text.Contains('\n')),
                Is.EqualTo((true, false, true)), measured);
        }

        // The box width is the point of breaking lines over resizing the box: the same wrapper gives both
        // leaves the same box.
        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Balanced_Then_TheBoxKeepsTheWidthOfThePlainOne()
        {
            // Arrange / Act
            var (plain, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");

            // Assert
            Assert.That((arranged, balanced.layout.width), Is.EqualTo((true, plain.layout.width)), measured);
        }

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_BalancedUnderAFullWidthClass_Then_TheDisplayedTextStillBreaks()
        {
            // Arrange / Act — a declared width is no reason to leave the text unbroken.
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance w-full");

            // Assert
            Assert.That((arranged, balanced.text.Contains('\n')), Is.EqualTo((true, true)), measured);
        }

        // GREEN_ON_BASE(characterization): the base never rewrote the raw text either.
        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Balanced_Then_TheTextTheLeafWasGivenIsUnchanged()
        {
            // Arrange / Act
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");

            // Assert — the raw text the resolver rewrites from, which no consumer's value is read from.
            Assert.That((arranged, RawTextOf(balanced)), Is.EqualTo((true, Head + " e")), measured);
        }

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_TheClassIsOnTheContainer_Then_TheLeafBreaksItsLines()
        {
            // Arrange / Act — the leaf carries no class of its own.
            var (_, leaf, arranged, measured) = MountWithOrphan("e", "text-balance", "");

            // Assert
            Assert.That((arranged, leaf.text.Contains('\n')), Is.EqualTo((true, true)), measured);
        }

        // GREEN_ON_BASE(characterization): the base never writes a break into a displayed text.
        [Test]
        public void Given_BalanceOnTheContainerAndTextWrapOnTheLeaf_When_TheTextWraps_Then_TheLeafIsNotBroken()
        {
            // Arrange / Act — text-wrap resets the style it inherits.
            var (_, leaf, arranged, measured) = MountWithOrphan("e", "text-balance", "text-wrap");

            // Assert
            Assert.That((arranged, leaf.text.Contains('\n')), Is.EqualTo((true, false)), measured);
        }

        // GREEN_ON_BASE(characterization): the base narrowed the box to the same height.
        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Balanced_Then_NoLineIsAdded()
        {
            // Arrange / Act
            var (plain, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");

            // Assert
            Assert.That((arranged, balanced.layout.height), Is.EqualTo((true, plain.layout.height)), measured);
        }

        [Test]
        public void Given_ABalancedLabel_When_ItsWrapperWidensToOneLine_Then_ItsBreaksGoAway()
        {
            // Arrange
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");
            var broken = balanced.text.Contains('\n');

            // Act
            s_setWrapperWidth.Invoke(700f);
            Settle();

            // Assert
            Assert.That((arranged, broken, balanced.text.Contains('\n')), Is.EqualTo((true, true, false)), measured);
        }

        [Test]
        public void Given_ABalancedLabel_When_ItsTextBecomesOneWord_Then_ItsBreaksGoAway()
        {
            // Arrange
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");
            var broken = balanced.text.Contains('\n');

            // Act
            s_setText.Invoke("aaaa");
            Settle();

            // Assert
            Assert.That((arranged, broken, balanced.text), Is.EqualTo((true, true, "aaaa")), measured);
        }

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Pretty_Then_AWordJoinsItAtTheSameHeight()
        {
            // Arrange / Act
            var (plain, pretty, arranged, measured) = MountWithOrphan("e", "", "text-pretty");

            // Assert
            Assert.That(
                (arranged, LastLine(pretty.text).Contains(' '), pretty.layout.height == plain.layout.height),
                Is.EqualTo((true, true, true)), measured);
        }

        // GREEN_ON_BASE(characterization): the base left this text unbroken too.
        [Test]
        public void Given_ALastWordWiderThanAThirdOfTheLine_When_Pretty_Then_TheTextIsLeftAlone()
        {
            // Arrange / Act — Chromium leaves a last line of this width to the greedy breaks.
            var (_, pretty, arranged, measured) = MountWithOrphan("eeeeeeeeeeee", "", "text-pretty");

            // Assert
            Assert.That((arranged, pretty.text.Contains('\n')), Is.EqualTo((true, false)), measured);
        }

        // GREEN_ON_BASE(characterization): the base left this text unbroken too.
        [Test]
        public void Given_ALastLineHoldingTwoWords_When_Pretty_Then_TheTextIsLeftAlone()
        {
            // Arrange / Act — at the head's width "e f" share the second line.
            var (_, pretty, arranged, measured) = MountWithOrphan("e f", "", "text-pretty");

            // Assert
            Assert.That((arranged, pretty.text.Contains('\n')), Is.EqualTo((true, false)), measured);
        }

        [Test]
        public void Given_ABalancedLeafSizedByItsText_When_ItsTextGrows_Then_ItIsWiderThanItWasWithTheShortText()
        {
            // Arrange — a leaf aligned to the start of its parent is as wide as its text.
            s_containerClass = "";
            s_leafClass = "text-balance";
            s_layout = Layout.Hug;
            s_text = "aaaa bbbb";
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPair));
            Settle();
            var styled = _window.rootVisualElement.Q<Label>("styled");
            var shortWidth = styled.layout.width;

            // Act
            s_setText.Invoke(Head + " e");
            Settle();

            // Assert — by more than a letter, which the rounding of a measurement cannot account for.
            Assert.That(styled.layout.width, Is.GreaterThan(shortWidth + Width(styled, "e")),
                $"short {shortWidth} now {styled.layout.width} text '{styled.text}'");
        }

        [Test]
        public void Given_ABrokenLeafSizedByItsText_When_ItsParentWidensInSmallSteps_Then_ItsBreaksGoAway()
        {
            // Arrange
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance", Layout.Hug);
            var broken = balanced.text.Contains('\n');
            var parentWidth = _window.rootVisualElement.Q<Label>("plain").parent.layout.width;

            // Act — a step of one pixel is under the threshold, so only their sum can move the leaf.
            for (var step = 1; step <= 40; step++)
            {
                s_setWrapperWidth.Invoke(parentWidth + step);
                Settle();
            }

            // Assert
            Assert.That((arranged, broken, balanced.text.Contains('\n')), Is.EqualTo((true, true, false)), measured);
        }

        [Test]
        public void Given_ABrokenLeaf_When_ItsFontStyleChanges_Then_ItsMeasurementsAreTakenUnderTheNewStyle()
        {
            // Arrange
            var (_, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance");
            var manipulator = _mounted.Root.Reconciler.Context.TextBalanceManipulators[balanced];

            // Act
            balanced.style.unityFontStyleAndWeight = FontStyle.Bold;
            StyleTextEffectResolver.ReapplyElement(_mounted.Root.Reconciler.Context, balanced);
            Settle();

            // Assert
            var key = typeof(StyleTextBalanceManipulator)
                .GetField("_measureKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manipulator)!;
            var style = key.GetType().GetField("_fontStyle", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(key);
            Assert.That((arranged, style), Is.EqualTo((true, FontStyle.Bold)), measured);
        }

        [Test]
        public void Given_TextWithRunsOfSpaces_When_Balanced_Then_ItsLinesAreAsManyAsTheCollapsedTextTakesInNormal()
        {
            // Arrange / Act — the plain leaf collapses the runs as white-space: normal does.
            var (plain, balanced, arranged, measured) = MountWithOrphan(
                "e", "", "text-balance", head: "aaaa  bbbb   cccc dddd");

            // Assert
            Assert.That(
                (arranged, balanced.text.Contains('\n'), balanced.text.Contains("  "),
                    balanced.layout.height == plain.layout.height),
                Is.EqualTo((true, true, false, true)), measured);
        }

        // GREEN_ON_BASE(characterization): the base never rewrote the white space of a leaf with nothing to balance.
        [Test]
        public void Given_TextThatFitsOneLine_When_Balanced_Then_ItKeepsTheWhiteSpaceItWasWrittenWith()
        {
            // Arrange
            s_containerClass = "";
            s_leafClass = "text-balance";
            s_text = "aaaa  bbbb";
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPair));

            // Act
            Settle();
            var styled = _window.rootVisualElement.Q<Label>("styled");

            // Assert — neither collapsed nor written pre-wrap, since nothing was broken.
            Assert.That((styled.text, styled.style.whiteSpace.value), Is.EqualTo(("aaaa  bbbb", WhiteSpace.Normal)));
        }

        [Test]
        public void Given_ABalancedLeafUnderAMaxWidth_When_ItsTextWraps_Then_ItBreaksInsideTheCeilingAtTheSameHeight()
        {
            // Arrange / Act — the wrapper is wide, the ceiling is the room.
            var (plain, balanced, arranged, measured) = MountWithOrphan("e", "", "text-balance", ceiling: true);

            // Assert
            Assert.That(
                (arranged, balanced.text.Contains('\n'), balanced.layout.height == plain.layout.height),
                Is.EqualTo((true, true, true)), measured);
        }

        [Test]
        public void Given_ABalancedLeafBesideASiblingInARow_When_Balanced_Then_TheSiblingKeepsItsWidth()
        {
            // Arrange / Act
            var (_, balanced, arranged, measured) = MountWithOrphan(
                "e", "", "text-balance", Layout.Row, ceiling: true);
            var sibling = _window.rootVisualElement.Q<Label>("sibling");

            // Assert
            Assert.That((arranged, balanced.text.Contains('\n'), sibling.layout.width),
                Is.EqualTo((true, true, SiblingWidthPx)), measured);
        }

        [Test]
        public void Given_ABalancedLeafInAScrollView_When_ItsTextWraps_Then_ItBreaksAgainstTheInnerBox()
        {
            // Arrange / Act
            var (plain, balanced, arranged, measured) = MountWithOrphan(
                "e", "", "text-balance", Layout.Scroll);

            // Assert
            Assert.That(
                (arranged, balanced.text.Contains('\n'), balanced.layout.height == plain.layout.height),
                Is.EqualTo((true, true, true)), measured);
        }
    }
}
