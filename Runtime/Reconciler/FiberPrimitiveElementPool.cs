using UnityEngine.UIElements;

namespace Velvet
{
    // Resets a Button to a clean state so it can be reused from VNodePool.
    // Delegates the shared class-list restoration + UIToolkit-side reset to
    // FiberElementPoolReset.ResetClassListAndCommon and only handles Button-specific
    // state (text / iconImage clear) here.
    // button.clicked event handlers are registered by FiberEventBindingManager with a
    // closure-based unregister (actions.Add(() => button.clicked -= handler)); these are
    // released by FiberElementCleaner.CleanupElementResources before the Button reaches the pool.
    // The Button.clickable instance itself is retained by Unity and not touched here.
    internal static class FiberButtonPoolHelper
    {
        public static void ResetButtonForReuse(Button button)
        {
            if (button == null) return;

            // Button is the ONLY poolable primitive whose DSL allows children (icon + label, e.g.
            // V.Button(children: ...)). Label / Toggle / Slider / TextField hardcode empty children.
            // CleanupDescendants resource-cleans a removed button's children but, by design, does NOT
            // pool-return or detach them (descendants ride along on the bulk parent.RemoveAt). For a
            // childless container that subtree is just GC'd; but a poolable Button carries its children
            // INTO the pool. RentButton would then hand back a button that still has its old children,
            // and CreateElement's child reconcile (which assumes an empty baseline) appends the new
            // children on top — the button's contents visibly duplicate on reuse. Detach them here so a
            // recycled button is empty, matching a freshly constructed instance (and the childless-only
            // invariant already enforced on the rollback path in FiberElementCleaner.ReturnRolledBackOrphan).
            if (button.childCount > 0) button.Clear();

            FiberElementPoolReset.ResetClassListAndCommon(button, TextElement.ussClassName, Button.ussClassName);
            button.text = string.Empty;
            button.iconImage = default;
        }
    }

    // Resets a Label to a clean state so it can be reused from VNodePool.
    // Delegates the shared class-list restoration + UIToolkit-side reset to
    // FiberElementPoolReset.ResetClassListAndCommon and only handles Label-specific
    // state (text clear) here.
    internal static class FiberLabelPoolHelper
    {
        public static void ResetLabelForReuse(Label label)
        {
            if (label == null) return;

            FiberElementPoolReset.ResetClassListAndCommon(label, TextElement.ussClassName, Label.ussClassName);
            label.text = string.Empty;
        }
    }

    // Resets a Slider to a clean state so it can be reused from VNodePool.
    // Delegates the shared class-list restoration + UIToolkit-side reset to
    // FiberElementPoolReset.ResetClassListAndCommon and handles Slider-specific state
    // (value / lowValue / highValue / label) here.
    // Slider inherits from BaseSlider<float> which inherits from BaseField<float>.
    // The constructor chain adds three USS classes in order:
    //   BaseField.ussClassName = "unity-base-field" (BaseField.cs:354)
    //   BaseSlider.ussClassName = "unity-base-slider" (BaseSlider.cs:442)
    //   Slider.ussClassName = "unity-slider" (Slider.cs:171)
    // All three must be restored after ClearClassList.
    // Sub-elements (dragger, tracker, labelElement) retain their own USS classes
    // through the pool cycle for the same reason described in FiberTogglePoolHelper.
    // Default range (lowValue=0f, highValue=10f) matches Unity's Slider() default
    // constructor (Slider.cs). SetValueWithoutNotify(0f) avoids firing ChangeEvent.
    internal static class FiberSliderPoolHelper
    {
        private const float DefaultLowValue = 0f;
        private const float DefaultHighValue = 10f;

        public static void ResetSliderForReuse(Slider slider)
        {
            if (slider == null) return;

            FiberElementPoolReset.ResetClassListAndCommon(
                slider,
                BaseField<float>.ussClassName,
                BaseSlider<float>.ussClassName,
                Slider.ussClassName);

            slider.lowValue = DefaultLowValue;
            slider.highValue = DefaultHighValue;
            slider.SetValueWithoutNotify(DefaultLowValue);
            slider.label = string.Empty;
            slider.direction = SliderDirection.Horizontal;
            slider.pageSize = 0f;
        }
    }

