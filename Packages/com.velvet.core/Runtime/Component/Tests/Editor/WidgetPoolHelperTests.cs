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
            var button = new Button { text = "hello", name = "my-button", tooltip = "my-tooltip", focusable = false, viewDataKey = "my-view-data" };
            button.AddToClassList("custom-class");
            button.style.color = new StyleColor(Color.red);
            button.userData = 42;

            // Act
            FiberButtonPoolHelper.ResetButtonForReuse(button);

            // Assert
            var actual = (button.text, button.userData, button.name, button.tooltip, button.focusable, button.viewDataKey);
            Assert.AreEqual((string.Empty, (object)null, string.Empty, string.Empty, true, (string)null), actual,
                "Reset returns every consumer-set field to its constructed default (focusable is TRUE for this widget type)");
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
            var slider = new Slider { value = 5.5f, label = "Volume", name = "my-slider", tooltip = "my-tooltip", focusable = false, viewDataKey = "my-view-data" };
            slider.AddToClassList("custom-class");
            slider.style.color = new StyleColor(Color.red);
            slider.userData = 42;

            // Act
            FiberSliderPoolHelper.ResetSliderForReuse(slider);

            // Assert
            var actual = (slider.value, slider.label, slider.userData, slider.name, slider.tooltip, slider.focusable, slider.viewDataKey);
            Assert.AreEqual((0f, string.Empty, (object)null, string.Empty, string.Empty, true, (string)null), actual,
                "Reset returns every consumer-set field to its constructed default (focusable is TRUE for this widget type)");
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

    /// <summary>
    /// Specifies the TextField-specific reset contract enforced by
    /// <see cref="FiberTextFieldPoolHelper.ResetTextFieldForReuse"/> on top of the shared pool contract in
    /// <see cref="PoolHelperTestsBase{TElement}"/>. TextField is security-critical: a pooled instance may have
    /// held PII (passwords, email addresses, player names), so reset must guarantee no residue reaches the next
    /// consumer.
    /// <list type="bullet">
    /// <item>The stored value is cleared, so PII text never leaks to the next consumer.</item>
    /// <item>The password-masking flag is cleared, so a prior password field cannot ghost its masking state into
    /// a non-password consumer.</item>
    /// <item>maxLength returns to the Unity default of -1 (unlimited).</item>
    /// <item>Resetting clears the remaining consumer-set state — label, userData, name, tooltip, focusable,
    /// viewDataKey.</item>
    /// <item>Resetting strips custom USS classes but restores the three built-in styling classes the TextField
    /// inherits (<see cref="BaseField{T}.ussClassName"/>, <see cref="TextInputBaseField{T}.ussClassName"/>,
    /// <see cref="TextField.ussClassName"/>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The helper also calls <c>textSelection.SelectNone()</c> to clear cursor / selection state. An EditMode
    /// TextField has no panel, so <c>selectingManipulator</c> does not surface cursor changes through
    /// ITextSelection and that effect cannot be asserted here; the call is exercised only for absence of
    /// exceptions, and visual cursor residue is caught at runtime under a panel.
    /// </remarks>
    internal sealed class TextFieldPoolHelperTests : PoolHelperTestsBase<TextField>
    {
        protected override void ClearPool() => VNodePool.ClearTextFieldPoolForTesting();
        protected override TextField Rent() => VNodePool.RentTextField();
        protected override void Return(TextField element) => VNodePool.ReturnTextField(element);
        protected override void Reset(TextField element) => FiberTextFieldPoolHelper.ResetTextFieldForReuse(element);
        protected override int MaxPoolSize => 32;

        protected override void SetElementSpecificGhost(TextField textField)
        {
            textField.value = "secret-password";
            textField.textEdition.isPassword = true;
        }

        protected override void AssertElementSpecificGhostCleared(TextField textField)
        {
            var actual = (textField.value, textField.textEdition.isPassword);
            Assert.AreEqual((string.Empty, false), actual,
                "Neither the stored value (potentially PII) nor the password-masking flag survives the pool cycle");
        }

        [Test]
        public void Given_TextFieldHoldingPii_When_Reset_Then_ValueAndPasswordFlagAndMaxLengthAreCleared()
        {
            // Arrange
            var textField = new TextField { value = "user@example.com", maxLength = 100 };
            textField.textEdition.isPassword = true;

            // Act
            FiberTextFieldPoolHelper.ResetTextFieldForReuse(textField);

            // Assert
            var actual = (textField.value, textField.textEdition.isPassword, textField.maxLength);
            Assert.AreEqual((string.Empty, false, -1), actual,
                "PII value, password-masking flag, and maxLength are all reset (value empty, masking off, maxLength -1 unlimited)");
        }

        [Test]
        public void Given_TextFieldWithCustomState_When_Reset_Then_ConsumerSetStateIsCleared()
        {
            // Arrange
            var textField = new TextField { label = "Email", name = "my-text-field", tooltip = "my-tooltip", focusable = false, viewDataKey = "my-view-data" };
            textField.AddToClassList("custom-class");
            textField.style.color = new StyleColor(Color.red);
            textField.userData = 42;

            // Act
            FiberTextFieldPoolHelper.ResetTextFieldForReuse(textField);

            // Assert
            var actual = (textField.label, textField.userData, textField.name, textField.tooltip, textField.focusable, textField.viewDataKey);
            Assert.AreEqual((string.Empty, (object)null, string.Empty, string.Empty, true, (string)null), actual,
                "Reset returns every consumer-set field to its constructed default (focusable is TRUE for this widget type)");
        }

        [Test]
        public void Given_TextFieldWithCustomClass_When_Reset_Then_CustomClassRemovedAndBuiltinClassesRestored()
        {
            // Arrange
            var textField = new TextField();
            textField.AddToClassList("custom-class");

            // Act
            FiberTextFieldPoolHelper.ResetTextFieldForReuse(textField);

            // Assert
            var classes = textField.GetClasses();
            CollectionAssert.IsSupersetOf(classes,
                new[] { BaseField<string>.ussClassName, TextInputBaseField<string>.ussClassName, TextField.ussClassName },
                "All three inherited built-in styling classes are restored");
            CollectionAssert.DoesNotContain(classes, "custom-class", "Custom classes are removed");
        }
    }

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
            var toggle = new Toggle { value = true, label = "Enabled", name = "my-toggle", tooltip = "my-tooltip", focusable = false, viewDataKey = "my-view-data" };
            toggle.AddToClassList("custom-class");
            toggle.style.color = new StyleColor(Color.red);
            toggle.userData = 42;

            // Act
            FiberTogglePoolHelper.ResetToggleForReuse(toggle);

            // Assert
            var actual = (toggle.value, toggle.label, toggle.userData, toggle.name, toggle.tooltip, toggle.focusable, toggle.viewDataKey);
            Assert.AreEqual((false, string.Empty, (object)null, string.Empty, string.Empty, true, (string)null), actual,
                "Reset returns every consumer-set field to its constructed default (focusable is TRUE for this widget type)");
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
