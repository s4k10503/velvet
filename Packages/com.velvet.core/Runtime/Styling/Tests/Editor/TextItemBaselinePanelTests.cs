using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a text item gets from Velvet before any class of its own: the preflight reset of a
    /// <see cref="Label"/>'s margin and padding (<c>_preflight.uss</c>), and the automatic minimum size
    /// (<see cref="StyleFlexMinSizeManipulator"/>) along its parent's main axis.
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

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // The geometry event is synthesized because the EditMode player loop delivers none, and the
        // manipulator only re-derives from one once resolvedStyle is valid.
        private Label MountLabel(string containerClass, string labelClass, string text)
        {
            _mounted = V.Mount(
                _window.rootVisualElement,
                V.Div(containerClass, V.Label(className: labelClass, text: text)));
            var label = _window.rootVisualElement.Q<Label>();
            ForcePanelUpdate(label.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            label.SimulateEvent(evt);
            ForcePanelUpdate(label.panel);
            return label;
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

        [Test]
        public void Given_ALabelWithNoUtilities_When_Resolved_Then_ItsMarginAndPaddingAreZero()
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
        public void Given_ALabelWithPaddingAndMarginUtilities_When_Resolved_Then_TheUtilitiesWinOverTheReset()
        {
            // Arrange / Act — p-2 and m-1 are 8px and 4px on the spacing scale (see _tokens.uss).
            var label = MountLabel("flex flex-col", "p-2 m-1", "text");

            // Assert
            Assert.That(
                (label.resolvedStyle.paddingLeft, label.resolvedStyle.marginLeft),
                Is.EqualTo((8f, 4f)));
        }

        [Test]
        public void Given_ALabelInAColumn_When_Resolved_Then_ItsMinHeightIsItsWrappedHeight()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-col h-[10px]", "", Sentence);
            var width = label.resolvedStyle.width - HorizontalFrame(label);
            var wrapped = label.MeasureTextSize(
                Sentence, width, VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

            // Assert
            Assert.That(label.style.minHeight.value.value, Is.EqualTo(Mathf.Ceil(wrapped) + VerticalFrame(label)));
        }

        [Test]
        public void Given_ALabelInARow_When_Resolved_Then_ItsMinWidthIsItsWidestWord()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "", Sentence);

            // Assert
            Assert.That(
                label.style.minWidth.value.value,
                Is.EqualTo(Mathf.Ceil(MeasuredWidth(label, LongWord)) + HorizontalFrame(label)));
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
        // ignoring the class — a declared main size takes the axis over.
        [Test]
        public void Given_ALabelWithItsOwnWidth_When_Resolved_Then_NoMinWidthIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-row w-[20px]", "w-[40px]", Sentence);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base writes no minimum at all; what reddens it is the manipulator
        // ignoring the class — a declared main size takes the axis over.
        [Test]
        public void Given_ALabelWithItsOwnHeight_When_ResolvedInAColumn_Then_NoMinHeightIsWritten()
        {
            // Arrange / Act
            var label = MountLabel("flex flex-col h-[10px]", "h-[5px]", Sentence);

            // Assert
            Assert.That(label.style.minHeight.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