    // Resets a TextField to a clean state so it can be reused from VNodePool.
    // Delegates the shared class-list restoration + UIToolkit-side reset to
    // FiberElementPoolReset.ResetClassListAndCommon and handles TextField-specific
    // state (security-critical input clearing) here.
    // TextField inherits from TextInputBaseField<string> which inherits from
    // BaseField<string>. The constructor chain adds three USS classes in order:
    //   BaseField.ussClassName = "unity-base-field" (BaseField.cs:354)
    //   TextInputBaseField.ussClassName = "unity-base-text-field" (TextInputFieldBase.cs:342)
    //   TextField.ussClassName = "unity-text-field" (TextField.cs:179)
    // All three must be restored after ClearClassList.
    // <strong>Security contract:</strong> TextField is a security-critical
    // widget because pooled instances may have held PII (passwords, email addresses, player names).
    // ResetTextFieldForReuse must guarantee that the next consumer cannot observe
    // stale text or selection state in any frame after pool rent. Concretely:
    //   SetValueWithoutNotify(string.Empty): clears the stored value without firing
    //   ChangeEvent<string>. Pool return must not propagate stale text to listeners.
    //   textEdition.isPassword = false: prevents a password field's masking state from
    //   ghosting into a non-password consumer (or vice versa, exposing keystrokes that the next
    //   consumer expected to be masked).
    //   maxLength = -1: restores the Unity default (unlimited), in case the previous
    //   consumer constrained input length.
    //   textSelection.SelectNone(): clears both cursorIndex and
    //   selectIndex via the public ITextSelection contract, preventing visual cursor
    //   ghosting and undo stack residue from leaking into the next consumer.
    //   label = string.Empty: same rationale as Toggle / Slider.
    // Inner TextElement sub-elements (placeholder, multiline container, etc.) retain their
    // own USS classes through the pool cycle for the same reason described in
    // FiberTogglePoolHelper: Velvet's ApplyClassNames overwrites root-facing classes
    // on every mount, and structural sub-elements stay consistent with Unity's built-in styling.
    internal static class FiberTextFieldPoolHelper
    {
        private const int DefaultMaxLength = -1;

        public static void ResetTextFieldForReuse(TextField textField)
        {
            if (textField == null) return;

            FiberElementPoolReset.ResetClassListAndCommon(
                textField,
                BaseField<string>.ussClassName,
                TextInputBaseField<string>.ussClassName,
                TextField.ussClassName);

            textField.SetValueWithoutNotify(string.Empty);
            textField.textEdition.isPassword = false;
            textField.maxLength = DefaultMaxLength;
            textField.textSelection.SelectNone();
            textField.label = string.Empty;
        }
    }

    // Resets a Toggle to a clean state so it can be reused from VNodePool.
    // Delegates the shared class-list restoration + UIToolkit-side reset to
    // FiberElementPoolReset.ResetClassListAndCommon and handles Toggle-specific
    // state (value reset via SetValueWithoutNotify) here.
    // Toggle inherits from BaseBoolField (which has no USS class of its own) which inherits
    // from BaseField<bool>. The constructor chain adds BaseField.ussClassName
    // ("unity-base-field", BaseField.cs:354) and Toggle.ussClassName
    // ("unity-toggle", Toggle.cs:156); both must be restored after ClearClassList.
    // Sub-elements (checkmark, labelElement) retain their own USS classes through the
    // pool cycle because FiberElementPoolReset.ResetClassListAndCommon only touches the
    // root element. Velvet's ApplyClassNames overwrites the user-facing root classes on every
    // mount, so structural sub-elements stay consistent with Unity's built-in styling.
    // SetValueWithoutNotify(false) resets the value without firing ChangeEvent<bool>,
    // avoiding spurious notifications during pool return.
    internal static class FiberTogglePoolHelper
    {
        public static void ResetToggleForReuse(Toggle toggle)
        {
            if (toggle == null) return;

            FiberElementPoolReset.ResetClassListAndCommon(toggle, BaseField<bool>.ussClassName, Toggle.ussClassName);
            toggle.SetValueWithoutNotify(false);
            toggle.label = string.Empty;
        }
    }
}
