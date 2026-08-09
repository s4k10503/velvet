using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        // Hiding writes the same `hidden` utility an author could write, so it goes through the class
        // projection rather than straight onto the class list — outside it, a `md:flex` payload and this prop
        // would both hold `display` with no ranking between them, and whichever the stylesheet declared later
        // would win. The important band is the layer that matches the prop's meaning: an explicit
        // Visible: false outranks every variant, and only an equally explicit `md:!flex` can overrule it.
        public static void ApplyVisible(VisualElement element, bool? visible)
        {
            if (visible == false)
            {
                StyleClassProjection.Add(element, FiberElementProps.HiddenClassName, s_hiddenPriority);
            }
            else
            {
                StyleClassProjection.Remove(element, FiberElementProps.HiddenClassName, s_hiddenPriority);
            }
        }

        private static readonly int s_hiddenPriority = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);

        // Unlike ApplyEnabled, a dropped Focusable cannot coalesce to a constant: what an absent prop has to restore is the element's own constructed value, which differs by
        // type — FiberElementPoolReset.ResetCommonState writes one answer and every widget helper but the Label
        // one overwrites it — and which no table can answer for a V.Custom<T> type. The element is asked instead
        // of a table: the create path never writes an absent Focusable (FiberElementFactory.ApplyProps guards on
        // HasValue), so the value standing here before the first declared write is that default. Recording it at
        // the first declared write is what makes that ordering hold; recording later would capture a declared
        // value as the default. Any other writer of a mounted element's focusable owes RecordFocusableDefault
        // first, for the same reason.
        public static void ApplyFocusable(VisualElement element, bool? focusable)
        {
            if (focusable.HasValue)
            {
                RecordFocusableDefault(element);
                element.focusable = focusable.Value;
                return;
            }

            // No record means no declared value was ever written, so the element still carries its own default.
            if (s_focusableDefaults.TryGetValue(element, out var recorded))
            {
                element.focusable = recorded.Value;
            }
        }

        // Callers writing focusable outside the prop path must run this before their write, or the value they
        // are about to install becomes what a Focusable prop first declared afterwards records as the
        // element's own. Idempotent: the first record for an element is the one that stands.
        internal static void RecordFocusableDefault(VisualElement element)
        {
            if (!s_focusableDefaults.TryGetValue(element, out _))
            {
                s_focusableDefaults.Add(element, new FocusableDefault(element.focusable));
            }
        }

        private sealed class FocusableDefault
        {
            public readonly bool Value;

            public FocusableDefault(bool value) => Value = value;
        }

        private sealed class TabIndexDefault
        {
            public readonly int Value;

            public TabIndexDefault(int value) => Value = value;
        }

        private sealed class DelegatesFocusDefault
        {
            public readonly bool Value;

            public DelegatesFocusDefault(bool value) => Value = value;
        }

        private static readonly ConditionalWeakTable<VisualElement, FocusableDefault> s_focusableDefaults = new();
        private static readonly ConditionalWeakTable<VisualElement, TabIndexDefault> s_tabIndexDefaults = new();
        private static readonly ConditionalWeakTable<VisualElement, DelegatesFocusDefault> s_delegatesFocusDefaults = new();

        // Same shape and same reason as ApplyFocusable: what an absent prop restores is the element's own
        // constructed value, which differs by type. A TextElement is built at tabIndex -1 and a BaseField
        // delegates focus, so the 0 / false these coalesced to were another type's answer — reachable with
        // no pool involved, by rendering the prop once and then not.
        public static void ApplyTabIndex(VisualElement element, int? tabIndex)
        {
            if (tabIndex.HasValue)
            {
                RecordTabIndexDefault(element);
                element.tabIndex = tabIndex.Value;
                return;
            }

            if (s_tabIndexDefaults.TryGetValue(element, out var recorded))
            {
                element.tabIndex = recorded.Value;
            }
        }

        public static void ApplyDelegatesFocus(VisualElement element, bool? delegatesFocus)
        {
            if (delegatesFocus.HasValue)
            {
                RecordDelegatesFocusDefault(element);
                element.delegatesFocus = delegatesFocus.Value;
                return;
            }

            if (s_delegatesFocusDefaults.TryGetValue(element, out var recorded))
            {
                element.delegatesFocus = recorded.Value;
            }
        }

        // Callers writing either outside the prop path owe this before their write, for the reason
        // RecordFocusableDefault gives. Idempotent: the first record for an element is the one that stands.
        internal static void RecordTabIndexDefault(VisualElement element)
        {
            if (!s_tabIndexDefaults.TryGetValue(element, out _))
            {
                s_tabIndexDefaults.Add(element, new TabIndexDefault(element.tabIndex));
            }
        }

        internal static void RecordDelegatesFocusDefault(VisualElement element)
        {
            if (!s_delegatesFocusDefaults.TryGetValue(element, out _))
            {
                s_delegatesFocusDefaults.Add(element, new DelegatesFocusDefault(element.delegatesFocus));
            }
        }

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
