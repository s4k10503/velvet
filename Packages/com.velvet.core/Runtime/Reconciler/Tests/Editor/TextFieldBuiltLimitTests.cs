using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <c>FiberPropApplier</c> carries a delayed field's text across the writes it still makes
    /// to the engine's own limit: lifting the limit an element was built with when a render declares
    /// <c>maxLength:</c>, and putting it back when a later render drops the declaration. A declared limit
    /// never reaches the engine, so these cases read the engine's limit through an element built with one,
    /// where the limit was once narrowed and widened by the declaration itself.
    /// <list type="bullet">
    /// <item>A lifted tree declares a limit, which takes the element's own limit off.</item>
    /// <item>A built tree declares none, which puts the element's own limit of three back.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldBuiltLimitTests : ReconcilerTestFixture
    {
        internal sealed class BuiltWithThree : TextField
        {
            public BuiltWithThree() => maxLength = 3;
        }

        private static VNode[] One(
            string? value, int? limit, bool delayed, bool? multiline, string? placeholder,
            Func<VisualElement, Action>? refCallback)
        {
            var props = new FiberElementProps
            {
                FieldValue = value,
                TextField = new TextFieldSettings(
                    Placeholder: placeholder, MaxLength: limit, IsDelayed: delayed ? true : (bool?)null)
                {
                    Multiline = multiline,
                },
            };
            return new VNode[] { V.Custom<BuiltWithThree>(props: props, refCallback: refCallback) };
        }

        private static VNode[] Lifted(
            string? value, bool delayed = true, bool? multiline = null, string? placeholder = null,
            Func<VisualElement, Action>? refCallback = null)
            => One(value, 10, delayed, multiline, placeholder, refCallback);

        private static VNode[] Built(
            string? value, bool delayed = true, bool? multiline = null, string? placeholder = null,
            Func<VisualElement, Action>? refCallback = null)
            => One(value, null, delayed, multiline, placeholder, refCallback);

        private TextField Mount(VNode[] tree)
        {
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            return (TextField)Root!.ElementAt(0);
        }

        private static TextElement Input(TextField field) => (TextElement)field.textEdition;

        // GREEN_ON_BASE(characterization): the base's limit writes over this sequence leave the same text.
        // A value written silently from a refCallback is read as the value's display, not as typing, by each
        // limit write that follows.
        [Test]
        public void Given_ADelayedFieldWhoseValueARefCallbackWroteSilently_When_TheDeclarationLiftsDropsAndLiftsAgain_Then_TheFieldShowsTheValue()
        {
            // Arrange
            Func<VisualElement, Action> seed = el =>
            {
                ((TextField)el).SetValueWithoutNotify("hello");
                return () => { };
            };
            var mounted = Built(null, refCallback: seed);
            var lifted = Lifted(null, refCallback: seed);
            var dropped = Built(null, refCallback: seed);
            var element = Mount(mounted);
            Reconciler.Reconcile(Root, mounted, lifted);
            Reconciler.Reconcile(Root, lifted, dropped);

            // Act
            Reconciler.Reconcile(Root, dropped, lifted);

            // Assert
            Assert.That(element.text, Is.EqualTo("hello"));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // It pins that the comparison with the value's display is not asked of an edit it carried: cut to
        // the narrower limit, the deletion equals the value's display under that limit.
        [Test]
        public void Given_ADeletionTheDelayedFieldHasNotCommitted_When_TheLimitIsRestoredToItAndLiftedAgain_Then_TheDeletionSurvives()
        {
            // Arrange
            var mounted = Lifted("hello");
            var dropped = Built("hello");
            var element = Mount(mounted);
            Input(element).text = "hel";
            Reconciler.Reconcile(Root, mounted, dropped);

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert — the value is folded in: the deletion stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("hel", "hello")));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // It pins the same hold for typing the restored limit cuts to the value's display under it.
        [Test]
        public void Given_TypingTheDelayedFieldHasNotCommitted_When_TheLimitCutsItToTheValuesDisplayAndIsLiftedAgain_Then_TheCutEditSurvives()
        {
            // Arrange
            var mounted = Lifted("hello");
            var dropped = Built("hello");
            var element = Mount(mounted);
            Input(element).text = "help";
            Reconciler.Reconcile(Root, mounted, dropped);

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert — the value is folded in: the cut edit stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("hel", "hello")));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // It pins that typing on over a carried edit keeps the hold, here up to the value's display under
        // the restored limit.
        [Test]
        public void Given_AnEditCarriedAcrossARestoredLimit_When_TheUserTypesOnToTheValuesDisplayAndTheLimitIsLifted_Then_TheTypingSurvives()
        {
            // Arrange
            var mounted = Lifted("hello");
            var dropped = Built("hello");
            var element = Mount(mounted);
            Input(element).text = "he";
            Reconciler.Reconcile(Root, mounted, dropped);
            Input(element).text = "hel";

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert — the value is folded in: the typing stays uncommitted.
            Assert.That((element.text, element.value), Is.EqualTo(("hel", "hello")));
        }

        // GREEN_ON_BASE(characterization): the base reads the typed-back text as the value's display.
        // The user types the carried deletion back to the value, so the field shows what it would with no
        // edit at all, and the limit changes after that are the engine's.
        [Test]
        public void Given_ACarriedDeletionTypedBackToTheValue_When_TheLimitIsRestoredAndLiftedAgain_Then_TheFieldShowsTheValue()
        {
            // Arrange
            var mounted = Built("hello");
            var lifted = Lifted("hello");
            var element = Mount(mounted);
            Input(element).text = "he";
            Reconciler.Reconcile(Root, mounted, lifted);
            Input(element).text = "hello";

            // Act
            Reconciler.Reconcile(Root, lifted, mounted);
            Reconciler.Reconcile(Root, mounted, lifted);

            // Assert
            Assert.That((element.text, element.value), Is.EqualTo(("hello", "hello")));
        }

        // GREEN_ON_BASE(characterization): the base ends the hold on a carried edit when a value is written.
        // A value written away and back by renders takes a record each time, which ends the hold on the
        // edit carried before them. Code outside Velvet then turns multiline on, which shows the value cut
        // with its line break, a form of the value's display the record does not hold.
        [Test]
        public void Given_AHeldEditWhoseValueRendersWroteAwayAndBack_When_CodeOutsideVelvetShowsTheValueAndTheLimitIsLifted_Then_TheFieldShowsTheValue()
        {
            // Arrange
            const string value = "a\nbcdef";
            var mounted = Lifted(value);
            var dropped = Built(value);
            var away = Built("zz");
            var element = Mount(mounted);
            Input(element).text = "xy";
            Reconciler.Reconcile(Root, mounted, dropped);
            Reconciler.Reconcile(Root, dropped, away);
            Reconciler.Reconcile(Root, away, dropped);
            element.multiline = true;
            var shownByTheCode = element.text;

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert — what the code left is folded in because the case is about that form.
            Assert.That((shownByTheCode, element.text), Is.EqualTo(("a\nb", "a\nbcdef")));
        }

        // GREEN_ON_BASE(characterization): the base ends the hold on a carried edit when a value is written.
        // The value written silently after the edit was carried replaces what the field shows, so the hold
        // ends with it. The silent write stands in for an effect's.
        [Test]
        public void Given_AnEditCarriedAcrossARestoredLimit_When_CodeWritesTheValueSilentlyAndTheLimitIsLifted_Then_TheFieldShowsTheNewValue()
        {
            // Arrange
            var mounted = Lifted("hello");
            var dropped = Built("hello");
            var element = Mount(mounted);
            Input(element).text = "hel";
            Reconciler.Reconcile(Root, mounted, dropped);
            element.SetValueWithoutNotify("world");

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert
            Assert.That(element.text, Is.EqualTo("world"));
        }

        // GREEN_ON_BASE(characterization): the base reads any shown text other than its record as an edit.
        // A single-line field shows a value cut by a later limit with its line break, so a deletion of that
        // break leaves the record without it; it pins that only multiline coming off is read that way.
        [Test]
        public void Given_ASingleLineDelayedFieldShowingALineBreak_When_TheUserDeletesItAndTheLimitIsLifted_Then_TheDeletionSurvives()
        {
            // Arrange — the value arrives before the limit write that cuts it, which leaves that write's form.
            var mounted = Lifted("a\nbcdef");
            var dropped = Built("a\nbcdef");
            var element = Mount(mounted);
            Reconciler.Reconcile(Root, mounted, dropped);
            var beforeTyping = element.text;
            Input(element).text = "ab";

            // Act
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert — the reading before the typing is folded in because the case is about that form.
            Assert.That((beforeTyping, element.text), Is.EqualTo(("a\nb", "ab")));
        }

        // GREEN_ON_BASE(characterization): the base follows a commit with its record.
        // The commit is simulated with SimulateChange: the value and shown text land together and the
        // field's change callbacks run. Without the record following the commit, the deletion reads as the
        // text Velvet last wrote.
        [Test]
        public void Given_AnEditDeletedAfterTheFieldCommittedItsText_When_TheLimitIsRestored_Then_TheDeletionSurvives()
        {
            // Arrange
            var mounted = Lifted(null);
            var dropped = Built(null);
            var element = Mount(mounted);
            element.SimulateChange("abc");
            Input(element).text = string.Empty;

            // Act
            Reconciler.Reconcile(Root, mounted, dropped);

            // Assert — the value is folded in: the deletion was never committed.
            Assert.That((element.text, element.value), Is.EqualTo((string.Empty, "abc")));
        }

        // GREEN_ON_BASE(characterization): with no edit pending the base leaves the field to the engine.
        // It pins that the restore invents no edit from the stripped line breaks. A single-line field shows
        // its value with the line breaks stripped, so nothing was typed here although the shown text differs
        // from the value. The control is the engine driven through the same writes.
        [Test]
        public void Given_ADelayedValueWithLineBreaksAndNoEdit_When_TheLimitIsRestoredAndLiftedAgain_Then_TheFieldShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var mounted = Lifted(value);
            var dropped = Built(value);
            var element = Mount(mounted);
            var control = new BuiltWithThree { isDelayed = true };
            control.SetValueWithoutNotify(value);
            control.maxLength = -1;
            control.maxLength = 3;
            control.maxLength = -1;

            // Act
            Reconciler.Reconcile(Root, mounted, dropped);
            Reconciler.Reconcile(Root, dropped, mounted);

            // Assert
            Assert.That(element.text, Is.EqualTo(control.text));
        }

        // GREEN_ON_BASE(characterization): with no edit pending the base leaves the field to the engine.
        // It pins the single restore of the case above, which is the form the others start from.
        [Test]
        public void Given_ADelayedValueWithLineBreaksAndNoEdit_When_TheLimitIsRestored_Then_TheFieldShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var mounted = Lifted(value);
            var dropped = Built(value);
            var element = Mount(mounted);
            var control = new BuiltWithThree { isDelayed = true };
            control.SetValueWithoutNotify(value);
            control.maxLength = -1;
            control.maxLength = 3;

            // Act
            Reconciler.Reconcile(Root, mounted, dropped);

            // Assert
            Assert.That(element.text, Is.EqualTo(control.text));
        }

        // GREEN_ON_BASE(characterization): the base reads the silent write as the value's display here too.
        // The value is written from a refCallback through the silent setter, as in the first case, and the
        // limit then lifts, is restored and lifts again with no edit pending: read as typed, what the
        // restored limit left is held over the lifted one.
        [Test]
        public void Given_AMultilineDelayedFieldWhoseValueARefCallbackWroteSilently_When_TheLimitLiftsDropsAndLiftsAgain_Then_TheFieldShowsTheValue()
        {
            // Arrange
            Func<VisualElement, Action> seed = el =>
            {
                ((TextField)el).SetValueWithoutNotify("x\nyz");
                return () => { };
            };
            var mounted = Built(null, multiline: true, refCallback: seed);
            var lifted = Lifted(null, multiline: true, refCallback: seed);
            var element = Mount(mounted);
            Reconciler.Reconcile(Root, mounted, lifted);
            Reconciler.Reconcile(Root, lifted, mounted);

            // Act
            Reconciler.Reconcile(Root, mounted, lifted);

            // Assert
            Assert.That(Input(element).text, Is.EqualTo("x\nyz"));
        }

        // GREEN_ON_BASE(characterization): the base reads the deletion as an edit against the record it took.
        // It pins that the comparison with the value's display drops the line break only where the field
        // is single-line: on a multi-line field the deleted break is an edit.
        [Test]
        public void Given_AMultilineDelayedFieldWhoseEditDeletesTheValuesBreak_When_TheLimitIsRestored_Then_TheEditStaysUncommitted()
        {
            // Arrange
            var mounted = Lifted("a\nb", multiline: true);
            var dropped = Built("a\nb", multiline: true);
            var element = Mount(mounted);
            Input(element).text = "ab";

            // Act
            Reconciler.Reconcile(Root, mounted, dropped);

            // Assert — the value is folded in: the edit stays uncommitted.
            Assert.That((element.value, Input(element).text), Is.EqualTo(("a\nb", "ab")));
        }

        // GREEN_ON_BASE(characterization): the base's limit write reads any shown text other than its record as an edit.
        // It pins that a deletion carried across the restored limit is held across a multiline write as
        // well, where the deletion equals the value's display under that limit.
        [Test]
        public void Given_ADeletionCarriedAcrossARestoredLimit_When_MultilineComesOnAndTheLimitIsLifted_Then_TheDeletionSurvives()
        {
            // Arrange
            var mounted = Lifted("hello");
            var dropped = Built("hello");
            var turnedOn = Built("hello", multiline: true);
            var lifted = Lifted("hello", multiline: true);
            var element = Mount(mounted);
            Input(element).text = "hel";
            Reconciler.Reconcile(Root, mounted, dropped);

            // Act
            Reconciler.Reconcile(Root, dropped, turnedOn);
            Reconciler.Reconcile(Root, turnedOn, lifted);

            // Assert — the value is folded in: the deletion stays uncommitted.
            Assert.That((element.value, Input(element).text), Is.EqualTo(("hello", "hel")));
        }

        // GREEN_ON_BASE(characterization): the base reads typing that reaches the value's display as the engine's.
        // A render repeating the multiline declaration over typing turns nothing, so it holds nothing: the
        // typing later reaches the value's single-line display and a limit change then reads it as the
        // engine's. The control is the engine taking the value and the lifted limit.
        [Test]
        public void Given_TypingOnAFieldAfterMultilineCameOff_When_ARenderRepeatsTheDeclarationAndTheTypingReachesTheValuesDisplay_Then_ALimitChangeShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = Lifted(value, multiline: true);
            var singleLineTree = Built(value, multiline: false);
            var repeatedTree = Built(value, multiline: false, placeholder: "p");
            var liftedTree = Lifted(value, multiline: false, placeholder: "p");
            var element = Mount(multilineTree);
            Reconciler.Reconcile(Root, multilineTree, singleLineTree);
            Input(element).text = "x";
            Reconciler.Reconcile(Root, singleLineTree, repeatedTree);
            Input(element).text = "abc";
            var control = new BuiltWithThree();
            control.SetValueWithoutNotify(value);
            control.maxLength = -1;
            control.multiline = false;

            // Act
            Reconciler.Reconcile(Root, repeatedTree, liftedTree);

            // Assert
            Assert.That(Input(element).text, Is.EqualTo(control.text));
        }

        // GREEN_ON_BASE(characterization): the base shows the value as multiline comes on over text it reads as no edit.
        // The value arrives in a render of its own because a mount writes the value ahead of the limit, and
        // that order shows the form TextFieldMultilineEngineTests pins for a limit written last.
        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseValueArrivedAfterItsLimit_When_ALaterRenderTurnsMultilineOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange
            var mountTree = Built("x");
            var oldTree = Built("a\nbcdef");
            var newTree = Built("a\nbcdef", multiline: true);
            var element = Mount(mountTree);
            Reconciler.Reconcile(Root, mountTree, oldTree);
            var whileSingleLine = Input(element).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the single-line reading is folded in because the case is about what that display showed.
            Assert.That((whileSingleLine, Input(element).text), Is.EqualTo(("abc", "a\nb")));
        }

        // GREEN_ON_BASE(characterization): the base shows the value cut to its limit as multiline comes back on.
        // Turning multiline off drops the break from the multi-line display, which leaves the shown text in
        // neither form a value write or a limit write produces on a single-line field.
        [Test]
        public void Given_AFieldThatIsNotDelayedWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndBackOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = Built(value, delayed: false, multiline: true);
            var oldTree = Built(value, delayed: false, multiline: false);
            var element = Mount(multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var whileSingleLine = Input(element).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, multilineTree);

            // Assert — the single-line reading is folded in because the case is about what that display showed.
            Assert.That((whileSingleLine, Input(element).text), Is.EqualTo(("ab", "a\nb")));
        }

        // GREEN_ON_BASE(characterization): the base shows the value cut to its limit as multiline comes back on.
        // The delayed counterpart of the case above, where the text multiline coming off leaves is what the
        // next write has to tell from an edit: held on screen, a blur would commit it.
        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndBackOn_Then_TheFieldShowsTheValueUpToTheLimit()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = Built(value, multiline: true);
            var oldTree = Built(value, multiline: false);
            var element = Mount(multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var whileSingleLine = Input(element).text;

            // Act
            Reconciler.Reconcile(Root, oldTree, multilineTree);

            // Assert — same single-line term as the case above, and for the same reason.
            Assert.That((whileSingleLine, Input(element).text), Is.EqualTo(("ab", "a\nb")));
        }

        // GREEN_ON_BASE(characterization): the base leaves the field to the engine when the limit lifts with no edit.
        // The other write that tells the shown text from an edit is the limit's. The control is the engine
        // driven with no edit through the writes each render makes, in the order ApplyTextField makes them.
        [Test]
        public void Given_ADelayedFieldWithNoEditWhoseLimitCutsALineBreakValue_When_MultilineComesOffAndTheLimitIsLifted_Then_TheFieldShowsWhatTheEngineAloneWould()
        {
            // Arrange
            const string value = "a\nbcdef";
            var multilineTree = Built(value, multiline: true);
            var oldTree = Built(value, multiline: false);
            var newTree = Lifted(value, multiline: false);
            var element = Mount(multilineTree);
            Reconciler.Reconcile(Root, multilineTree, oldTree);
            var control = new BuiltWithThree { isDelayed = true };
            control.SetValueWithoutNotify(value);
            control.multiline = true;
            control.multiline = false;
            control.maxLength = -1;
            control.multiline = false;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(element.text, Is.EqualTo(control.text));
        }

        // GREEN_ON_BASE(characterization): turning multiline on, the base carries text that differs from the single-line form.
        // "ab" differs from "abc". It pins that the record carries an edit equal to the value cut to its
        // limit with the break removed, which a comparison against that form would read as no edit.
        [Test]
        public void Given_AnEditEqualToTheValueCutWithItsBreakRemoved_When_ALaterRenderTurnsMultilineOn_Then_TheEditStaysUncommitted()
        {
            // Arrange — the value arrives after the limit, so the field shows "abc" and the typing deletes
            // its last character.
            var mountTree = Built("x");
            var oldTree = Built("a\nbcdef");
            var newTree = Built("a\nbcdef", multiline: true);
            var element = Mount(mountTree);
            Reconciler.Reconcile(Root, mountTree, oldTree);
            Input(element).text = "ab";

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the value is read beside the shown text, so the edit has to stay uncommitted as well as
            // on screen.
            Assert.That((element.value, Input(element).text), Is.EqualTo(("a\nbcdef", "ab")));
        }
    }
}
