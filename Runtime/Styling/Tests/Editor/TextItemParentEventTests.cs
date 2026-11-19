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

        // Sets the text without the ChangeEvent<string> that the text setter sends from an attached label, which the leaf
        // derives on by itself (Given_ALabelSettledInARow_When_ItsTextIsSet_Then_ItsMinWidthFollowsWithNoGeometryEventFired
        // pins it); a case about the parent's event has to change the text without it.
        private static void SetTextQuietly(Label label, string text)
            => ((INotifyValueChanged<string>)label).SetValueWithoutNotify(text);

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

        // GREEN_ON_BASE(characterization): the base leaves a settled label alone on a parent event that moves no direction. Drop `hub.Direction =
        // direction;` from s_onParentGeometry, or `Settle(signature, text);` after the Write in DeriveCore, and the
        // label derives for its new text.
        [Test]
        public void Given_ALabelSettledInARow_When_ItsTextChangesAndOnlyItsParentsGeometryEventFiresAgain_Then_ItsMinWidthStaysWhereItSettled()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            var settled = label.style.minWidth.value.value;
            SetTextQuietly(label, Longer);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(settled));
        }

        // GREEN_ON_BASE(characterization): the base derives a settled label again when its text is set, which is the property SetTextQuietly exists to avoid.
        [Test]
        public void Given_ALabelSettledInARow_When_ItsTextIsSet_Then_ItsMinWidthFollowsWithNoGeometryEventFired()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var settled = label.style.minWidth.value.value;

            // Act
            label.text = Longer;

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.Not.EqualTo(settled));
        }

        // GREEN_ON_BASE(characterization): the base derives again on a parent event for a label holding no signature. Drop `|| !_hasSignature` from
        // OnParentGeometryChanged and the returned text gets none.
        [Test]
        public void Given_ALabelEmptiedAfterMountingWithText_When_ItsTextReturnsAndOnlyItsParentsGeometryEventFires_Then_ItGetsAMinWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            label.text = string.Empty;
            FireGeometry(label);
            SetTextQuietly(label, Sentence);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base settles a label that stands down, so a parent event leaves it. Drop `Settle(signature, text);` from
        // the StandsDown branch of DeriveCore and the event derives it without its clip.
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

        // GREEN_ON_BASE(characterization): the base reads an inline percent as one that reads its parent's box. Change `inline.value.unit ==
        // LengthUnit.Percent` to `!=` in IsPercent and the parent event leaves the label as it was.
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
            SetTextQuietly(label, Longer);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongerWord)));
        }

        // GREEN_ON_BASE(characterization): the base measures no natural width for a clipped label. Change `measure: false` to `true` in the first
        // IsAtNaturalWidth call of DeriveCore and one is measured.
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

        // GREEN_ON_BASE(characterization): the base keeps the key of the text and font size it measured. Drop `_naturalKey = key;` from
        // IsAtNaturalWidth and the key stays at its default of zero.
        [Test]
        public void Given_ALabelInARowNarrowerThanItsText_When_Resolved_Then_ANaturalWidthKeyIsKept()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);

            // Act
            var kept = Read<int>(ManipulatorOf(scope, label), "_naturalKey");

            // Assert
            Assert.That(kept, Is.Not.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base keeps the natural width it measured. Change `_hasNatural = true;` to `false` in IsAtNaturalWidth
        // and nothing is kept.
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

        // GREEN_ON_BASE(characterization): the base says a text of no width is not at its natural width. Change the `return false;` after `!(width >
        // 0f)` in IsAtNaturalWidth to `true` and the label is at it.
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

        // GREEN_ON_BASE(characterization): the base writes no minimum for a label at its text's width. Return `false` from the end of
        // IsAtNaturalWidth and a minimum is written.
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

        // GREEN_ON_BASE(characterization): the base takes a width equal to the threshold as the natural width. Change `>=` to `>` in the
        // return of IsAtNaturalWidth and the label is not at it. The inputs are declared: the natural width is the 100 the label is
        // given, and 100 + 0.01 - 0.01 is 100 in single precision.
        [Test]
        public void Given_ALabelWhoseWidthIsExactlyTheNaturalWidthThreshold_When_ItIsAskedWhetherItIsAtNaturalWidth_Then_ItIs()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "", Sentence);
            label.style.width = 100f;
            ForcePanelUpdate(label.panel);
            var manipulator = ManipulatorOf(scope, label);
            var type = typeof(StyleFlexMinSizeManipulator);
            type.GetField("_natural", Private)!.SetValue(manipulator, 100f);
            type.GetField("_naturalKey", Private)!.SetValue(manipulator, HashCode.Combine(Sentence, label.resolvedStyle.fontSize));
            type.GetField("_hasNatural", Private)!.SetValue(manipulator, true);

            // Act
            var atNatural = (bool)type.GetMethod("IsAtNaturalWidth", Private)!
                .Invoke(manipulator, new object[] { label, true, Sentence, label.resolvedStyle, 0.01f, false })!;

            // Assert — false where the layout did not give the label 100, which the expected value is not.
            Assert.That(label.resolvedStyle.width == 100f && atNatural, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base takes its hub callback off a parent whose last leaf leaves. Drop
        // `_subscribedParent.UnregisterCallback(s_onParentGeometry);` from SyncParentSubscription and the callback
        // stays.
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
