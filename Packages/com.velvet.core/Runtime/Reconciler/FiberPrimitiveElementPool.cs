using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The shared class-list restoration and UIToolkit-side reset live in
    // FiberElementPoolReset.ResetClassListAndCommon; what is left here is what only a Button has.
    // PooledElementSurfaceResetTests is what says the pair between them accounts for the whole surface.
    // button.clicked event handlers are registered by FiberEventBindingManager with a
    // closure-based unregister (actions.Add(() => button.clicked -= handler)); these are
    // released by FiberElementCleaner.CleanupElementResources before the Button reaches the pool.
    internal static class FiberButtonPoolHelper
    {
        public static void ResetButtonForReuse(Button button)
        {
            if (button == null) return;

            // CleanupDescendants resource-cleans a removed button's children but, by design, does NOT
            // pool-return or detach them (descendants ride along on the bulk parent.RemoveAt). For a
            // childless container that subtree is just GC'd; but a poolable Button carries its children
            // INTO the pool. RentButton would then hand back a button that still has its old children,
            // and CreateElement's child reconcile (which assumes an empty baseline) appends the new
            // children on top — the button's contents visibly duplicate on reuse. Detach them here so a
            // recycled button is empty, matching a freshly constructed instance (and the childless-only
            // invariant already enforced on the rollback path in FiberElementCleaner.ReturnRolledBackOrphan).
            //
            // Only Button and Label may do this, and the split is not about which DSL factory takes a
            // `children:` argument — V.Custom<T> takes one for any T. It is about what the child container
            // already holds: Toggle, Slider and TextField each construct a sub-element into the very
            // container FiberNodePatcher.GetChildContainer hands children to, so Clear() there would delete
            // the control's own structure rather than a previous tenant's content. They reach the same
            // outcome by exclusion instead (FiberElementPoolReset.DetachForeignChildren).
            // PoolableWidgetChildBaselineTests pins which types those are.
            if (button.childCount > 0) button.Clear();

            FiberElementPoolReset.ResetClassListAndCommon(button, TextElement.ussClassName, Button.ussClassName);
            FiberElementPoolReset.ResetTextElementState(button);
            button.text = string.Empty;
            button.iconImage = default;
            // Replacing the manipulator is the only way to drop handlers a consumer subscribed to the
            // Clickable itself: `clicked` is an event, so nothing outside can clear it without holding every
            // delegate that was added. Leaving the instance in place, as this did, carries a consumer's own
            // click action into whatever mounts next. This is the one place the pool return buys correctness
            // with allocation — a fresh manipulator, and the callback re-registration the swap performs —
            // against the stance the class-list overloads in FiberElementPoolReset are shaped by.
            button.clickable = new Clickable((Action)null);
            // The common reset scrubs focusable to the plain-VisualElement default (false), but a Button's
            // OWN constructor default is focusable — without restoring it, a recycled button silently drops
            // out of Tab/gamepad navigation, diverging from the freshly-constructed instance this reset
            // promises to match.
            button.focusable = true;
        }
    }

    // Same split between the shared reset and the widget's own as FiberButtonPoolHelper.
    internal static class FiberLabelPoolHelper
    {
        public static void ResetLabelForReuse(Label label)
        {
            if (label == null) return;

            // Same detach rule, and the same duplicate-on-reuse failure, as FiberButtonPoolHelper. A Label
            // reaches it through V.Custom<Label>(children: ...), which mounts an exact pooled Label and
            // expands the children into it.
            if (label.childCount > 0) label.Clear();

            FiberElementPoolReset.ResetClassListAndCommon(label, TextElement.ussClassName, Label.ussClassName);
            FiberElementPoolReset.ResetTextElementState(label);
            label.text = string.Empty;
            // The common reset writes the plain-VisualElement 0; a Label's own constructor leaves it out of
            // the tab ring, and a recycled one that keeps 0 joins a focus order it was never in.
            label.tabIndex = -1;
        }
    }

    // Same split as FiberButtonPoolHelper.
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

        // A slider that carries its numeric input field cannot be made to give it up. The teardown behind
        // showInputField only runs while that field is on a panel, and every pool return detaches first, so
        // writing the flag false here would strand the sub-element AND consume the one write that removes
        // it — leaving no state from which any later write can. Such a slider is dropped rather than pooled;
        // VNodePool asks before returning one. SliderPoolAdmissionTests pins both directions.
        public static bool CanReuse(Slider slider)
        {
            if (slider == null) return false;

            for (var i = 0; i < slider.childCount; i++)
            {
                var input = slider.ElementAt(i);
                if (!input.ClassListContains(BaseField<float>.inputUssClassName)) continue;
                for (var j = 0; j < input.childCount; j++)
                {
                    if (input.ElementAt(j).ClassListContains(Slider.textFieldClassName)) return false;
                }
            }
            return true;
        }

        public static void ResetSliderForReuse(Slider slider)
        {
            if (slider == null) return;

            FiberElementPoolReset.DetachForeignChildren(
                slider, BaseField<float>.inputUssClassName, BaseField<float>.labelUssClassName);
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
            slider.inverted = false;
            slider.fill = false;
            slider.showMixedValue = false;
            slider.generateVisualContent = null;
            // See FiberTogglePoolHelper for the focus delegation; the picking mode is the same shape — a
            // composite field's constructor takes its root out of the pick path so the input beneath it
            // receives the pointer, and the common reset writes the plain-VisualElement default back.
            slider.delegatesFocus = true;
            slider.pickingMode = PickingMode.Ignore;
            // See FiberButtonPoolHelper: the common reset's focusable=false is the plain-VisualElement
            // default; a Slider's own constructor default is focusable, and dropping it would remove a
            // recycled slider from Tab/gamepad navigation.
            slider.focusable = true;
        }
    }

    // Same split as FiberButtonPoolHelper, with a security contract stated below.
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
        private const char DefaultMaskChar = '*';

        public static void ResetTextFieldForReuse(TextField textField)
        {
            if (textField == null) return;

            FiberElementPoolReset.DetachForeignChildren(
                textField, BaseField<string>.inputUssClassName, BaseField<string>.labelUssClassName);
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
            textField.showMixedValue = false;
            textField.emojiFallbackSupport = true;
            textField.isDelayed = false;
            textField.isReadOnly = false;
            textField.autoCorrection = false;
            textField.hideMobileInput = false;
            textField.hideSoftKeyboard = false;
            textField.keyboardType = TouchScreenKeyboardType.Default;
            textField.maskChar = DefaultMaskChar;
            textField.selectAllOnFocus = true;
            textField.selectAllOnMouseUp = true;
            textField.doubleClickSelectsWord = true;
            textField.tripleClickSelectsLine = true;
            // The scroller visibility only stores while multiline is on, and a field reaches the pool with
            // multiline either way — a consumer that turned it on, set the visibility and turned it off
            // again leaves a stored setting no write can reach. So multiline is turned on for the write and
            // back off after, and neither half is redundant: PooledScrollerVisibilityResetTests fails on
            // the first being dropped, PooledElementSurfaceResetTests on the last.
            textField.multiline = true;
            textField.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            textField.multiline = false;
            textField.generateVisualContent = null;
            // Same pair, and the same reason, as FiberSliderPoolHelper.
            textField.delegatesFocus = true;
            textField.pickingMode = PickingMode.Ignore;
            // See FiberButtonPoolHelper: restore the type's own constructor default after the common
            // reset's focusable=false, or a recycled field cannot be tabbed into.
            textField.focusable = true;
        }
    }

    // Same split as FiberButtonPoolHelper.
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

            FiberElementPoolReset.DetachForeignChildren(
                toggle, BaseField<bool>.inputUssClassName, BaseField<bool>.labelUssClassName);
            FiberElementPoolReset.ResetClassListAndCommon(toggle, BaseField<bool>.ussClassName, Toggle.ussClassName);
            toggle.SetValueWithoutNotify(false);
            toggle.label = string.Empty;
            toggle.text = string.Empty;
            toggle.showMixedValue = false;
            toggle.toggleOnLabelClick = true;
            toggle.generateVisualContent = null;
            // A composite field's constructor hands focus to its input; the common reset writes the
            // plain-VisualElement false, which would leave a recycled one taking focus on its own root and
            // dropping the keystrokes the input was meant to receive.
            toggle.delegatesFocus = true;
            // See FiberButtonPoolHelper: restore the type's own constructor default after the common
            // reset's focusable=false, or a recycled toggle drops out of Tab/gamepad navigation.
            toggle.focusable = true;
        }
    }
}
