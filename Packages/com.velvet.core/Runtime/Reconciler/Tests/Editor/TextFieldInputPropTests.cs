using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the four text-input props <c>V.TextField</c> declares beyond the password flag —
    /// placeholder, maxLength, isReadOnly, isDelayed.
    /// <list type="bullet">
    /// <item>Each reaches the element through a reconcile of the factory's node, not only through a
    /// direct applier call.</item>
    /// <item>Each, once dropped by a later render, restores what the element was constructed with. The
    /// drops are measured on a subclass built away from every default, so an implementation coalescing
    /// to UI Toolkit's own constants fails them.</item>
    /// <item>A member no render has declared — including the password flag, which predates the other
    /// four — is left where a <c>refCallback:</c> put it when a later render redeclares its
    /// neighbours.</item>
    /// <item>An edit a delayed field is still holding reaches the value before the flag comes off,
    /// whichever render takes it off.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldInputPropTests : ReconcilerTestFixture
    {
        // Built away from TextField's own defaults on all four members, so the value a drop must restore
        // differs from the constant an implementation would otherwise coalesce to.
        internal sealed class PrefilledTextField : TextField
        {
            public const string BuiltPlaceholder = "built-in hint";
            public const int BuiltMaxLength = 8;

            public PrefilledTextField()
            {
                textEdition.placeholder = BuiltPlaceholder;
                maxLength = BuiltMaxLength;
                isReadOnly = true;
                isDelayed = true;
            }
        }

        public override void TearDown()
        {
            base.TearDown();
            // The saturating cases below fill a process-wide pool, which the rest of the run would inherit —
            // the pooled-field case among them, whose first unmount needs the room.
            VNodePoolTestAccess.ClearTextFieldPoolForTest();
        }

        [Test]
        public void Given_ADeclaredPlaceholder_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(placeholder: "Search") };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).textEdition.placeholder, Is.EqualTo("Search"));
        }

        [Test]
        public void Given_ADeclaredMaxLength_When_TheFieldIsReconciled_Then_TheEngineLimitStaysUnlimited()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(maxLength: 12) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).maxLength, Is.EqualTo(-1));
        }

        // A finite limit is not written onto the field, so a value longer than it is shown in full while the
        // limit stands and after a later render drops it.
        [Test]
        public void Given_AValueLongerThanADeclaredMaxLength_When_ALaterRenderDropsIt_Then_TheValueStaysWhole()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    FieldValue = "abcdefgh",
                    TextField = new TextFieldSettings(MaxLength: 3),
                }),
            };
            var newTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps { FieldValue = "abcdefgh" }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileLimited = element.Q<TextElement>()!.text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the reading while the limit stands is folded in: a drop that restored a clipped
            // value would satisfy the later reading alone.
            Assert.That(
                (whileLimited, element.Q<TextElement>()!.text),
                Is.EqualTo(("abcdefgh", "abcdefgh")));
        }

        [Test]
        public void Given_ADeclaredReadOnlyFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isReadOnly: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isReadOnly, Is.True);
        }

        [Test]
        public void Given_ADeclaredDelayedFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isDelayed: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isDelayed, Is.True);
        }

        [Test]
        public void Given_APlaceholderDeclaredEmpty_When_TheFieldIsReconciled_Then_TheElementCarriesAnEmptyOne()
        {
            // Arrange — an empty string is a declared empty hint, not an absent one, so it has to reach a
            // field built with a hint of its own and clear it.
            var tree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(Placeholder: string.Empty),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).textEdition.placeholder, Is.Empty);
        }

        [Test]
        public void Given_ADeclaredPlaceholder_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(Placeholder: "declared"),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.textEdition.placeholder;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term separates a restore from a remount, which would satisfy the
            // reading while the tree holds a different element.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.textEdition.placeholder),
                Is.EqualTo((true, "declared", PrefilledTextField.BuiltPlaceholder)));
        }

        [Test]
        public void Given_ADeclaredMaxLength_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(MaxLength: 3),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.maxLength;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.maxLength),
                Is.EqualTo((true, PrefilledTextField.BuiltMaxLength, PrefilledTextField.BuiltMaxLength)));
        }

        [Test]
        public void Given_ADeclaredReadOnlyFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(IsReadOnly: false),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.isReadOnly;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.isReadOnly),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ADeclaredDelayedFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(IsDelayed: false),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.isDelayed;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.isDelayed),
                Is.EqualTo((true, false, true)));
        }

        // The two below arrange the state a delayed field is in mid-edit — text on the inner element, a
        // value that has not received it — and ask what the flag coming off does with it. Both routes out of
        // the flag are asked: a render redeclaring it false, and a render dropping it onto a constructed
        // default of false.
        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderDeclaresTheFlagFalse_Then_TheValueReceivesIt()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true) };
            var newTree = new VNode[] { V.TextField(isDelayed: false) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "typed";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.value, Is.EqualTo("typed"));
        }

        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderDropsTheFlag_Then_TheValueReceivesIt()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true) };
            var newTree = new VNode[] { V.TextField() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "typed";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.value, Is.EqualTo("typed"));
        }

        // The edit is arranged as the two cases above do. Changing the limit leaves it where the user
        // typed it. DelayedMaxLengthEditReportTests measures the same change on a panel, where a write of
        // the text could report.
        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderChangesMaxLength_Then_TheEditIsStillShownUncommitted()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 3) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "abcd";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("abcd", string.Empty)));
        }

        // The flag comes from a refCallback and the first tree declares no text-input prop, so the render
        // declaring the limit is the first to reach the field's settings, with the typing already on screen.
        // One callback instance stands in both trees, as in the refCallback sequence further down.
        [Test]
        public void Given_AnEditInAFieldARefCallbackMadeDelayed_When_TheFirstRenderDeclaringMaxLengthArrives_Then_TheEditIsStillShownUncommitted()
        {
            // Arrange
            Func<VisualElement, Action> makeDelayed = el =>
            {
                ((TextField)el).isDelayed = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(refCallback: makeDelayed) };
            var newTree = new VNode[] { V.TextField(maxLength: 3, refCallback: makeDelayed) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "abcd";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the delayed flag is folded in because the case is about a field the callback made
            // delayed, and the value because the edit has to stay uncommitted.
            Assert.That(
                (element.isDelayed, element.text, element.value),
                Is.EqualTo((true, "abcd", string.Empty)));
        }

        // The value is written from a refCallback through the silent setter, which moves the shown text
        // without a change event, and the limit then arrives, narrows and widens with no edit pending: read
        // as typed, and neither limit change alters what the callback wrote.
        [Test]
        public void Given_ADelayedFieldWhoseValueARefCallbackWroteSilently_When_MaxLengthArrivesThenWidens_Then_TheFieldShowsTheValue()
        {
            // Arrange
            Func<VisualElement, Action> seed = el =>
            {
                ((TextField)el).SetValueWithoutNotify("hello");
                return () => { };
            };
            var mounted = new VNode[] { V.TextField(isDelayed: true, refCallback: seed) };
            var narrowed = new VNode[] { V.TextField(isDelayed: true, maxLength: 3, refCallback: seed) };
            var widened = new VNode[] { V.TextField(isDelayed: true, maxLength: 10, refCallback: seed) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert
            Assert.That(element.text, Is.EqualTo("hello"));
        }

        // A limit a refCallback writes is the field's own maxLength, which keeps a prefix. Declaring a wider
        // limit does not assign that property, so it does not put the value back on screen.
        [Test]
        public void Given_ADelayedFieldARefCallbackLimitedToZero_When_ALaterRenderDeclaresAWiderLimit_Then_TheCallbackLimitStillHidesTheValue()
        {
            // Arrange
            Func<VisualElement, Action> limitToZero = el =>
            {
                ((TextField)el).maxLength = 0;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(value: "abc", isDelayed: true, refCallback: limitToZero) };
            var newTree = new VNode[]
            {
                V.TextField(value: "abc", isDelayed: true, maxLength: 5, refCallback: limitToZero),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileZero = element.text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the reading under the callback's limit is folded in, and the value with it: an empty
            // display whose value was cleared would satisfy the two text readings alone.
            Assert.That((whileZero, element.text, element.value), Is.EqualTo((string.Empty, string.Empty, "abc")));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // The deletion is already as long as the limit it narrows to, so the text stays.
        [Test]
        public void Given_ADeletionTheDelayedFieldHasNotCommitted_When_MaxLengthNarrowsToItThenWidens_Then_TheDeletionSurvives()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "hel";

            // Act
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert — the value is folded in: the deletion stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("hel", "hello")));
        }

        [Test]
        public void Given_TypingTheDelayedFieldHasNotCommitted_When_MaxLengthNarrowsThenWidens_Then_TheTypingSurvives()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "help";

            // Act
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert — the value is folded in: the typing stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("help", "hello")));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // It pins that typing on over a carried edit keeps the hold.
        [Test]
        public void Given_AnEditCarriedAcrossANarrowerLimit_When_TheUserTypesOnToTheValuesDisplayAndTheLimitWidens_Then_TheTypingSurvives()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "he";
            Reconciler.Reconcile(Root, mounted, narrowed);
            ((TextElement)element.textEdition).text = "hel";

            // Act
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert — the value is folded in: the typing stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("hel", "hello")));
        }

        // The user types the carried deletion back to the value, so the field shows what it would with no
        // edit at all, and the limit changes after that leave that text where it is.
        [Test]
        public void Given_ACarriedDeletionTypedBackToTheValue_When_MaxLengthNarrowsThenWidens_Then_TheFieldShowsTheValue()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 4) };
            var widenedAgain = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "he";
            Reconciler.Reconcile(Root, mounted, widened);
            ((TextElement)element.textEdition).text = "hello";

            // Act
            Reconciler.Reconcile(Root, widened, narrowed);
            Reconciler.Reconcile(Root, narrowed, widenedAgain);

            // Assert
            Assert.That((element.text, element.value), Is.EqualTo(("hello", "hello")));
        }

        // A value written away and back by renders takes a record each time, which ends the hold on the
        // edit carried before them. Code outside Velvet then turns multiline on, which shows the value,
        // and widening the limit leaves that text where it is.
        [Test]
        public void Given_AHeldEditWhoseValueRendersWroteAwayAndBack_When_CodeOutsideVelvetShowsTheValueAndTheLimitWidens_Then_TheFieldShowsTheValue()
        {
            // Arrange
            const string value = "a\nbcdef";
            var mounted = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 5) };
            var narrowed = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 3) };
            var away = new VNode[] { V.TextField(value: "zz", isDelayed: true, maxLength: 3) };
            var back = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 5) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "xy";
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, away);
            Reconciler.Reconcile(Root, away, back);
            element.multiline = true;
            var shownByTheCode = element.text;

            // Act
            Reconciler.Reconcile(Root, back, widened);

            // Assert — what the code left is folded in because the case is about that form.
            Assert.That((shownByTheCode, element.text), Is.EqualTo(("a\nbcdef", "a\nbcdef")));
        }

        // The value written silently after the edit was carried replaces what the field shows, so the hold
        // on the carried edit ends with it. The silent write stands in for an effect's.
        [Test]
        public void Given_AnEditCarriedAcrossANarrowerLimit_When_CodeWritesTheValueSilentlyAndTheLimitWidens_Then_TheFieldShowsTheNewValue()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: "hello", isDelayed: true, maxLength: 10) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "hel";
            Reconciler.Reconcile(Root, mounted, narrowed);
            element.SetValueWithoutNotify("world");

            // Act
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert
            Assert.That(element.text, Is.EqualTo("world"));
        }

        // A single-line field shows a value with its line breaks removed and the rest of it intact, so a
        // replacement of that display survives a later limit change.
        [Test]
        public void Given_ASingleLineDelayedFieldWhoseValueHasALineBreak_When_TheUserReplacesItAndMaxLengthChanges_Then_TheReplacementSurvives()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(value: "a\nbcdef", isDelayed: true, maxLength: 3) };
            var newTree = new VNode[] { V.TextField(value: "a\nbcdef", isDelayed: true, maxLength: 5) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var beforeTyping = element.text;
            ((TextElement)element.textEdition).text = "ab";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the reading before the typing is folded in because the case is about that form.
            Assert.That((beforeTyping, element.text), Is.EqualTo(("abcdef", "ab")));
        }

        // The commit is simulated with SimulateChange: the value and shown text land together and the
        // field's change callbacks run. Without the record following the commit, the deletion reads as the
        // text Velvet last wrote.
        [Test]
        public void Given_AnEditDeletedAfterTheFieldCommittedItsText_When_ALaterRenderChangesMaxLength_Then_TheDeletionSurvives()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 3) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            element.SimulateChange("abc");
            ((TextElement)element.textEdition).text = string.Empty;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the deletion was never committed.
            Assert.That((element.text, element.value), Is.EqualTo((string.Empty, "abc")));
        }

        // The pool hands the same instance to the next tenant, so a record the first tenant left stands
        // unless removal forgets it. The second tenant declares no limit at mount, so no limit write
        // refreshes the record before the typing.
        [Test]
        public void Given_ARecycledDelayedField_When_TheNextTenantTypesAndALaterRenderChangesMaxLength_Then_TheEditSurvives()
        {
            // Arrange
            var first = new VNode[] { V.TextField(isDelayed: true) };
            var second = new VNode[] { V.TextField(isDelayed: true) };
            var narrowed = new VNode[] { V.TextField(isDelayed: true, maxLength: 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);
            var firstElement = (TextField)Root!.ElementAt(0);
            firstElement.SimulateChange("abc");
            Reconciler.Reconcile(Root, first, Array.Empty<VNode>());
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), second);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "abc";

            // Act
            Reconciler.Reconcile(Root, second, narrowed);

            // Assert — the instance is folded in: a fresh one passes without the pool being involved.
            Assert.That(
                (ReferenceEquals(element, firstElement), element.text, element.value),
                Is.EqualTo((true, "abc", string.Empty)));
        }

        // The first tenant commits the text the second then types, so a record the removal left standing
        // reads that typing as no edit. The second tenant's own commit is what the record must follow.
        [Test]
        public void Given_ARecycledDelayedField_When_TheNextTenantCommitsThenTypesWhatTheLastTenantCommittedAndMaxLengthChanges_Then_TheEditSurvives()
        {
            // Arrange
            var first = new VNode[] { V.TextField(isDelayed: true) };
            var second = new VNode[] { V.TextField(isDelayed: true) };
            var narrowed = new VNode[] { V.TextField(isDelayed: true, maxLength: 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);
            var firstElement = (TextField)Root!.ElementAt(0);
            firstElement.SimulateChange("abc");
            Reconciler.Reconcile(Root, first, Array.Empty<VNode>());
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), second);
            var element = (TextField)Root!.ElementAt(0);
            element.SimulateChange("xy");
            ((TextElement)element.textEdition).text = "abc";

            // Act
            Reconciler.Reconcile(Root, second, narrowed);

            // Assert — the instance is folded in: a fresh one passes without the pool being involved.
            Assert.That(
                (ReferenceEquals(element, firstElement), element.text, element.value),
                Is.EqualTo((true, "abc", "xy")));
        }

        // Zero is a length a later key may not pass, not a length the edit already on screen is cut to.
        // The committed value is "xy", so the edit still differs from it and reads as uncommitted.
        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderSetsMaxLengthToZero_Then_TheEditStaysWholeAndUncommitted()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 0) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            element.SimulateChange("xy");
            ((TextElement)element.textEdition).text = "abc";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit is not the committed text.
            Assert.That((element.text, element.value), Is.EqualTo(("abc", "xy")));
        }

        // GREEN_ON_BASE(characterization): the base registers no shown-text callback, so nothing accumulates
        // there either. It pins that the branch's removal unregisters the one it registers.
        // Each tenancy that records the shown text registers a change callback on the field, so a removal
        // that left the previous one registered would add one per tenancy. The count is read after each
        // unmount rather than against a constant, so callbacks the element carries for other reasons
        // cancel out.
        [Test]
        public void Given_ARecycledDelayedField_When_ASecondTenancyRecordsAndUnmounts_Then_ItHoldsNoMoreCallbacksThanAfterTheFirst()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isDelayed: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            var firstElement = (TextField)Root!.ElementAt(0);
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());
            var afterFirst = CallbackRegistryProbe.BubbleUpCallbackCount(firstElement);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            var secondElement = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert — the instance is folded in: a fresh one would not be the recycled field.
            Assert.That(
                (ReferenceEquals(secondElement, firstElement),
                    CallbackRegistryProbe.BubbleUpCallbackCount(secondElement) - afterFirst),
                Is.EqualTo((true, 0)));
        }

        // The record follows a controlled value written on a patch, not only the mount's.
        [Test]
        public void Given_AControlledDelayedFieldWhoseValueChanged_When_TheUserTypesTheOldValueAndMaxLengthChanges_Then_TheEditSurvives()
        {
            // Arrange
            var mounted = new VNode[] { V.TextField(value: "a", isDelayed: true, maxLength: 10) };
            var changed = new VNode[] { V.TextField(value: "b", isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: "b", isDelayed: true, maxLength: 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            Reconciler.Reconcile(Root, mounted, changed);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "a";

            // Act
            Reconciler.Reconcile(Root, changed, narrowed);

            // Assert
            Assert.That((element.text, element.value), Is.EqualTo(("a", "b")));
        }

        // The limit narrows and widens with no edit pending. A single-line field shows the value with its
        // line breaks removed, and neither change cuts what remains.
        [Test]
        public void Given_ADelayedValueWithLineBreaksAndNoEdit_When_MaxLengthNarrowsThenWidens_Then_TheFieldShowsTheValueWithoutItsBreak()
        {
            // Arrange
            const string value = "a\nbcdef";
            var mounted = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 10) };
            var narrowed = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 3) };
            var widened = new VNode[] { V.TextField(value: value, isDelayed: true, maxLength: 5) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, mounted, narrowed);
            Reconciler.Reconcile(Root, narrowed, widened);

            // Assert
            Assert.That(element.text, Is.EqualTo("abcdef"));
        }

        // Dropping the prop restores -1, which means no limit and must not be read as a length to cut to.
        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_ALaterRenderDropsMaxLength_Then_TheEditIsStillShownUncommitted()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 3) };
            var newTree = new VNode[] { V.TextField(isDelayed: true) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            ((TextElement)element.textEdition).text = "abc";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That((element.text, element.value, element.maxLength), Is.EqualTo(("abc", string.Empty, -1)));
        }

        // An event from the inner text element is not a commit of the field's value, so it leaves the
        // record where Velvet put it.
        [Test]
        public void Given_AnEditTheDelayedFieldHasNotCommitted_When_TheInnerElementRaisesAChangeEventAndMaxLengthChanges_Then_TheEditSurvives()
        {
            // Arrange
            var oldTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 10) };
            var newTree = new VNode[] { V.TextField(isDelayed: true, maxLength: 8) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var inner = (TextElement)element.textEdition;
            inner.text = "abc";
            using (var evt = ChangeEvent<string>.GetPooled(string.Empty, "abc"))
            {
                element.SimulateBubbledEvent(evt, inner);
            }

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((element.text, element.value), Is.EqualTo(("abc", string.Empty)));
        }

        // A single-line field shows its value with the line breaks stripped, so nothing was typed here
        // although the shown text differs from the value. Changing the limit leaves that text where it is.
        [Test]
        public void Given_ADelayedValueWithLineBreaksAndNoEdit_When_ALaterRenderChangesMaxLength_Then_TheFieldShowsTheValueWithoutItsBreak()
        {
            // Arrange
            const string value = "a\nbcdef";
            var oldTree = new VNode[]
            {
                V.TextField(value: value, isDelayed: true, maxLength: 10),
            };
            var newTree = new VNode[]
            {
                V.TextField(value: value, isDelayed: true, maxLength: 3),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.text, Is.EqualTo("abcdef"));
        }

        [Test]
        public void Given_AFieldThatNeverDeclaredAnyOfThem_When_AbsentSettingsAreApplied_Then_NothingIsWritten()
        {
            // Arrange
            var field = new PrefilledTextField();

            // Act
            FiberPropApplier.ApplyTextField(field, null);

            // Assert
            Assert.That(
                (field.textEdition.placeholder, field.maxLength, field.isReadOnly, field.isDelayed),
                Is.EqualTo((PrefilledTextField.BuiltPlaceholder, PrefilledTextField.BuiltMaxLength, true, true)));
        }

        // The five below share one sequence: a render declares one member, the refCallback — which the
        // create path queues and the pass boundary runs, both after the props are applied, so the record
        // is taken before it — assigns another, and
        // a second render redeclares only the first. The undeclared member is nobody's to write, so it has
        // to survive. One callback instance stands in both trees, so the patch does not re-run it
        // (ReconcilerContext.SyncRefCallback's identity skip) and a second assignment cannot stand in for
        // a survival. Each saturates the TextField pool before the second render, for the reason
        // VNodePoolTestAccess.SaturateTextFieldPoolForTest gives.
        // GREEN_ON_BASE(characterization): the base patches this field, keeping the instance and the flag.
        [Test]
        public void Given_APasswordFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isPasswordField = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), element), element.isPasswordField),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base patches this field, keeping the instance and the hint.
        [Test]
        public void Given_APlaceholderWrittenFromARefCallback_When_ALaterRenderRedeclaresTheMaxLength_Then_TheHintSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setHint = el =>
            {
                ((TextField)el).textEdition.placeholder = "from ref";
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(maxLength: 12, refCallback: setHint) };
            var newTree = new VNode[] { V.TextField(maxLength: 13, refCallback: setHint) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), element), element.textEdition.placeholder),
                Is.EqualTo((true, true, "from ref")));
        }

        // GREEN_ON_BASE(characterization): the base patches this field, keeping the instance and the limit.
        [Test]
        public void Given_AMaxLengthWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheLimitSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setLimit = el =>
            {
                ((TextField)el).maxLength = 4;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setLimit) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setLimit) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), element), element.maxLength),
                Is.EqualTo((true, true, 4)));
        }

        // GREEN_ON_BASE(characterization): the base patches this field, keeping the instance and the flag.
        [Test]
        public void Given_AReadOnlyFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isReadOnly = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), element), element.isReadOnly),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base patches this field, keeping the instance and the flag.
        [Test]
        public void Given_ADelayedFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isDelayed = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), element), element.isDelayed),
                Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the base re-rents the pooled field and patches it, keeping the hint.
        [Test]
        public void Given_APooledFieldWhoseLastTenantDeclaredAPlaceholder_When_ItsNextTenantWritesOneFromARefCallback_Then_TheHintSurvives()
        {
            // Arrange — the first tenancy records a placeholder default and then unmounts, which is what
            // hands the element to the shared pool; the second declares only the length limit. The pool is
            // saturated only once the second tenancy holds the field, since the first unmount needs the room,
            // and from there for the reason VNodePoolTestAccess.SaturateTextFieldPoolForTest gives.
            var declaring = new VNode[] { V.TextField(placeholder: "previous tenant") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), declaring);
            var pooled = (TextField)Root!.ElementAt(0);
            Reconciler.Reconcile(Root, declaring, Array.Empty<VNode>());
            Func<VisualElement, Action> setHint = el =>
            {
                ((TextField)el).textEdition.placeholder = "from ref";
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(maxLength: 12, refCallback: setHint) };
            var newTree = new VNode[] { V.TextField(maxLength: 13, refCallback: setHint) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var poolSaturated = VNodePoolTestAccess.SaturateTextFieldPoolForTest();

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // would carry no record from either tenancy and satisfy the hint on its own.
            Assert.That(
                (poolSaturated, ReferenceEquals(Root!.ElementAt(0), pooled),
                    ((TextField)Root!.ElementAt(0)).textEdition.placeholder),
                Is.EqualTo((true, true, "from ref")));
        }
    }
}
