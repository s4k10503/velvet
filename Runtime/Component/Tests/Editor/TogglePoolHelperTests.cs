using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the Toggle-specific reset contract enforced by
    /// <see cref="FiberTogglePoolHelper.ResetToggleForReuse"/> on top of the shared pool contract in
    /// <see cref="PoolHelperTestsBase{TElement}"/>.
    /// <list type="bullet">
    /// <item>Resetting clears the consumer-set state — value, label, userData, name, tooltip, focusable,
    /// viewDataKey — so the recycled Toggle presents as a freshly constructed instance.</item>
    /// <item>Resetting strips custom USS classes but restores the two built-in styling classes the Toggle
    /// inherits (<see cref="BaseField{T}.ussClassName"/> and <see cref="Toggle.ussClassName"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class TogglePoolHelperTests : PoolHelperTestsBase<Toggle>
    {
        protected override void ClearPool() => VNodePool.ClearTogglePoolForTesting();
        protected override Toggle Rent() => VNodePool.RentToggle();
        protected override void Return(Toggle element) => VNodePool.ReturnToggle(element);
        protected override void Reset(Toggle element) => FiberTogglePoolHelper.ResetToggleForReuse(element);
        protected override int MaxPoolSize => 32;

        protected override void SetElementSpecificGhost(Toggle toggle) => toggle.value = true;

        protected override void AssertElementSpecificGhostCleared(Toggle toggle)
        {
            Assert.IsFalse(toggle.value, "value from the previous use does not survive the pool cycle");
        }

        [Test]
        public void Given_ToggleWithCustomState_When_Reset_Then_ConsumerSetStateIsCleared()
        {
            // Arrange
            var toggle = new Toggle { value = true, label = "Enabled", name = "my-toggle", tooltip = "my-tooltip", focusable = true, viewDataKey = "my-view-data" };
            toggle.AddToClassList("custom-class");
            toggle.style.color = new StyleColor(Color.red);
            toggle.userData = 42;

            // Act
            FiberTogglePoolHelper.ResetToggleForReuse(toggle);

            // Assert
            var actual = (toggle.value, toggle.label, toggle.userData, toggle.name, toggle.tooltip, toggle.focusable, toggle.viewDataKey);
            Assert.AreEqual((false, string.Empty, (object)null, string.Empty, string.Empty, false, (string)null), actual,
                "Reset clears every consumer-set field back to its constructed default");
        }

        [Test]
        public void Given_ToggleWithCustomClass_When_Reset_Then_CustomClassRemovedAndBuiltinClassesRestored()
        {
            // Arrange
            var toggle = new Toggle();
            toggle.AddToClassList("custom-class");

            // Act
            FiberTogglePoolHelper.ResetToggleForReuse(toggle);

            // Assert
            var classes = toggle.GetClasses();
            CollectionAssert.IsSupersetOf(classes, new[] { BaseField<bool>.ussClassName, Toggle.ussClassName },
                "Both inherited built-in styling classes are restored");
            CollectionAssert.DoesNotContain(classes, "custom-class", "Custom classes are removed");
        }
    }
}
