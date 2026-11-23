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
        private const string RowParent = WideRow;
        private const string ColumnParent = "flex flex-col w-[300px] h-[2000px]";

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

        // GREEN_ON_BASE(characterization): the base derives a settled label again for a text the patch changed, with its classes as they were. Change `==` to `!=` in the early return of Refresh and the label is left as it settled.
        [Test]
        public void Given_ALabelSettledInARow_When_ItsTextChangesAndItIsRefreshedWithTheClassesItHad_Then_ItsMinWidthFollowsTheText()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var manipulator = ManipulatorOf(scope, label);
            SetTextQuietly(label, Longer);

            // Act
            manipulator.Refresh(Read<string[]>(manipulator, "_lastClassNames"));

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongerWord)));
        }

        // GREEN_ON_BASE(characterization): the base derives a label a grid stopped sizing on its parent's next event, though the direction stayed. Drop `|| sizedByGrid` from the `_readsParentBox` assignment in DeriveCore and the event leaves it.
        [Test]
        public void Given_ALabelStoodDownForAGridThatStopsSizingIt_When_ItsTextChangesAndOnlyItsParentsGeometryEventFires_Then_ItGetsAMinWidth()
        {
            // Arrange — the first event records the parent's direction and derives nothing new. The registry entry then stands
            // in for a grid, which the stand-down path reads only by key, and is gone again before any further event and
            // before the scope ends.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            var grids = ReconcilerContextProbe.Of(scope).GridManipulators;
            grids[parent] = null!;
            ManipulatorOf(scope, label).Rederive();
            grids.Remove(parent);
            SetTextQuietly(label, Longer);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALabelStoodDownForAGridAndSettledAgainByItsOwnEvent_When_TheGridStopsSizingItAndOnlyItsParentsGeometryEventFires_Then_ItGetsAMinWidth()
        {
            // Arrange — as the case above, with the label's own geometry event between the stand-down and the
            // grid leaving, which reaches the early return on the settled signature. The text is left as it was.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            var grids = ReconcilerContextProbe.Of(scope).GridManipulators;
            grids[parent] = null!;
            ManipulatorOf(scope, label).Rederive();
            FireGeometry(label);
            grids.Remove(parent);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALabelWithAMinWidthWritten_When_ItsInlineOverflowIsSetHiddenAndItsOwnGeometryEventFires_Then_ItsMinWidthIsReleased()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            var written = label.style.minWidth.keyword == StyleKeyword.Undefined;
            label.style.overflow = Overflow.Hidden;

            // Act
            FireGeometry(label);

            // Assert — a keyword the expected one is not where no minimum was written to release.
            Assert.That(written ? label.style.minWidth.keyword : StyleKeyword.Undefined, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ALabelStoodDownForInlineOverflowHidden_When_ThatInlineValueIsClearedAndItsOwnGeometryEventFires_Then_ItGetsAMinWidth()
        {
            // Arrange — Rederive drops the settled signature, so the derive it runs stands the label down on the inline value.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, NarrowRow, "", Sentence);
            label.style.overflow = Overflow.Hidden;
            ManipulatorOf(scope, label).Rederive();
            label.style.overflow = new StyleEnum<Overflow>(StyleKeyword.Null);

            // Act
            FireGeometry(label);

            // Assert
            Assert.That(label.style.minWidth.keyword, Is.Not.EqualTo(StyleKeyword.Null));
        }

        private static double s_seconds;

        private static double ReadSeconds() => s_seconds;

        // Runs the relative-length poll of what the label has on a laid-out panel, so the resolver writes each bracket
        // percentage as pixels and records it: the poll is the only writer once the first layout has passed, and it
        // fires on the panel's own clock, which is held still afterwards so no later layout pass runs it again.
        private static void RunRelativePoll(VisualElement element)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, ReadSeconds);
            for (var i = 0; i < 2; i++)
            {
                s_seconds += 1d;
                EditorPanelTestHelpers.DriveSchedulerOnce(element.panel);
                EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            }
        }

        // Mounted, then polled and derived once more, which is where a percentage layer's pixels and the derive's
        // record of them exist.
        private Label MountPolled(ReconcilerScope scope, string containerClass, string labelClass)
        {
            var label = MountLabel(scope, containerClass, labelClass, Sentence);
            RunRelativePoll(label);
            FireGeometry(label);
            FireGeometry(label.parent);
            return label;
        }

        private static bool IsPixels(StyleLength length, float pixels)
            => length.keyword == StyleKeyword.Undefined && length.value.unit == LengthUnit.Pixel
                && Mathf.Abs(length.value.value - pixels) < 0.01f;

        // The parent takes a size on its main axis that makes `calc(50%+2px)` of it 3px or 4px, with a layout pass and
        // then only the parent's own geometry event: no scheduler tick runs, so a pixel value a length layer wrote
        // inline stays what it was before the resize.
        private static void ResizeParent(VisualElement parent, bool row)
        {
            if (row)
            {
                parent.style.width = 4f;
            }
            else
            {
                parent.style.height = 2f;
            }
            ForcePanelUpdate(parent.panel);
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentMaxWidth_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthFollowsTheCap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, RowParent, "shrink-0 max-w-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.maxWidth, 1002f);

            // Act
            ResizeParent(parent, true);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minWidth.value.value : float.NaN, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentWidth_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthFollowsTheWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, RowParent, "shrink-0 w-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.width, 1002f);

            // Act
            ResizeParent(parent, true);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minWidth.value.value : float.NaN, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentSizeInARow_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthFollowsTheSize()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, RowParent, "shrink-0 size-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.width, 1002f);

            // Act
            ResizeParent(parent, true);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minWidth.value.value : float.NaN, Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentMaxHeight_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinHeightFollowsTheCap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, ColumnParent, "max-h-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.maxHeight, 1002f);

            // Act
            ResizeParent(parent, false);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minHeight.value.value : float.NaN, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentHeight_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinHeightFollowsTheHeight()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, ColumnParent, "h-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.height, 1002f);

            // Act
            ResizeParent(parent, false);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minHeight.value.value : float.NaN, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void Given_ALabelWithABracketCalcPercentSizeInAColumn_When_ItsParentShrinksAndOnlyItsParentsGeometryEventFires_Then_ItsMinHeightFollowsTheSize()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, ColumnParent, "size-[calc(50%+2px)]");
            var parent = label.parent;
            var written = IsPixels(label.style.height, 1002f);

            // Act
            ResizeParent(parent, false);
            FireGeometry(parent);

            // Assert — NaN where the resolver never wrote the percentage as pixels, which the expected value is not.
            Assert.That(written ? label.style.minHeight.value.value : float.NaN, Is.EqualTo(3f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base caps the minimum by the inline max-width, which is the pixel layer's. A maximum read from the size layer's percentage instead caps it at 1002 and the minimum is the widest word's.
        [Test]
        public void Given_ALabelWithABracketCalcPercentSizeAndAPixelMaxWidth_When_Mounted_Then_ItsMinWidthIsCappedByTheMaxWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, WideRow, "shrink-0 size-[calc(50%+2px)] max-w-[40px]");
            var written = IsPixels(label.style.width, 1002f);

            // Act
            var minimum = written ? label.style.minWidth.value.value : float.NaN;

            // Assert
            Assert.That(minimum, Is.EqualTo(40f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base caps the minimum by the inline width, which the width layer wins over the size layer's. A width read from the size layer's percentage instead caps it at 1002.
        [Test]
        public void Given_ALabelWithAPixelWidthOverABracketCalcPercentSize_When_Mounted_Then_ItsMinWidthIsCappedByTheWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, WideRow, "shrink-0 w-[40px] size-[calc(50%+2px)]");
            var written = IsPixels(label.style.width, 40f);

            // Act
            var minimum = written ? label.style.minWidth.value.value : float.NaN;

            // Assert
            Assert.That(minimum, Is.EqualTo(40f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base caps the minimum by the inline width, which the important size layer holds. A width read from the lower width layer's percentage instead caps it at 1002.
        [Test]
        public void Given_ALabelWithAPercentWidthUnderAHigherKeyedPixelSize_When_Mounted_Then_ItsMinWidthIsCappedByTheSize()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, WideRow, "shrink-0 w-[calc(50%+2px)] !size-[40px]");
            var written = IsPixels(label.style.width, 40f);

            // Act
            var minimum = written ? label.style.minWidth.value.value : float.NaN;

            // Assert
            Assert.That(minimum, Is.EqualTo(40f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base reads the live inline width, which the overwrite set to 40. A width taken from the
        // percentage the layer last wrote instead (the record the resolver keeps) caps the minimum at 1002.
        [Test]
        public void Given_ALabelWithABracketCalcPercentWidth_When_ItsInlineWidthIsOverwrittenAndItsOwnGeometryEventFires_Then_ItsMinWidthFollowsTheOverwrittenWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var label = MountPolled(scope, WideRow, "shrink-0 w-[calc(50%+2px)]");
            var written = IsPixels(label.style.width, 1002f);
            label.style.width = 40f;

            // Act
            FireGeometry(label);

            // Assert — NaN where the resolver never wrote the percentage as pixels, so there was nothing to overwrite.
            Assert.That(written ? label.style.minWidth.value.value : float.NaN, Is.EqualTo(40f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base reads a text whose widest run is as wide as the whole text as one a minimum can reach. Change `>=` to `>` in MinimumCanReach and the equal widths no longer do.
        [Test]
        public void Given_ATextWithABreakInItWhoseWidestRunIsItsWholeWidth_When_AskedWhetherTheMinimumCanReachIt_Then_ItCan()
        {
            // Arrange — two runs and a line break, with the whole width given as what the widest run measures.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "", Sentence);
            const string text = "aa bb\ncc";
            var widest = (float)typeof(StyleFlexMinSizeManipulator).GetMethod("MeasureMinContentWidth", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { label, text, WhiteSpace.Normal })!;
            var method = typeof(StyleFlexMinSizeManipulator).GetMethod("MinimumCanReach", BindingFlags.NonPublic | BindingFlags.Static)!;

            // Act
            var canReach = (bool)method.Invoke(null, new object[] { label, text, WhiteSpace.Normal, widest })!;

            // Assert
            Assert.That(canReach, Is.True);
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
