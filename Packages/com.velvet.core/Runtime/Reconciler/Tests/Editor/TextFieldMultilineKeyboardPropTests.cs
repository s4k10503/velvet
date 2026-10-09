using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.TextField</c>'s <c>multiline:</c>, <c>keyboardType:</c> and <c>autoCorrection:</c>
    /// on the undeclared-when-null contract <see cref="TextFieldInputPropTests"/> pins for the props before
    /// them, plus what a declared multiline does to a value carrying line breaks and to a delayed field's
    /// uncommitted edit.
    /// <para/>
    /// The keyboard cases read the element's properties. That is what Velvet writes; what a touch-screen
    /// platform's soft keyboard then does is not measured here.
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldMultilineKeyboardPropTests : ReconcilerTestFixture
    {
        // Built away from TextField's own defaults on all three members, so the value a drop must restore
        // differs from the constant an implementation would otherwise coalesce to.
        internal sealed class PrefilledTextField : TextField
        {
            public const TouchScreenKeyboardType BuiltKeyboardType = TouchScreenKeyboardType.EmailAddress;

            public PrefilledTextField()
            {
                multiline = true;
                keyboardType = BuiltKeyboardType;
                autoCorrection = true;
            }
        }

        [Test]
        public void Given_ADeclaredMultilineFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(multiline: true) };
            var constructed = new TextField().multiline;

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — the constructed reading is folded in because a field built with the flag on would
            // satisfy the declared one with nothing written.
            Assert.That(
                (constructed, ((TextField)Root!.ElementAt(0)).multiline),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADeclaredKeyboardType_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(keyboardType: TouchScreenKeyboardType.NumberPad) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).keyboardType, Is.EqualTo(TouchScreenKeyboardType.NumberPad));
        }

        [Test]
        public void Given_ADeclaredAutoCorrectionFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(autoCorrection: true) };
            var constructed = new TextField().autoCorrection;

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — same constructed term, and for the same reason.
            Assert.That(
                (constructed, ((TextField)Root!.ElementAt(0)).autoCorrection),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADeclaredMultilineFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings { Multiline = false },
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.multiline;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term separates a restore from a remount, which would satisfy the
            // reading while the tree holds a different element.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.multiline),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ADeclaredKeyboardType_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings { KeyboardType = TouchScreenKeyboardType.NumberPad },
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.keyboardType;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.keyboardType),
                Is.EqualTo((true, TouchScreenKeyboardType.NumberPad, PrefilledTextField.BuiltKeyboardType)));
        }

        [Test]
        public void Given_ADeclaredAutoCorrectionFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings { AutoCorrection = false },
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.autoCorrection;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.autoCorrection),
                Is.EqualTo((true, false, true)));
        }

        // The three below share TextFieldInputPropTests' refCallback sequence, and its reason for one
        // callback instance standing in both trees.
        [Test]
        public void Given_AMultilineFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).multiline = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.multiline),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AKeyboardTypeWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheTypeSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setType = el =>
            {
                ((TextField)el).keyboardType = TouchScreenKeyboardType.PhonePad;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setType) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setType) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.keyboardType),
                Is.EqualTo((true, TouchScreenKeyboardType.PhonePad)));
        }

        [Test]
        public void Given_AnAutoCorrectionFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).autoCorrection = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.autoCorrection),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AValueWithALineBreak_When_AMultilineFieldIsMounted_Then_TheFieldShowsTheBreak()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(value: "first\nsecond", multiline: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — the value is read beside the shown text, so the break has to survive on both.
            var element = (TextField)Root!.ElementAt(0);
            Assert.That(
                element.value + "|" + ((TextElement)element.textEdition).text,
                Is.EqualTo("first\nsecond|first\nsecond"));
        }

        [Test]
        public void Given_AValueWithALineBreak_When_ALaterRenderDeclaresMultiline_Then_TheFieldShowsTheBreak()
        {
            // Arrange — the value is the same in both trees, so the patch reaches the element through the
            // settings alone.
            var oldTree = new VNode[] { V.TextField(value: "first\nsecond") };
            var newTree = new VNode[] { V.TextField(value: "first\nsecond", multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), ((TextElement)element.textEdition).text),
                Is.EqualTo((true, "first\nsecond")));
        }

        [Test]
        public void Given_AMultilineEditTheDelayedFieldHasNotCommitted_When_ALaterRenderDropsBothFlags_Then_TheValueKeepsTheBreak()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, multiline: true) };
            var newTree = new VNode[] { V.TextField() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "first\nsecond";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.value, Is.EqualTo("first\nsecond"));
        }

        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderTurnsMultilineOn_Then_TheTypedTextStays()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "saved", isDelayed: true) };
            var newTree = new VNode[] { V.TextField(value: "saved", isDelayed: true, multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "draft";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is read beside the shown text, so the edit has to stay uncommitted as well as
            // on screen.
            Assert.That(
                (element.multiline, element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo((true, "saved", "draft")));
        }

        // GREEN_ON_BASE(characterization): the base carries this typing because it differs from the value.
        // Its comparison with a single-line form of the value needs no record; the case pins that the
        // branch's record, which replaced that comparison, is taken before the typing.
        // The multiline counterpart of TextFieldInputPropTests' case for a field a refCallback made delayed:
        // the render turning multiline on is the first to reach the field's settings, with the typing already
        // on screen. One callback instance stands in both trees, as in the refCallback cases above.
        [Test]
        public void Given_AnEditInAFieldARefCallbackMadeDelayed_When_TheFirstRenderDeclaringMultilineArrives_Then_TheTypedTextStays()
        {
            // Arrange
            Func<VisualElement, Action> makeDelayed = el =>
            {
                ((TextField)el).isDelayed = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(refCallback: makeDelayed) };
            var newTree = new VNode[] { V.TextField(multiline: true, refCallback: makeDelayed) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "draft";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the delayed flag is folded in because the case is about a field the callback made
            // delayed, and the value because the edit has to stay uncommitted.
            Assert.That(
                (element.isDelayed, element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo((true, string.Empty, "draft")));
        }

        // GREEN_ON_BASE(characterization): the base compares the shown text with the value's single-line form.
        // The silent write leaves exactly that form, so the base shows the value as multiline comes on. It
        // pins that the branch, whose record the silent write leaves stale, does not read that text as typed.
        [Test]
        public void Given_ADelayedFieldWhoseValueARefCallbackWroteSilently_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheValue()
        {
            // Arrange
            Func<VisualElement, Action> seed = el =>
            {
                ((TextField)el).SetValueWithoutNotify("line1\nline2");
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(isDelayed: true, refCallback: seed) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, multiline: true, refCallback: seed) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((TextElement)element.textEdition).text, Is.EqualTo("line1\nline2"));
        }

        // The value is written from a refCallback through the silent setter, as in the case above, and the
        // limit then narrows and widens with no edit pending: read as typed, what the narrower limit left
        // is held over the wider one.
        [Test]
        public void Given_AMultilineDelayedFieldWhoseValueARefCallbackWroteSilently_When_MaxLengthNarrowsThenWidens_Then_TheFieldShowsTheValue()
        {
            // Arrange
            Func<VisualElement, Action> seed = el =>
            {
                ((TextField)el).SetValueWithoutNotify("x\ny");
                return () => { };
            };
            var mounted = new VNode[] { V.TextField(isDelayed: true, multiline: true, refCallback: seed) };
            var narrowed = new VNode[]
            {
                V.TextField(maxLength: 2, isDelayed: true, multiline: true, refCallback: seed),
            };
            var widened = new VNode[]
            {
                V.TextField(maxLength: 5, isDelayed: true, multiline: true, refCallback: seed),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert
            Assert.That(((TextElement)element.textEdition).text, Is.EqualTo("x\ny"));
        }

        // GREEN_ON_BASE(characterization): the base reads the deletion as an edit against the record it took.
        // It pins that the branch's comparison with the value's display drops the line break only where the
        // field is single-line: on a multi-line field the deleted break is an edit.
        [Test]
        public void Given_AMultilineDelayedFieldWhoseEditDeletesTheValuesBreak_When_ALaterRenderChangesMaxLength_Then_TheEditStaysUncommitted()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "a\nb", maxLength: 10, isDelayed: true, multiline: true) };
            var newTree = new VNode[] { V.TextField(value: "a\nb", maxLength: 8, isDelayed: true, multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "ab";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo(("a\nb", "ab")));
        }

        // GREEN_ON_BASE(characterization): the base's limit write reads any shown text other than its record as an edit.
        // It pins that the branch holds a deletion the narrower limit carried across a multiline write as
        // well, where the deletion equals the value's display under that limit.
        [Test]
        public void Given_ADeletionCarriedAcrossANarrowerLimit_When_MultilineComesOnAndTheLimitWidens_Then_TheDeletionSurvives()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var turnedOn = new VNode[]
            {
                V.TextField(value: "hello", isDelayed: true, maxLength: 3, multiline: true),
            };
            var widened = new VNode[]
            {
                V.TextField(value: "hello", isDelayed: true, maxLength: 10, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "hel";
            Reconciler.Reconcile(Root, mounted, narrowed);

            // Act
            Reconciler.Reconcile(Root, narrowed, turnedOn);
            Reconciler.Reconcile(Root, turnedOn, widened);

            // Assert — the value is folded in: the deletion stays uncommitted.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo(("hello", "hel")));
        }

        // A render repeating the multiline declaration over typing turns nothing, so it holds nothing: the
        // typing later reaches the value's single-line display and a limit change then reads it as the
        // engine's. The field goes through the on and off renders of
        // Given_ADelayedFieldWithNoEditWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndBackOn_Then_TheFieldShowsTheValueUpToTheLimit,
        // after which the record is no form of that display.
        [Test]
        public void Given_TypingOnAFieldAfterMultilineCameOff_When_ARenderRepeatsTheDeclarationAndTheTypingReachesTheValuesDisplay_Then_ALimitChangeShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 3, isDelayed: true, multiline: true),
            };
            var singleLineTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 3, isDelayed: true, multiline: false),
            };
            var repeatedTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 3, isDelayed: true, multiline: false, placeholder: "p"),
            };
            var widenedTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 5, isDelayed: true, multiline: false, placeholder: "p"),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), multilineTree);
            Reconciler.Reconcile(Root, multilineTree, singleLineTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "x";
            Reconciler.Reconcile(Root, singleLineTree, repeatedTree);
            ((TextElement)element.textEdition).text = "abc";
            // The control is the engine taking the value, the wider limit and the repeated declaration, the
            // last two in the order the last render writes them.
            var control = new TextField();
            control.SetValueWithoutNotify(value);
            control.maxLength = 5;
            control.multiline = false;

            // Act
            Reconciler.Reconcile(Root, repeatedTree, widenedTree);

            // Assert
            Assert.That(((TextElement)element.textEdition).text, Is.EqualTo(control.text));
        }

        // The deletion of the value's line break survives multiline coming off unchanged, and is then what
        // the value's single-line display shows.
        [Test]
        public void Given_ADeletionOfTheValuesBreakTheDelayedFieldHasNotCommitted_When_MultilineComesOffAndBackOn_Then_TheDeletionSurvives()
        {
            // Arrange
            var multilineTree = new VNode[] { V.TextField(value: "a\nb", isDelayed: true, multiline: true) };
            var singleLineTree = new VNode[] { V.TextField(value: "a\nb", isDelayed: true, multiline: false) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), multilineTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "ab";

            // Act
            Reconciler.Reconcile(Root, multilineTree, singleLineTree);
            Reconciler.Reconcile(Root, singleLineTree, multilineTree);

            // Assert — the value is folded in: the deletion stays uncommitted.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo(("a\nb", "ab")));
        }

        // Code outside Velvet turns multiline off directly, which drops the break the limit left after it,
        // so the field shows neither form of the value's display.
        [Test]
        public void Given_ADelayedFieldWhoseMultilineCodeOutsideVelvetTurnedOff_When_ALaterRenderWidensTheLimit_Then_TheFieldShowsTheValueUpToIt()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: true),
            };
            var newTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 5, isDelayed: true, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            element.multiline = false;
            var whileOff = ((TextElement)element.textEdition).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the reading while off is folded in because the case is about what that write left.
            Assert.That(
                (whileOff, ((TextElement)element.textEdition).text),
                Is.EqualTo(("ab", "a\nbcd")));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // It pins that the branch reads multiline coming off outside Velvet as no edit only where the field
        // then shows the record without its breaks, and not over typing that differs from it.
        [Test]
        public void Given_AnEditOnADelayedFieldWhoseMultilineCodeOutsideVelvetTurnedOff_When_ALaterRenderDeclaresALimit_Then_TheEditStaysWithoutItsBreak()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, multiline: true) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, multiline: true, maxLength: 20) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "first\nsecond";
            element.multiline = false;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo((string.Empty, "firstsecond")));
        }

        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseValueHasALineBreak_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheBreak()
        {
            // Arrange — the single-line display leaves the break out, so the shown text differs from the value
            // with no edit pending.
            var oldTree = new VNode[] { V.TextField(value: "first\nsecond", isDelayed: true) };
            var newTree = new VNode[] { V.TextField(value: "first\nsecond", isDelayed: true, multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((TextElement)element.textEdition).text, Is.EqualTo("first\nsecond"));
        }

        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseValueArrivedAfterItsLimit_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange — the value arrives in a render of its own because a mount writes the value ahead of the
            // limit, and that order shows the form TextFieldMultilineEngineTests pins for a limit written last.
            var mountTree = new VNode[] { V.TextField(value: "x", maxLength: 3, isDelayed: true) };
            var oldTree = new VNode[] { V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true) };
            var newTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mountTree);
            Reconciler.Reconcile(Root, mountTree, oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileSingleLine = ((TextElement)element.textEdition).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the single-line reading is folded in because the case is about what that display showed.
            Assert.That(
                (whileSingleLine, ((TextElement)element.textEdition).text),
                Is.EqualTo(("abc", "a\nb")));
        }

        [Test]
        public void Given_AFieldThatIsNotDelayedWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndBackOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange — turning multiline off drops the break from the multi-line display, which leaves the
            // shown text in neither form a value write or a limit write produces on a single-line field.
            var multilineTree = new VNode[] { V.TextField(value: "a\nbcdef", maxLength: 3, multiline: true) };
            var oldTree = new VNode[] { V.TextField(value: "a\nbcdef", maxLength: 3, multiline: false) };
            var newTree = new VNode[] { V.TextField(value: "a\nbcdef", maxLength: 3, multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileSingleLine = ((TextElement)element.textEdition).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the single-line reading is folded in because the case is about what that display showed.
            Assert.That(
                (whileSingleLine, ((TextElement)element.textEdition).text),
                Is.EqualTo(("ab", "a\nb")));
        }

        // The delayed counterpart of the case above, where the text multiline coming off leaves is what the
        // next write has to tell from an edit: held on screen, a blur would commit it.
        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndBackOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange
            var multilineTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: true),
            };
            var oldTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: false),
            };
            var newTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileSingleLine = ((TextElement)element.textEdition).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same single-line term as the case above, and for the same reason.
            Assert.That(
                (whileSingleLine, ((TextElement)element.textEdition).text),
                Is.EqualTo(("ab", "a\nb")));
        }

        // The other write that tells the shown text from an edit is the limit's. The control is the engine
        // driven with no edit through the writes each render makes, in the order ApplyTextField makes them,
        // as TextFieldInputPropTests' line-break limit case does.
        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndALaterRenderWidensTheLimit_Then_TheFieldShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 3, isDelayed: true, multiline: true),
            };
            var oldTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 3, isDelayed: true, multiline: false),
            };
            var newTree = new VNode[]
            {
                V.TextField(value: value, maxLength: 5, isDelayed: true, multiline: false),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var control = new TextField();
            control.SetValueWithoutNotify(value);
            control.maxLength = 3;
            control.isDelayed = true;
            control.multiline = true;
            control.multiline = false;
            control.maxLength = 5;
            control.multiline = false;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.text, Is.EqualTo(control.text));
        }

        // GREEN_ON_BASE(characterization): turning multiline on, the base carries shown text that differs from
        // its single-line form of the value, and "ab" differs from "abc". It pins that the branch's record
        // carries an edit equal to the value cut to its limit with the break removed, which a comparison
        // against that form would read as no edit.
        [Test]
        public void Given_AnEditEqualToTheValueCutWithItsBreakRemoved_When_ALaterRenderTurnsMultilineOn_Then_TheEditStaysUncommitted()
        {
            // Arrange — the value arrives after the limit, as in
            // Given_ADelayedFieldWithNoEditWhoseValueArrivedAfterItsLimit_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheValueUpToTheLimit,
            // so the field shows "abc" and the typing deletes its last character.
            var mountTree = new VNode[] { V.TextField(value: "x", maxLength: 3, isDelayed: true) };
            var oldTree = new VNode[] { V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true) };
            var newTree = new VNode[]
            {
                V.TextField(value: "a\nbcdef", maxLength: 3, isDelayed: true, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mountTree);
            Reconciler.Reconcile(Root, mountTree, oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "ab";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is read beside the shown text, so the edit has to stay uncommitted as well as
            // on screen.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo(("a\nbcdef", "ab")));
        }

        // GREEN_ON_BASE(characterization): the base writes the flag alone as multiline comes off. It pins
        // that the branch carries nothing across that write, where the engine has already dropped the
        // edit's break.
        [Test]
        public void Given_AMultilineEditTheDelayedFieldHasNotCommitted_When_ALaterRenderTurnsMultilineOff_Then_TheEditStaysWithoutItsBreak()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, multiline: true) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, multiline: false) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "first\nsecond";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That(
                (element.value, ((TextElement)element.textEdition).text),
                Is.EqualTo((string.Empty, "firstsecond")));
        }

        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseValueFitsItsLimit_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheBreak()
        {
            // Arrange — a limit longer than the value, so the single-line display is not cut at all.
            var oldTree = new VNode[] { V.TextField(value: "first\nsecond", maxLength: 20, isDelayed: true) };
            var newTree = new VNode[]
            {
                V.TextField(value: "first\nsecond", maxLength: 20, isDelayed: true, multiline: true),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(((TextElement)element.textEdition).text, Is.EqualTo("first\nsecond"));
        }

        [Test]
        public void Given_AFieldDeclaredPasswordAndMultiline_When_ItIsReconciled_Then_ItCarriesBoth()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isPasswordField: true, multiline: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var element = (TextField)Root!.ElementAt(0);
            Assert.That((element.isPasswordField, element.multiline), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ADelayedFieldDeclaredSingleLine_When_ItIsReconciled_Then_ItStaysSingleLine()
        {
            // Arrange — the delayed flag is written ahead of multiline, so the multiline write meets a delayed
            // field.
            var tree = new VNode[] { V.TextField(isDelayed: true, multiline: false) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).multiline, Is.False);
        }
    }
}
