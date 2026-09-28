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

        // Mounts the pair and narrows both wrappers to just past the width the plain label needs for Head on
        // one line, so the full text puts its last word on a second line of its own.
        private (Label plain, Label pretty) MountAtHeadWidth(string lastWord)
        {
            s_text = Head + " " + lastWord;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderPair));
            Settle();
            var plain = _window.rootVisualElement.Q<Label>("plain");
            var headWidth = plain.MeasureTextSize(Head, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;
            var style = plain.resolvedStyle;
            var frame = style.marginLeft + style.marginRight + style.paddingLeft + style.paddingRight
                + style.borderLeftWidth + style.borderRightWidth;
            s_setWrapperWidth.Invoke(Mathf.Ceil(headWidth + frame) + 1f);
            Settle();
            return (plain, _window.rootVisualElement.Q<Label>("pretty"));
        }

        [Test]
        public void Given_AShortLastWordAloneOnItsLine_When_Pretty_Then_TheBoxNarrowsAtTheSameHeight()
        {
            // Arrange / Act
            var (plain, pretty) = MountAtHeadWidth("e");

            // Assert
            Assert.That(
                (pretty.layout.height == plain.layout.height, pretty.layout.width < plain.layout.width - 1f),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base never narrows a text-pretty box.
        [Test]
        public void Given_ALastWordWiderThanAThirdOfTheLine_When_Pretty_Then_TheBoxIsLeftAlone()
        {
            // Arrange / Act — Chromium leaves a last line of this width to the greedy breaks.
            var (_, pretty) = MountAtHeadWidth("eeeeeeeeeeee");

            // Assert
            Assert.That(pretty.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base never narrows a text-pretty box.
        [Test]
        public void Given_ALastLineHoldingTwoWords_When_Pretty_Then_TheBoxIsLeftAlone()
        {
            // Arrange / Act — at the head's width "e f" share the second line.
            var (_, pretty) = MountAtHeadWidth("e f");

            // Assert
            Assert.That(pretty.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ANarrowedPrettyBox_When_ItsTextStopsEndingInAShortWord_Then_TheBoxIsHandedBack()
        {
            // Arrange
            var (_, pretty) = MountAtHeadWidth("e");
            var narrowed = pretty.style.width.keyword;

            // Act
            s_setText.Invoke(Head + " eeeeeeeeeeee");
            Settle();

            // Assert
            Assert.That((narrowed, pretty.style.width.keyword), Is.EqualTo((StyleKeyword.Undefined, StyleKeyword.Null)));
        }
    }
}
