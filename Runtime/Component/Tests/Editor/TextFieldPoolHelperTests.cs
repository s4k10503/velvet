using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
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
}
