using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the Label-specific reset contract enforced by
    /// <see cref="FiberLabelPoolHelper.ResetLabelForReuse"/> on top of the shared pool contract in
    /// <see cref="PoolHelperTestsBase{TElement}"/>.
    /// <list type="bullet">
    /// <item>Rent takes the new text, so a recycled Label shows the value supplied to the current Rent rather
    /// than the prior consumer's text.</item>
    /// <item>Resetting clears the consumer-set state — text, userData, name, tooltip, focusable, viewDataKey.</item>
    /// <item>Resetting strips custom USS classes but restores the built-in styling classes the Label inherits
    /// (<see cref="TextElement.ussClassName"/> from its TextElement base and <see cref="Label.ussClassName"/>).</item>
    /// </list>
    /// </summary>
    internal sealed class LabelPoolHelperTests : PoolHelperTestsBase<Label>
    {
        protected override void ClearPool() => VNodePool.ClearLabelPoolForTesting();
        protected override Label Rent() => VNodePool.RentLabel("test");
        protected override void Return(Label element) => VNodePool.ReturnLabel(element);
        protected override void Reset(Label element) => FiberLabelPoolHelper.ResetLabelForReuse(element);
        protected override int MaxPoolSize => 32;

        protected override void SetElementSpecificGhost(Label label) => label.text = "sentinel";

        protected override void AssertElementSpecificGhostCleared(Label label)
        {
            // Unlike Button, RentLabel reapplies the requested text on a recycled instance, so the prior
            // tenant's "sentinel" ghost manifests as being replaced by the current rent's text rather than emptied.
            Assert.AreEqual("test", label.text, "the prior use's text does not survive the pool cycle");
        }

        [Test]
        public void Given_ReturnedLabel_When_RentedWithNewText_Then_TextReflectsCurrentRent()
        {
            // Arrange
            var sentinel = VNodePool.RentLabel("sentinel");
            VNodePool.ReturnLabel(sentinel);

            // Act
            var rented = VNodePool.RentLabel("reused");
            Assume.That(rented, Is.SameAs(sentinel), "Precondition: the pool recycled the returned instance");

            // Assert
            Assert.AreEqual("reused", rented.text, "RentLabel applies the requested text to the recycled instance");
        }

        [Test]
        public void Given_LabelWithCustomState_When_Reset_Then_ConsumerSetStateIsCleared()
        {
            // Arrange
            var label = new Label("hello") { name = "my-label", tooltip = "my-tooltip", focusable = true, viewDataKey = "my-view-data" };
            label.AddToClassList("custom-class");
            label.style.color = new StyleColor(Color.red);
            label.userData = 42;

            // Act
            FiberLabelPoolHelper.ResetLabelForReuse(label);

            // Assert
            var actual = (label.text, label.userData, label.name, label.tooltip, label.focusable, label.viewDataKey);
            Assert.AreEqual((string.Empty, (object)null, string.Empty, string.Empty, false, (string)null), actual,
                "Reset clears every consumer-set field back to its constructed default");
        }

        [Test]
        public void Given_LabelWithCustomClass_When_Reset_Then_CustomClassRemovedAndBuiltinClassesRestored()
        {
            // Arrange
            var label = new Label("hello");
            label.AddToClassList("custom-class");

            // Act
            FiberLabelPoolHelper.ResetLabelForReuse(label);

            // Assert
            var classes = label.GetClasses();
            CollectionAssert.IsSupersetOf(classes, new[] { TextElement.ussClassName, Label.ussClassName },
                "Both inherited built-in styling classes are restored");
            CollectionAssert.DoesNotContain(classes, "custom-class", "Custom classes are removed");
        }
    }
}
