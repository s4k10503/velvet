using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Single source of truth for property application.
    // Both FiberElementFactory (initial creation) and Reconciler (diff updates) route through this class.
    // Each method also handles resetting to null / default values.
    internal static class FiberPropApplier
    {
        public static void ApplyText(VisualElement element, string? text)
        {
            var value = text ?? string.Empty;
            switch (element)
            {
                case Label label: label.text = value; break;
                case Button button: button.text = value; break;
                case TextField tf: tf.label = value; break;
                case Toggle toggle: toggle.label = value; break;
                case RadioButton rb: rb.label = value; break;
                case RadioButtonGroup rbg: rbg.label = value; break;
                case IntegerField intField: intField.label = value; break;
            }
        }

        public static void ApplyTooltip(VisualElement element, string? tooltip)
            => element.tooltip = tooltip ?? string.Empty;

        public static void ApplyEnabled(VisualElement element, bool? enabled)
            => element.SetEnabled(enabled ?? true);

        public static void ApplyVisible(VisualElement element, bool? visible)
        {
            if (visible.HasValue)
            {
                element.EnableInClassList(FiberElementProps.HiddenClassName, !visible.Value);
            }
            else
            {
                element.RemoveFromClassList(FiberElementProps.HiddenClassName);
            }
        }

        public static void ApplyFocusable(VisualElement element, bool? focusable)
            => element.focusable = focusable ?? true;

        public static void ApplyFieldValue(VisualElement element, object? value)
        {
            // A controlled field reflects its declared value, so clearing the value prop to null resets the
            // element to its type default (mirroring ApplyText's null -> empty coalescing) instead of stranding
            // the prior value. On the initial mount a null FieldValue is skipped by the caller, so this clear
            // path is reached only when a re-render diffs a concrete value down to null.
            if (value == null)
            {
                FiberElementFactory.ClearFieldValue(element);
                return;
            }

            FiberElementFactory.ApplyFieldValue(element, value);
        }

        public static void ApplySlider(VisualElement element, SliderSettings? settings)
        {
            if (element is not Slider sliderEl)
            {
                return;
            }

            sliderEl.lowValue = Resolve(settings?.LowValue, 0f);
            sliderEl.highValue = Resolve(settings?.HighValue, 10f);
        }

        public static void ApplyScrollView(VisualElement element, ScrollViewSettings? settings)
        {
            if (element is not ScrollView svEl)
            {
                return;
            }

            svEl.verticalScrollerVisibility = Resolve(settings?.VerticalScrollerVisibility, ScrollerVisibility.Auto);
            svEl.horizontalScrollerVisibility = Resolve(settings?.HorizontalScrollerVisibility, ScrollerVisibility.Auto);
            svEl.touchScrollBehavior = Resolve(settings?.TouchScrollBehavior, ScrollView.TouchScrollBehavior.Clamped);
        }

        public static void ApplyTextField(VisualElement element, TextFieldSettings? settings)
        {
            if (element is not TextField tfEl)
            {
                return;
            }

            tfEl.isPasswordField = settings?.IsPassword ?? false;
        }

        // Applies choices to DropdownField / RadioButtonGroup. A null Choices prop (or no settings at
        // all) resets the widget to an empty choice list instead of stranding a prior render's options,
        // mirroring ApplyFieldValue's null-clears-to-default contract.
        public static void ApplyChoices(VisualElement element, ChoicesSettings? settings)
        {
            var choices = settings?.Choices ?? new List<string>();
            switch (element)
            {
                case DropdownField dd:
                    dd.choices = choices;
                    break;
                case RadioButtonGroup rbg:
                    rbg.choices = choices;
                    break;
            }
        }

        private static T Resolve<T>(T? nullable, T defaultValue) where T : struct
            => nullable ?? defaultValue;
    }
}
