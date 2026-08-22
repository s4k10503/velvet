using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A prop that stops being declared restores what the element was constructed with, not a constant.
    // Which constant would be wrong depends on the type, and the two values are pinned by the tuples
    // below rather than asserted anywhere in prose. No pool is involved: declaring the prop on one
    // render and not the next is enough, through V.Custom<Label> or a Motion node over one. What a pool
    // return does to the same records is the last three cases.
    internal sealed class ConstructedFocusDefaultTests
    {
        [Test]
        public void Given_ALabelWhoseDeclaredTabIndexIsDropped_When_ThePropIsApplied_Then_ItReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var label = new Label();
            var built = label.tabIndex;

            // Act
            FiberPropApplier.ApplyTabIndex(label, 3);
            var declared = label.tabIndex;
            FiberPropApplier.ApplyTabIndex(label, null);

            // Assert
            Assert.That((built, declared, label.tabIndex), Is.EqualTo((-1, 3, -1)));
        }

        [Test]
        public void Given_AFieldWhoseDeclaredFocusDelegationIsDropped_When_ThePropIsApplied_Then_ItReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var field = new TextField();
            var built = field.delegatesFocus;

            // Act
            FiberPropApplier.ApplyDelegatesFocus(field, false);
            var declared = field.delegatesFocus;
            FiberPropApplier.ApplyDelegatesFocus(field, null);

            // Assert
            Assert.That((built, declared, field.delegatesFocus), Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ALabelThatNeverDeclaredATabIndex_When_TheAbsentPropIsApplied_Then_NothingIsWritten()
        {
            // Arrange
            var label = new Label();

            // Act
            FiberPropApplier.ApplyTabIndex(label, null);

            // Assert
            Assert.That(label.tabIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Given_AFieldThatNeverDeclaredFocusDelegation_When_TheAbsentPropIsApplied_Then_NothingIsWritten()
        {
            // Arrange
            var field = new TextField();

            // Act
            FiberPropApplier.ApplyDelegatesFocus(field, null);

            // Assert
            Assert.That(field.delegatesFocus, Is.True);
        }

        [Test]
        public void Given_ADeclaredValueWrittenTwice_When_ItIsDropped_Then_TheRecordIsTheFirstReadingNotTheSecond()
        {
            // Arrange
            var label = new Label();

            // Act
            FiberPropApplier.ApplyTabIndex(label, 3);
            FiberPropApplier.ApplyTabIndex(label, 7);
            FiberPropApplier.ApplyTabIndex(label, null);

            // Assert
            Assert.That(label.tabIndex, Is.EqualTo(-1));
        }

        // The three below stand in for the pool cycle a record must not cross: the return drops the record,
        // so what stands on the element afterwards is what an absent prop leaves alone. Each table is asked
        // separately because dropping it is a line per table.
        [Test]
        public void Given_AFocusableRecordForgottenAsAtAPoolReturn_When_TheAbsentPropIsApplied_Then_TheStandingValueIsLeftAlone()
        {
            // Arrange
            var element = new VisualElement { focusable = false };
            FiberPropApplier.ApplyFocusable(element, true);
            FiberPropApplier.ForgetRecordedDefaults(element);

            // Act
            FiberPropApplier.ApplyFocusable(element, null);

            // Assert
            Assert.That(element.focusable, Is.True);
        }

        [Test]
        public void Given_ATabIndexRecordForgottenAsAtAPoolReturn_When_TheAbsentPropIsApplied_Then_TheStandingValueIsLeftAlone()
        {
            // Arrange
            var label = new Label();
            FiberPropApplier.ApplyTabIndex(label, 3);
            FiberPropApplier.ForgetRecordedDefaults(label);

            // Act
            FiberPropApplier.ApplyTabIndex(label, null);

            // Assert
            Assert.That(label.tabIndex, Is.EqualTo(3));
        }

        [Test]
        public void Given_AFocusDelegationRecordForgottenAsAtAPoolReturn_When_TheAbsentPropIsApplied_Then_TheStandingValueIsLeftAlone()
        {
            // Arrange
            var field = new TextField();
            FiberPropApplier.ApplyDelegatesFocus(field, false);
            FiberPropApplier.ForgetRecordedDefaults(field);

            // Act
            FiberPropApplier.ApplyDelegatesFocus(field, null);

            // Assert
            Assert.That(field.delegatesFocus, Is.False);
        }
    }
}
