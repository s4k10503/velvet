using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // The pool's contract is that a rented element is indistinguishable from a fresh one, and a slider
    // carrying its numeric input field cannot be made to satisfy it — FiberSliderPoolHelper.CanReuse
    // states why. Admission is therefore the scrub for that one property, and this pins both directions:
    // dropping the check would recycle a slider with a stray input field, and widening it to refuse
    // everything would silently turn the pool off.
    internal sealed class SliderPoolAdmissionTests
    {
        [Test]
        public void Given_a_slider_that_built_its_input_field_When_it_is_returned_to_the_pool_Then_only_a_slider_without_one_comes_back()
        {
            // Arrange
            var carrying = new Slider();
            carrying.showInputField = true;
            var built = carrying.Q<TextField>() != null;
            var plain = new Slider();

            // Act
            VNodePool.ReturnSlider(carrying);
            var afterCarrying = VNodePool.RentSlider();
            VNodePool.ReturnSlider(plain);
            var afterPlain = VNodePool.RentSlider();

            // Assert
            Assert.That(
                (built, ReferenceEquals(afterCarrying, carrying), ReferenceEquals(afterPlain, plain)),
                Is.EqualTo((true, false, true)));
        }
    }
}
