using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A stored scroller visibility only answers a write while multiline is on, so a field that arrives at
    // the pool single-line keeps whatever the previous consumer left — invisible until the next consumer
    // turns multiline back on and finds a scrollbar it never asked for. The surface guard cannot see this
    // one: its dirty pass leaves multiline on, which is the state the write already reaches.
    internal sealed class PooledScrollerVisibilityResetTests
    {
        [Test]
        public void Given_a_field_that_arrives_single_line_after_setting_a_scroller_When_it_is_reset_for_reuse_Then_turning_multiline_back_on_shows_no_scroller()
        {
            // Arrange
            var field = new TextField { multiline = true, verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible };
            var stored = field.verticalScrollerVisibility;
            field.multiline = false;

            // Act
            FiberTextFieldPoolHelper.ResetTextFieldForReuse(field);
            field.multiline = true;

            // Assert
            Assert.That(
                (stored, field.verticalScrollerVisibility),
                Is.EqualTo((ScrollerVisibility.AlwaysVisible, ScrollerVisibility.Hidden)));
        }
    }
}
