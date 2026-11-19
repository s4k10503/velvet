using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a text leaf derives its automatic minimum again on an event of its parent, and what the minimum's
    /// natural-width bookkeeping holds once a derive has run. Only the parent's own geometry event is fired, with no layout
    /// pass after the leaf's text or classes changed, so a leaf shows a new value only where it derived again.
    /// </summary>
    [TestFixture]
    internal sealed class TextItemParentEventTests : PanelTestBase
    {
        private const string LongWord = "internationalization";
        private const string Sentence = LongWord + " is long";
        private const string LongerWord = "antidisestablishmentarianism";
        private const string Longer = LongerWord + " is long";
        private const string NarrowRow = "flex flex-row w-[20px]";
        private const string WideRow = "flex flex-row w-[2000px]";

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // Mounted through a reconciler of the test's own so the leaf's manipulator can be reached from its context. The
        // geometry event is synthesized because the EditMode player loop delivers none.
        private Label MountLabel(ReconcilerScope scope, string containerClass, string labelClass, string text)
        {
            _window.rootVisualElement.Add(scope.Root);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(containerClass, V.Label(className: labelClass, text: text)) });
            var label = scope.Root.Q<Label>();
            ForcePanelUpdate(label.panel);
            FireGeometry(label);
            ForcePanelUpdate(label.panel);
            return label;
        }

        // Only the event, with no layout pass: what the element's callbacks see is what the last pass left.
        private static void FireGeometry(VisualElement element)
        {
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            element.SimulateEvent(evt);
        }

        private static float MeasuredWidth(TextElement e, string text)
            => e.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        private static float WidestWordMinimum(TextElement e, string word)
            => Mathf.Ceil(MeasuredWidth(e, word))
                + e.resolvedStyle.paddingLeft + e.resolvedStyle.paddingRight
                + e.resolvedStyle.borderLeftWidth + e.resolvedStyle.borderRightWidth;

        private static StyleFlexMinSizeManipulator ManipulatorOf(ReconcilerScope scope, Label label)
            => ReconcilerContextProbe.Of(scope).FlexMinSizeManipulators[label];

        private static T Read<T>(StyleFlexMinSizeManipulator manipulator, string name)
        {
            for (var type = typeof(StyleFlexMinSizeManipulator); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Private | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return (T)field.GetValue(manipulator)!;
                }
            }
            throw new MissingFieldException(nameof(StyleFlexMinSizeManipulator), name);
        }

        [Test]
        public void Given_ALabelSettledInARow_When_OnlyItsParentsGeometryEventFiresTwiceAfterItsTextChanged_Then_ItsMinWidthStaysWhereItSettled()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            var settled = label.style.minWidth.value.value;
            label.text = Longer;

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(settled));
        }

        [Test]
        public void Given_ALabelThatDerivedForAnEmptyText_When_ItsTextReturnsAndOnlyItsParentsGeometryEventFires_Then_ItGetsAMinWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            label.text = string.Empty;
            FireGeometry(label);
            label.text = Sentence;

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AClippedLabelSettledInARow_When_ItsClipClassLeavesOutsideAPatchAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthIsLeftAlone()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "overflow-hidden", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            label.RemoveFromClassList("overflow-hidden");

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALabelWithAnInlinePercentMaxWidth_When_ItsTextChangesAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthFollowsTheText()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "shrink-0", Sentence);
            var parent = label.parent;
            label.style.maxWidth = new StyleLength(new Length(100f, LengthUnit.Percent));
            FireGeometry(label);
            FireGeometry(parent);
            label.text = Longer;

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongerWord)));
        }

        [Test]
        public void Given_AClippedLabelInARowWithRoomForItsText_When_Resolved_Then_NoNaturalWidthHasBeenMeasuredForIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "overflow-hidden", Sentence);

            // Act
            var measured = Read<bool>(ManipulatorOf(scope, label), "_hasNatural");

            // Assert
            Assert.That(measured, Is.False);
        }

        [Test]
        public void Given_ALabelInARowNarrowerThanItsText_When_Resolved_Then_ItsNaturalWidthIsKeptForItsTextAndFontSize()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var expected = HashCode.Combine(Sentence, label.resolvedStyle.fontSize);

            // Act
            var kept = Read<int>(ManipulatorOf(scope, label), "_naturalKey");

            // Assert
            Assert.That(kept, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ALabelInARowNarrowerThanItsText_When_Resolved_Then_ItsNaturalWidthIsKept()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);

            // Act
            var kept = Read<bool>(ManipulatorOf(scope, label), "_hasNatural");

            // Assert
            Assert.That(kept, Is.True);
        }

        [Test]
        public void Given_ATextThatMeasuresNoWidth_When_ItsNaturalWidthIsAskedFor_Then_TheLabelIsNotAtIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var manipulator = ManipulatorOf(scope, label);
            var method = typeof(StyleFlexMinSizeManipulator).GetMethod("IsAtNaturalWidth", Private)!;

            // Act
            var atNatural = (bool)method.Invoke(manipulator, new object[] { label, true, string.Empty, label.resolvedStyle, 0f, true })!;

            // Assert
            Assert.That(atNatural, Is.False);
        }

        [Test]
        public void Given_ALabelWhoseWidthIsExactlyItsTextsWidthPlusItsFrameLessTheTolerance_When_Resolved_Then_ItsMinWidthIsNotWritten()
        {
            // Arrange — the frame is the 0.01 pixel the tolerance subtracts, so the threshold is the text's width.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "pl-[0.01px]", Sentence);
            label.style.width = Mathf.Ceil(MeasuredWidth(label, Sentence));

            // Act
            ForcePanelUpdate(label.panel);
            FireGeometry(label);
            ForcePanelUpdate(label.panel);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AFlexMinSizeManipulatorOnTheOnlyLeafOfAParent_When_ItIsRemoved_Then_TheParentKeepsNoCallbackOfTheHub()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var parent = new VisualElement();
            var label = new Label("text");
            parent.Add(label);
            var manipulator = new StyleFlexMinSizeManipulator(ReconcilerContextProbe.Of(scope), Array.Empty<string>());
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(parent);
            label.AddManipulator(manipulator);
            var joined = CallbackRegistryProbe.BubbleUpCallbackCount(parent) != before;

            // Act
            label.RemoveManipulator(manipulator);

            // Assert — the join is carried by the value: where the leaf never joined, the count is one `before` cannot equal.
            Assert.That(joined ? CallbackRegistryProbe.BubbleUpCallbackCount(parent) : before + 1, Is.EqualTo(before));
        }
    }
}
