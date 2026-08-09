using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A prop that stops being declared restores what the element was constructed with, not a constant.
    // Which constant would be wrong depends on the type: a TextElement is built out of the tab ring at
    // -1, and every BaseField delegates focus to the input beneath it, so coalescing to 0 / false handed
    // one type another type's answer. No pool is involved — rendering the prop once and then not is
    // enough.
    internal sealed class ConstructedFocusDefaultTests
    {
        [Test]
        public void Given_a_label_whose_declared_tab_index_is_dropped_When_the_prop_is_applied_Then_it_reads_the_one_it_was_built_with()
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
        public void Given_a_field_whose_declared_focus_delegation_is_dropped_When_the_prop_is_applied_Then_it_reads_the_one_it_was_built_with()
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
        public void Given_an_element_that_never_declared_either_When_the_absent_prop_is_applied_Then_nothing_is_written()
        {
            // Arrange
            var field = new TextField();

            // Act
            FiberPropApplier.ApplyTabIndex(field, null);
            FiberPropApplier.ApplyDelegatesFocus(field, null);

            // Assert
            Assert.That((field.tabIndex, field.delegatesFocus), Is.EqualTo((0, true)));
        }

        [Test]
        public void Given_a_declared_value_written_twice_When_it_is_dropped_Then_the_record_is_the_first_reading_not_the_second()
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
    }
}
