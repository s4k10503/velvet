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

        // A length limit narrower than the value leaves the shown text short of the value on a field that is
        // not delayed, so the shown text and the value disagree with no edit pending. The control is a
        // bare field given the same writes in the order the reconciler makes them, so the case asks only
        // that turning multiline on does nothing to a field that is not delayed beyond what the engine does.
        [Test]
        public void Given_AFieldThatIsNotDelayedShowingAClippedValue_When_ALaterRenderTurnsMultilineOn_Then_ItShowsWhatABareFieldWould()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "abcdefgh", maxLength: 3) };
            var newTree = new VNode[] { V.TextField(value: "abcdefgh", maxLength: 3, multiline: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var bare = new TextField();
            bare.SetValueWithoutNotify("abcdefgh");
            bare.maxLength = 3;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);
            bare.multiline = true;

            // Assert
            Assert.That(
                ((TextElement)element.textEdition).text,
                Is.EqualTo(((TextElement)bare.textEdition).text));
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
