using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // A text element's own painter is installed by its constructor and bound to that instance, so a
    // recycled one cannot be handed a replacement — FiberElementPoolReset.PaintsOnlyItself states what
    // separates it from anything else on the same delegate. Admission is the scrub, and this pins both
    // directions: dropping it recycles an element that paints the previous consumer's content on top of
    // the next one's, and widening it turns the two hottest pools off.
    internal sealed class TextElementPoolAdmissionTests
    {
        [SetUp]
        public void ClearPools()
        {
            VNodePoolTestAccess.ClearLabelPoolForTest();
            VNodePoolTestAccess.ClearButtonPoolForTest();
        }

        [Test]
        public void Given_a_label_a_consumer_added_a_painter_to_When_it_is_returned_to_the_pool_Then_only_a_label_painting_itself_comes_back()
        {
            // Arrange
            var painting = new Label();
            painting.generateVisualContent += _ => { };
            var plain = new Label();

            // Act
            VNodePool.ReturnLabel(painting);
            var afterPainting = VNodePool.RentLabel(string.Empty);
            VNodePool.ReturnLabel(plain);
            var afterPlain = VNodePool.RentLabel(string.Empty);

            // Assert
            Assert.That(
                (ReferenceEquals(afterPainting, painting), ReferenceEquals(afterPlain, plain)),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_a_button_whose_painter_was_replaced_When_it_is_returned_to_the_pool_Then_it_does_not_come_back()
        {
            // Arrange
            var replaced = new Button();
            replaced.generateVisualContent = _ => { };
            var plain = new Button();

            // Act
            VNodePool.ReturnButton(replaced);
            var afterReplaced = VNodePool.RentButton();
            VNodePool.ReturnButton(plain);
            var afterPlain = VNodePool.RentButton();

            // Assert
            Assert.That(
                (ReferenceEquals(afterReplaced, replaced), ReferenceEquals(afterPlain, plain)),
                Is.EqualTo((false, true)));
        }
    }
}
