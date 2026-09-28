using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <c>text-pretty</c>'s short-last-line avoidance, on a real panel so the text is measured: a label
    /// whose last word would sit alone on its line is narrowed until a word joins it, at the same height as
    /// a plain sibling, and one whose last word is as wide as a third of the line is left alone. The
    /// wrapper is sized after mount, from the plain label's own measure of the text before the last word,
    /// so the orphan exists in whatever font the host renders with. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextPrettyPanelTests : PanelTestBase
    {
        private const string Head = "aaaa bbbb cccc dddd";

        private static StateUpdater<float> s_setWrapperWidth;
        private static StateUpdater<string> s_setText;

        protected override Rect WindowSize => new Rect(0, 0, 800, 400);

        public override void SetUp()
        {
            s_setWrapperWidth = default;
            s_setText = default;
            base.SetUp();
        }

        private static string s_text;

        [Component]
        private static VNode RenderPair()
        {
            var (width, setWidth) = Hooks.UseState(700f);
            s_setWrapperWidth = setWidth;
            var (text, setText) = Hooks.UseState(s_text);
            s_setText = setText;
            return V.Div(children: new VNode[]
            {
                V.Div(className: $"w-[{width}px]", V.Label(name: "plain", className: "text-wrap", text: text)),
                V.Div(className: $"w-[{width}px]", V.Label(name: "pretty", className: "text-pretty", text: text)),
            });
        }

        private void Settle()
        {
            for (var i = 0; i < 3; i++)
            {
                _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
                ForcePanelUpdate(_window.rootVisualElement.panel);
            }
        }

        // Mounts the pair and narrows both wrappers to the first whole-pixel content width at which the plain
        // label's own measure puts Head on one line and the full text on more, so its last word sits on a
        // line of its own in whatever font the host renders with. Arranged is false when no width in range
        // does, which the cases report rather than read past.
        private (Label plain, Label pretty, bool arranged, string measured) MountWithOrphan(string lastWord)
        {
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
            var pretty = _window.rootVisualElement.Q<Label>("pretty");
            var measured = $"line {oneLine} head {headWidth} full {fullWidth} content {content} frame {frame} "
                + $"plain {plain.layout.width}x{plain.layout.height} pretty {pretty.layout.width}x{pretty.layout.height}";
            return (plain, pretty, arranged, measured);
        }

        private static float Width(TextElement element, string text) =>
            element.MeasureTextSize(text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        private static float Height(TextElement element, string text, float width) =>
            element.MeasureTextSize(text, width,
                float.IsNaN(width) ? VisualElement.MeasureMode.Undefined : VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Pretty_Then_AWordJoinsItAtTheSameHeight()
        {
            // Arrange / Act
            var (plain, pretty, arranged, measured) = MountWithOrphan("e");

            // Assert — no line added, and at the box it now has the text before the last word no longer fits
            // one line, so a word of it has moved down beside the last one.
            Assert.That(
                (arranged, pretty.layout.height == plain.layout.height,
                    Height(pretty, Head, pretty.contentRect.width) > Height(pretty, Head, float.NaN) + 0.5f),
                Is.EqualTo((true, true, true)), measured);
        }

        // GREEN_ON_BASE(characterization): the base never narrows a text-pretty box.
        [Test]
        public void Given_ALastWordWiderThanAThirdOfTheLine_When_Pretty_Then_TheBoxIsLeftAlone()
        {
            // Arrange / Act — Chromium leaves a last line of this width to the greedy breaks.
            var (_, pretty, arranged, measured) = MountWithOrphan("eeeeeeeeeeee");

            // Assert
            Assert.That((arranged, pretty.style.width.keyword), Is.EqualTo((true, StyleKeyword.Null)), measured);
        }

        // GREEN_ON_BASE(characterization): the base never narrows a text-pretty box.
        [Test]
        public void Given_ALastLineHoldingTwoWords_When_Pretty_Then_TheBoxIsLeftAlone()
        {
            // Arrange / Act — at the head's width "e f" share the second line.
            var (_, pretty, arranged, measured) = MountWithOrphan("e f");

            // Assert
            Assert.That((arranged, pretty.style.width.keyword), Is.EqualTo((true, StyleKeyword.Null)), measured);
        }

        [Test]
        public void Given_ANarrowedPrettyBox_When_ItsTextStopsEndingInAShortWord_Then_TheBoxIsHandedBack()
        {
            // Arrange
            var (_, pretty, arranged, measured) = MountWithOrphan("e");
            var narrowed = (arranged, pretty.style.width.keyword);

            // Act
            s_setText.Invoke(Head + " eeeeeeeeeeee");
            Settle();

            // Assert
            Assert.That($"{narrowed} {pretty.style.width.keyword}",
                Is.EqualTo($"{(true, StyleKeyword.Undefined)} {StyleKeyword.Null}"), measured);
        }
    }
}
