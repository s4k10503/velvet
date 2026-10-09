using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a text item gets from Velvet before any class of its own: the preflight reset of the
    /// margin, padding, white-space and flex-shrink of a <see cref="Label"/> Velvet creates
    /// (<c>_preflight.uss</c>), and the automatic
    /// minimum size (<see cref="StyleFlexMinSizeManipulator"/>) along its parent's main axis.
    /// <para>
    /// A real <see cref="UnityEditor.EditorWindow"/> panel with the bundled <c>StyleUtilities.uss</c> is
    /// required: the reset is a stylesheet rule, and the manipulator reads <c>resolvedStyle</c>. Expected
    /// minimums are derived from <see cref="TextElement.MeasureTextSize"/> on the same element rather than
    /// declared, since a font's metrics differ between machines; the frame is read back from the element
    /// for the same reason.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class TextItemBaselinePanelTests : PanelTestBase
    {
        private const string LongWord = "internationalization";
        private const string Sentence = LongWord + " is long";
        private const string ManyShortWords = "aa bb cc dd ee ff gg hh ii jj kk ll mm nn oo pp";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // The geometry event is synthesized because the EditMode player loop delivers none, and the
        // manipulator only re-derives from one once resolvedStyle is valid.
        private T Mount<T>(string containerClass, VNode item) where T : VisualElement
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(containerClass, item));
            var element = _window.rootVisualElement.Q<T>();
            Settle(element);
            return element;
        }

        private Label MountLabel(string containerClass, string labelClass, string text)
            => Mount<Label>(containerClass, V.Label(className: labelClass, text: text));

        private static void Settle(VisualElement element)
        {
            ForcePanelUpdate(element.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            element.SimulateEvent(evt);
            ForcePanelUpdate(element.panel);
        }

        private static float HorizontalFrame(VisualElement e)
            => e.resolvedStyle.paddingLeft + e.resolvedStyle.paddingRight
                + e.resolvedStyle.borderLeftWidth + e.resolvedStyle.borderRightWidth;

        private static float VerticalFrame(VisualElement e)
            => e.resolvedStyle.paddingTop + e.resolvedStyle.paddingBottom
                + e.resolvedStyle.borderTopWidth + e.resolvedStyle.borderBottomWidth;

        private static float MeasuredWidth(TextElement e, string text)
            => e.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        private static float MeasuredHeight(TextElement e, string text, float width)
            => e.MeasureTextSize(
                text, width, VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

        private static float WidestWordMinimum(TextElement e, string word)
            => Mathf.Ceil(MeasuredWidth(e, word)) + HorizontalFrame(e);

        [Test]
        public void Given_AVelvetLabelWithNoUtilities_When_Resolved_Then_ItsMarginAndPaddingAreZero()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-col", "", "text");
            var style = label.resolvedStyle;

            // Assert
            Assert.That(
                (style.marginTop, style.marginRight, style.marginBottom, style.marginLeft,
                    style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft),
                Is.EqualTo((0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f)));
        }

        [Test]
        public void Given_AVelvetLabelWithPaddingAndMarginUtilities_When_Resolved_Then_TheUtilitiesWinOverTheReset()
        {
            // Arrange / Act — p-2 and m-1 are 8px and 4px on the spacing scale (see _tokens.uss).
            var label = MountLabel("flex flex-col", "p-2 m-1", "text");

            // Assert
            Assert.That(
                (label.resolvedStyle.paddingLeft, label.resolvedStyle.marginLeft),
                Is.EqualTo((8f, 4f)));
        }

        [Test]
        public void Given_AVelvetLabelUnderAPlainDiv_When_Resolved_Then_ItWrapsLikeCssNormalText()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row", "", "text");

            // Assert
            Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
        }

        [Test]
        public void Given_AVelvetLabelUnderAWhitespaceNowrapDiv_When_Resolved_Then_ItInheritsNoWrap()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row whitespace-nowrap", "", "text");

            // Assert
            Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.NoWrap));
        }

        [Test]
        public void Given_AVelvetLabelWhoseParentLosesWhitespaceNowrap_When_Reconciled_Then_ItWrapsAgain()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            VNode[] Tree(string parentClass) => new VNode[]
            {
                V.Div(parentClass, V.Label(text: "text")),
            };
            var before = Tree("flex flex-row whitespace-nowrap");
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), before);
            var label = scope.Root.Q<Label>();
            Settle(label);
            var wasNoWrap = label.resolvedStyle.whiteSpace == WhiteSpace.NoWrap;

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Tree("flex flex-row"));
            Settle(label);

            // Assert — the nowrap read is folded in: a label that never inherited it would wrap both times.
            Assert.That((wasNoWrap, label.resolvedStyle.whiteSpace), Is.EqualTo((true, WhiteSpace.Normal)));
        }

        [Test]
        public void Given_AVelvetLabelWithNoUtilities_When_Resolved_Then_ItShrinksLikeACssFlexItem()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row", "", "text");

            // Assert
            Assert.That(label.resolvedStyle.flexShrink, Is.EqualTo(1f));
        }

        [Test]
        public void Given_AVelvetLabelWithWhitespaceNowrap_When_Resolved_Then_TheUtilityWinsOverTheReset()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row", "whitespace-nowrap", "text");

            // Assert
            Assert.That(label.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.NoWrap));
        }

        [Test]
        public void Given_AVelvetLabelWithShrink0_When_Resolved_Then_TheUtilityWinsOverTheReset()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row", "shrink-0", "text");

            // Assert
            Assert.That(label.resolvedStyle.flexShrink, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AVText_When_Mounted_Then_ItCarriesTheVelvetLabelClass()
        {
            // Arrange / Act
            var label = Mount<Label>("flex flex-col", V.Text("text"));

            // Assert
            Assert.That(label.ClassListContains(VNodePool.VelvetLabelClassName), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base resets nothing, so a control's own label keeps its theme
        // spacing there too; what reddens it is the reset reaching a Label Velvet did not create.
        [Test]
        public void Given_AVTextFieldWithALabel_When_Resolved_Then_ItsLabelKeepsTheThemeSpacing()
        {
            // Arrange — an engine-built field on the same panel is the reference for the theme's spacing.
            var reference = new TextField("reference");
            _window.rootVisualElement.Add(reference);
            _mounted = V.Mount(_window.rootVisualElement, V.Div("flex flex-col", V.TextField(label: "field")));
            var fields = _window.rootVisualElement.Query<TextField>().ToList();
            foreach (var field in fields)
            {
                Settle(field);
            }
            var ours = fields[1].labelElement;

            // Act
            var spacing = new[] { ours, reference.labelElement }.Select(l => (
                l.resolvedStyle.marginTop, l.resolvedStyle.marginLeft,
                l.resolvedStyle.paddingTop, l.resolvedStyle.paddingLeft)).ToArray();

            // Assert — the class term is what the reset keys on; the spacing term is the theme's own reading.
            Assert.That(
                (ours.ClassListContains(VNodePool.VelvetLabelClassName), spacing[0] == spacing[1]),
                Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the base already holds StyleFlexMinSizeManipulator's minimum, so this
        // passes there; what reddens it is that write going away.
        [Test]
        public void Given_ALargeLabelInAShorterFixedHeightCenteredCard_When_Resolved_Then_TheTextIsCenteredOnTheCard()
        {
            // Arrange
            var label = MountLabel("flex flex-col items-center justify-center h-[20px] w-[200px]", "text-[40px]", "Title");
            var card = label.parent;
            var contentWidth = label.resolvedStyle.width - HorizontalFrame(label);
            var textHeight = MeasuredHeight(label, "Title", contentWidth);

            // Act
            var textTop = label.worldBound.yMin + label.resolvedStyle.borderTopWidth + label.resolvedStyle.paddingTop;
            var textCenter = textTop + textHeight / 2f;

            // Assert — the taller-than-card term is gated in as NaN, not folded into a tuple, so the tolerance
            // still applies; a font size that stopped resolving would otherwise leave a label that fits the card.
            // The 1f margin is a floating-point allowance between a measured height and a laid-out edge.
            var gated = textHeight > card.resolvedStyle.height ? textCenter : float.NaN;
            Assert.That(gated, Is.EqualTo(card.worldBound.center.y).Within(1f));
        }

        [Test]
        public void Given_ALabelInAColumnNarrowEnoughToWrap_When_Resolved_Then_ItsMinHeightIsItsWrappedHeight()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-col h-[10px] w-[60px]", "", ManyShortWords);
            var width = label.resolvedStyle.width - HorizontalFrame(label);
            var wrapped = MeasuredHeight(label, ManyShortWords, width);
            var oneLine = label.MeasureTextSize(
                "aa", float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

            // Assert — the wrap is folded in: a container wide enough for one line cannot tell a wrapped
            // height from a single line's.
            Assert.That(
                (label.style.minHeight.value.value, wrapped > oneLine),
                Is.EqualTo((Mathf.Ceil(wrapped) + VerticalFrame(label), true)));
        }

        [Test]
        public void Given_ALabelInARow_When_Resolved_Then_ItsMinWidthIsItsWidestWord()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "", Sentence);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongWord)));
        }

        [Test]
        public void Given_ANoWrapLabelInARow_When_Resolved_Then_ItsMinWidthIsTheWholeLine()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "whitespace-nowrap", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Ceil(MeasuredWidth(label, Sentence)) + HorizontalFrame(label)));
        }

        [Test]
        public void Given_APreLabelInARow_When_Resolved_Then_ItsMinWidthIsTheWholeLine()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "whitespace-pre", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Ceil(MeasuredWidth(label, Sentence)) + HorizontalFrame(label)));
        }

        [Test]
        public void Given_AButtonInARow_When_Resolved_Then_ItsMinWidthIsItsWidestWordPlusItsOwnFrame()
        {
            // Arrange / Act
            var button = Mount<Button>("flex flex-row w-[20px]", V.Button(text: Sentence));

            // Assert
            Assert.That(button.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(button, LongWord)));
        }

        [Test]
        public void Given_ALabelWithCjkText_When_ResolvedInARow_Then_ItsMinWidthIsOneCharacter()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "", "日本語日本語日本語");

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, "日")));
        }

        [Test]
        public void Given_ALabelInARow_When_Resolved_Then_ItsMinHeightIsLeftAlone()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "", Sentence);

            // Assert — the minWidth is folded in: a label that stood down on both axes leaves this one null
            // as well, and the claim is that only the main axis is written.
            Assert.That(
                (label.style.minWidth.keyword == StyleKeyword.Null, label.style.minHeight.keyword),
                Is.EqualTo((false, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ALabelWhoseParentFlipsToAColumn_When_Resolved_Then_TheRowMinimumIsReleased()
        {
            // Arrange
            var label = MountLabel("flex flex-row w-[60px]", "", ManyShortWords);
            var parent = label.parent;

            // Act
            parent.style.flexDirection = FlexDirection.Column;
            Settle(label);

            // Assert — the new axis is folded in: a release that also wrote nothing would pass on its own.
            Assert.That(
                (label.style.minWidth.keyword, label.style.minHeight.keyword == StyleKeyword.Null),
                Is.EqualTo((StyleKeyword.Null, false)));
        }

        [Test]
        public void Given_ALabelWithADeclaredWidthBelowItsWidestWord_When_Resolved_Then_ItsMinWidthIsTheDeclaredWidth()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "w-[40px]", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Min(WidestWordMinimum(label, LongWord), 40f)));
        }

        [Test]
        public void Given_ALabelWithAScaleWidthBelowItsWidestWord_When_Resolved_Then_ItsMinWidthIsTheScaleWidth()
        {
            // Arrange / Act — w-4 is 16px on the spacing scale (see _tokens.uss).
            var label = MountLabel("flex flex-row w-[20px]", "w-4", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Min(WidestWordMinimum(label, LongWord), 16f)));
        }

        [Test]
        public void Given_ALabelWithADeclaredWidthAboveItsWidestWord_When_Resolved_Then_ItsMinWidthIsTheWord()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "w-[5000px]", Sentence);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongWord)));
        }

        [Test]
        public void Given_ALabelWithAFitContentWidth_When_Resolved_Then_ItsMinWidthIsTheWord()
        {
            // Arrange / Act — w-fit is a content keyword, not a definite size, so it caps nothing.
            var label = MountLabel("flex flex-row w-[20px]", "w-fit", Sentence);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongWord)));
        }

        [Test]
        public void Given_ALabelWithAMaximumWidth_When_Resolved_Then_ItsMinWidthIsClampedByIt()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "max-w-[30px]", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Min(WidestWordMinimum(label, LongWord), 30f)));
        }

        [Test]
        public void Given_ALabelWithAScaleMaximumHeight_When_ResolvedInAColumn_Then_ItsMinHeightIsClampedByIt()
        {
            // Arrange / Act — max-h-4 is 16px on the spacing scale (see _tokens.uss).
            var label = MountLabel("flex flex-col h-[10px] w-[60px]", "max-h-4", ManyShortWords);
            var wrapped = MeasuredHeight(label, ManyShortWords, label.resolvedStyle.width - HorizontalFrame(label));

            // Assert — the wrapped height is folded in: a max that clamped a single line would pass alone.
            Assert.That(
                (label.style.minHeight.value.value, wrapped > 16f),
                Is.EqualTo((16f, true)));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // ignoring the class — a clipped item has an automatic minimum of zero, so the manipulator must leave the slot alone.
        [Test]
        public void Given_ATruncatedLabelInARow_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "truncate", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // ignoring the class — a min-w-* class is the author declaring the axis.
        [Test]
        public void Given_ALabelWithItsOwnMinWidth_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "min-w-0", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // ignoring the class — a min-h-* class is the author declaring the axis.
        [Test]
        public void Given_ALabelWithItsOwnMinHeight_When_ResolvedInAColumn_Then_NoMinHeightIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-col h-[10px] w-[60px]", "min-h-0", ManyShortWords);

            // Assert
            Assert.That(label.style.minHeight.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // writing for an item that is not in the flow.
        [Test]
        public void Given_AnAbsolutelyPositionedLabel_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "absolute", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // writing for an item that is not laid out.
        [Test]
        public void Given_AHiddenLabel_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "hidden", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // measuring text for an item whose content is its children.
        [Test]
        public void Given_AButtonWithChildren_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var button = Mount<Button>(
                "flex flex-row w-[20px]",
                V.Button(text: Sentence, children: new VNode?[] { V.Div("w-4 h-4") }));

            // Assert
            Assert.That(button.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // ignoring that a grid track is minmax(0, 1fr).
        [Test]
        public void Given_ALabelInAGridColumn_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("grid grid-cols-2 w-[20px]", "", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALabelThatCannotShrink_When_Resolved_Then_ItsMinWidthIsItsWidestWord()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "shrink-0", Sentence);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongWord)));
        }

        [Test]
        public void Given_ALabelWhoseMinWidthClassIsPatchedAway_When_Reconciled_Then_TheAutomaticMinimumReturns()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            VNode[] Tree(string labelClass) => new VNode[]
            {
                V.Div("flex flex-row w-[20px]", V.Label(className: labelClass, text: Sentence)),
            };
            var before = Tree("min-w-0");
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), before);
            var label = scope.Root.Q<Label>();
            Settle(label);

            // Act — the patch removes the declaring class; the resolved style still holds its zero here.
            scope.Reconciler.Reconcile(scope.Root, before, Tree(""));

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongWord)));
        }

        [Test]
        public void Given_ALabelWithAnAutomaticMinimum_When_ItsMinWidthClassIsPatchedIn_Then_TheWrittenValueIsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            VNode[] Tree(string labelClass) => new VNode[]
            {
                V.Div("flex flex-row w-[20px]", V.Label(className: labelClass, text: Sentence)),
            };
            var before = Tree("");
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), before);
            var label = scope.Root.Q<Label>();
            Settle(label);
            var wrote = label.style.minWidth.keyword != StyleKeyword.Null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Tree("min-w-0"));

            // Assert — the earlier write is folded in: a manipulator that never wrote leaves the slot null
            // as well, and the claim is that the class takes a written value back out.
            Assert.That((wrote, label.style.minWidth.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ATransitionAllLabel_When_ItsMinimumChanges_Then_TheNewMinimumLandsAtOnce()
        {
            // Arrange — a half-second transition on every property, which an animated rewrite of the minimum
            // would still be near its start value under, the panel clock not having advanced.
            const string Longer = "antidisestablishmentarianism is long";
            using var scope = new ReconcilerScope();
            _window.rootVisualElement.Add(scope.Root);
            VNode[] Tree(string text) => new VNode[]
            {
                V.Div("flex flex-row w-[20px]", V.Label(className: "transition-all duration-500", text: text)),
            };
            var before = Tree(Sentence);
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), before);
            var label = scope.Root.Q<Label>();
            Settle(label);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Tree(Longer));
            ForcePanelUpdate(label.panel);

            // Assert
            Assert.That(
                label.resolvedStyle.minWidth.value,
                Is.EqualTo(WidestWordMinimum(label, "antidisestablishmentarianism")));
        }
    }
}
