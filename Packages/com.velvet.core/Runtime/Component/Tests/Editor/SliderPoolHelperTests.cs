using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the Slider-specific reset contract enforced by
    /// <see cref="FiberSliderPoolHelper.ResetSliderForReuse"/> on top of the shared pool contract in
    /// <see cref="PoolHelperTestsBase{TElement}"/>.
    /// <list type="bullet">
    /// <item>Resetting clears the consumer-set state — value, label, userData, name, tooltip, focusable,
    /// viewDataKey.</item>
    /// <item>Resetting restores Unity's constructed range defaults: lowValue 0, highValue 10, direction
    /// Horizontal, pageSize 0.</item>
    /// <item>Resetting strips custom USS classes but restores the three built-in styling classes the Slider
    /// inherits (<see cref="BaseField{T}.ussClassName"/>, <see cref="BaseSlider{T}.ussClassName"/>,
    /// <see cref="Slider.ussClassName"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class SliderPoolHelperTests : PoolHelperTestsBase<Slider>
    {
        protected override void ClearPool() => VNodePool.ClearSliderPoolForTesting();
        protected override Slider Rent() => VNodePool.RentSlider();
        protected override void Return(Slider element) => VNodePool.ReturnSlider(element);
        protected override void Reset(Slider element) => FiberSliderPoolHelper.ResetSliderForReuse(element);
        protected override int MaxPoolSize => 32;

        protected override void SetElementSpecificGhost(Slider slider) => slider.value = 3.14f;

        protected override void AssertElementSpecificGhostCleared(Slider slider)
        {
            Assert.AreEqual(0f, slider.value, "value from the previous use does not survive the pool cycle");
        }

        [Test]
        public void Given_SliderWithCustomState_When_Reset_Then_ConsumerSetStateIsCleared()
        {
            // Arrange
            var slider = new Slider { value = 5.5f, label = "Volume", name = "my-slider", tooltip = "my-tooltip", focusable = true, viewDataKey = "my-view-data" };
            slider.AddToClassList("custom-class");
            slider.style.color = new StyleColor(Color.red);
            slider.userData = 42;

            // Act
            FiberSliderPoolHelper.ResetSliderForReuse(slider);

            // Assert
            var actual = (slider.value, slider.label, slider.userData, slider.name, slider.tooltip, slider.focusable, slider.viewDataKey);
            Assert.AreEqual((0f, string.Empty, (object)null, string.Empty, string.Empty, false, (string)null), actual,
                "Reset clears every consumer-set field back to its constructed default");
        }

        [Test]
        public void Given_SliderWithCustomRange_When_Reset_Then_RangeReturnsToUnityDefaults()
        {
            // Arrange
            var slider = new Slider { lowValue = 1f, highValue = 100f, direction = SliderDirection.Vertical, pageSize = 5f };

            // Act
            FiberSliderPoolHelper.ResetSliderForReuse(slider);

            // Assert
            var actual = (slider.lowValue, slider.highValue, slider.direction, slider.pageSize);
            Assert.AreEqual((0f, 10f, SliderDirection.Horizontal, 0f), actual,
                "Reset restores Unity's constructed range defaults (low 0, high 10, Horizontal, pageSize 0)");
        }

        [Test]
        public void Given_SliderWithCustomClass_When_Reset_Then_CustomClassRemovedAndBuiltinClassesRestored()
        {
            // Arrange
            var slider = new Slider();
            slider.AddToClassList("custom-class");

            // Act
            FiberSliderPoolHelper.ResetSliderForReuse(slider);

            // Assert
            var classes = slider.GetClasses();
            CollectionAssert.IsSupersetOf(classes,
                new[] { BaseField<float>.ussClassName, BaseSlider<float>.ussClassName, Slider.ussClassName },
                "All three inherited built-in styling classes are restored");
            CollectionAssert.DoesNotContain(classes, "custom-class", "Custom classes are removed");
        }
    }
}
