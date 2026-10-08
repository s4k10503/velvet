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

        private static StateUpdater<float> s_setWrapperWidth;
        private static StateUpdater<string> s_setText;
        private static string s_text;
        private static string s_containerClass;
        private static string s_leafClass;

        protected override Rect WindowSize => new Rect(0, 0, 800, 400);

        public override void SetUp()
        {
            s_setWrapperWidth = default;
            s_setText = default;
            base.SetUp();
        }

        [Component(Compiler = false)]
        private static VNode RenderPair()
        {
            var (width, setWidth) = Hooks.UseState(700f);
            s_setWrapperWidth = setWidth;
            var (text, setText) = Hooks.UseState(s_text);
            s_setText = setText;
            return V.Div(children: new VNode[]
            {
                V.Div(className: $"w-[{width}px]", V.Label(name: "plain", className: "text-wrap", text: text)),
                V.Div(className: $"w-[{width}px] {s_containerClass}",
                    V.Label(name: "styled", className: s_leafClass, text: text)),
            });
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
            string lastWord, string containerClass, string leafClass)
        {
            s_containerClass = containerClass;
            s_leafClass = leafClass;
            s_text = Head + " " + lastWord;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPair));
            Settle();
            var plain = _window.rootVisualElement.Q<Label>("plain");
            var oneLine = Height(plain, Head, float.NaN);
            var headWidth = Mathf.Ceil(Width(plain, Head));
            var fullWidth = Mathf.Ceil(Width(plain, s_text));
            var content = float.NaN;
            for (var width = headWidth; width <= fullWidth; width++)
            {
                if (Height(plain, Head, width) <= oneLine + 0.5f && Height(plain, s_text, width) > oneLine + 0.5f)
                {
                    content = width;
                    break;
                }
            }
            var style = plain.resolvedStyle;
            var frame = style.marginLeft + style.marginRight + style.paddingLeft + style.paddingRight
                + style.borderLeftWidth + style.borderRightWidth;
            var arranged = !float.IsNaN(content);
            if (arranged)
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
    }
}
