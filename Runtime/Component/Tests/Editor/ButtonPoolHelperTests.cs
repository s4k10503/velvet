using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the Button-specific reset contract enforced by
    /// <see cref="FiberButtonPoolHelper.ResetButtonForReuse"/> on top of the shared pool contract in
    /// <see cref="PoolHelperTestsBase{TElement}"/>.
    /// <list type="bullet">
    /// <item>Resetting clears the consumer-set state — text, userData, name, tooltip, focusable, viewDataKey —
    /// so the recycled Button presents as a freshly constructed instance.</item>
    /// <item>Resetting strips custom USS classes but restores the built-in styling classes the Button inherits
    /// (<see cref="TextElement.ussClassName"/> from its TextElement base and <see cref="Button.ussClassName"/>),
    /// because Unity built-in styling depends on them.</item>
    /// </list>
    /// </summary>
    internal sealed class ButtonPoolHelperTests : PoolHelperTestsBase<Button>
    {
        protected override void ClearPool() => VNodePool.ClearButtonPoolForTesting();
        protected override Button Rent() => VNodePool.RentButton();
        protected override void Return(Button element) => VNodePool.ReturnButton(element);
        protected override void Reset(Button element) => FiberButtonPoolHelper.ResetButtonForReuse(element);
        protected override int MaxPoolSize => 32;

        protected override void SetElementSpecificGhost(Button button) => button.text = "sentinel";

        protected override void AssertElementSpecificGhostCleared(Button button)
        {
            Assert.AreEqual(string.Empty, button.text, "text from the previous use does not survive the pool cycle");
        }

        [Test]
        public void Given_ButtonWithCustomState_When_Reset_Then_ConsumerSetStateIsCleared()
        {
            // Arrange
            var button = new Button { text = "hello", name = "my-button", tooltip = "my-tooltip", focusable = true, viewDataKey = "my-view-data" };
            button.AddToClassList("custom-class");
            button.style.color = new StyleColor(Color.red);
            button.userData = 42;

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            var actual = (button.text, button.userData, button.name, button.tooltip, button.focusable, button.viewDataKey);
            Assert.AreEqual((string.Empty, (object)null, string.Empty, string.Empty, false, (string)null), actual,
                "Reset clears every consumer-set field back to its constructed default");
        }

        [Test]
        public void Given_ButtonWithCustomClass_When_Reset_Then_CustomClassRemovedAndBuiltinClassesRestored()
        {
            // Arrange
            var button = new Button();
            button.AddToClassList("custom-class");

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            var classes = button.GetClasses();
            CollectionAssert.IsSupersetOf(classes, new[] { TextElement.ussClassName, Button.ussClassName },
                "Both inherited built-in styling classes are restored");
            CollectionAssert.DoesNotContain(classes, "custom-class", "Custom classes are removed");
        }
    }
}
