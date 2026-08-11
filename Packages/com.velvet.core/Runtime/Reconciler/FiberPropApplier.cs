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

        // Unlike ApplyEnabled, a dropped Focusable cannot coalesce to a constant: what an absent prop has to
        // restore is the element's own constructed value, which differs by
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
                s_focusableDefaults.Add(element, new Recorded<bool>(element.focusable));
            }
        }

        // A box rather than a nullable field: "never recorded" has to stay distinguishable from the
        // recorded value itself, and for a reference-typed member a nullable field cannot separate them.
        private sealed class Recorded<T>
        {
            public readonly T Value;

            public Recorded(T value) => Value = value;
        }

        private static readonly ConditionalWeakTable<VisualElement, Recorded<bool>> s_focusableDefaults = new();
        private static readonly ConditionalWeakTable<VisualElement, Recorded<int>> s_tabIndexDefaults = new();
        private static readonly ConditionalWeakTable<VisualElement, Recorded<bool>> s_delegatesFocusDefaults = new();

        // A record is the applier's claim on a member: while one stands, dropping the prop writes the
        // recorded value back, and for a TextField so does redeclaring a neighbour, since the bag's presence
        // is what admits the members this render left undeclared. Recording is idempotent, so the next
        // tenancy's first declared write leaves a record the previous tenancy took — and the restore then
        // puts that tenancy's reading over whatever this one wrote. Called from
        // FiberElementCleaner.ReturnToPool, which is the single gate every poolable type passes through.
        internal static void ForgetRecordedDefaults(VisualElement element)
        {
            s_focusableDefaults.Remove(element);
            s_tabIndexDefaults.Remove(element);
            s_delegatesFocusDefaults.Remove(element);
            if (element is TextField textField)
            {
                s_textFieldDefaults.Remove(textField);
            }
        }

        // Same shape and same reason as ApplyFocusable: what an absent prop restores is the element's own
        // constructed value, which differs by type, so the 0 / false these coalesced to were another
        // type's answer. Which value each type is built with is pinned by ConstructedFocusDefaultTests,
        // and that the drop reaches this branch at all by PropsDiffTests, which reconciles rather than
        // calling in here.
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
                s_tabIndexDefaults.Add(element, new Recorded<int>(element.tabIndex));
            }
        }

        internal static void RecordDelegatesFocusDefault(VisualElement element)
        {
            if (!s_delegatesFocusDefaults.TryGetValue(element, out _))
            {
                s_delegatesFocusDefaults.Add(element, new Recorded<bool>(element.delegatesFocus));
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

        // Same recorded-default shape, and the same reason, as ApplyFocusable, with one record per member
        // rather than one per element: a member no render has ever declared carries no record and is not
        // written at all, so a value a refCallback assigned survives a re-render that redeclares only its
        // neighbours. A member a render did declare and a later one dropped restores what this element was
        // constructed with. The type guard below does not narrow that to one answer — it admits any subclass
        // of TextField, which V.Custom<T> can name, and such a subclass may be built with a placeholder or a
        // length limit no constant here could name. TextFieldInputPropTests measures both.
        public static void ApplyTextField(VisualElement element, TextFieldSettings? settings)
        {
            if (element is not TextField tfEl)
            {
                return;
            }

            if (!s_textFieldDefaults.TryGetValue(tfEl, out var built))
            {
                if (!Declares(settings))
                {
                    return;
                }

                built = new TextFieldDefaults();
                s_textFieldDefaults.Add(tfEl, built);
            }

            ApplyPasswordFlag(tfEl, settings?.IsPassword, built);
            ApplyPlaceholder(tfEl, settings?.Placeholder, built);
            ApplyMaxLength(tfEl, settings?.MaxLength, built);
            ApplyReadOnlyFlag(tfEl, settings?.IsReadOnly, built);
            ApplyDelayedFlag(tfEl, settings?.IsDelayed, built);
        }

        private static void ApplyPasswordFlag(TextField field, bool? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.IsPassword ??= new Recorded<bool>(field.isPasswordField);
                field.isPasswordField = value;
            }
            else if (built.IsPassword != null)
            {
                field.isPasswordField = built.IsPassword.Value;
            }
        }

        private static void ApplyPlaceholder(TextField field, string? declared, TextFieldDefaults built)
        {
            if (declared != null)
            {
                built.Placeholder ??= new Recorded<string>(field.textEdition.placeholder);
                field.textEdition.placeholder = declared;
            }
            else if (built.Placeholder != null)
            {
                field.textEdition.placeholder = built.Placeholder.Value;
            }
        }

        private static void ApplyMaxLength(TextField field, int? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.MaxLength ??= new Recorded<int>(field.maxLength);
                field.maxLength = value;
            }
            else if (built.MaxLength != null)
            {
                field.maxLength = built.MaxLength.Value;
            }
        }

        private static void ApplyReadOnlyFlag(TextField field, bool? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.IsReadOnly ??= new Recorded<bool>(field.isReadOnly);
                field.isReadOnly = value;
            }
            else if (built.IsReadOnly != null)
            {
                field.isReadOnly = built.IsReadOnly.Value;
            }
        }

        private static void ApplyDelayedFlag(TextField field, bool? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.IsDelayed ??= new Recorded<bool>(field.isDelayed);
                WriteDelayed(field, value);
            }
            else if (built.IsDelayed != null)
            {
                WriteDelayed(field, built.IsDelayed.Value);
            }
        }

        // Ordering: an edit the field is still holding is committed before the flag comes off. The flag's
        // contract is that the value lags the typed text until Enter or blur, so clearing it first strands
        // that edit — displayed, never reported, and with nothing later to re-sync it, since a render
        // repeating the same FieldValue does not reach ApplyFieldValue at all.
        // The notifying setter is the point of the write, not an incidental way of making it: reporting
        // the commit is the half that "never reported" names, and SetValueWithoutNotify would leave it.
        // Every other prop-path write to a field is the silent one — FiberNodePatcher.RaiseCheckedSignal
        // names that policy and what it costs — so this is the exception, and nothing else here fails if
        // it stops notifying. DelayedFlagCommitReportTests measures the report on a real panel;
        // TextFieldInputPropTests measures the commit itself on both routes off the flag.
        private static void WriteDelayed(TextField field, bool value)
        {
            if (!value && field.isDelayed)
            {
                field.value = field.text;
            }

            field.isDelayed = value;
        }

        private static bool Declares(TextFieldSettings? settings)
            => settings != null
               && (settings.IsPassword.HasValue
                   || settings.Placeholder != null
                   || settings.MaxLength.HasValue
                   || settings.IsReadOnly.HasValue
                   || settings.IsDelayed.HasValue);

        private sealed class TextFieldDefaults
        {
            public Recorded<bool>? IsPassword;
            public Recorded<string>? Placeholder;
            public Recorded<int>? MaxLength;
            public Recorded<bool>? IsReadOnly;
            public Recorded<bool>? IsDelayed;
        }

        private static readonly ConditionalWeakTable<TextField, TextFieldDefaults> s_textFieldDefaults = new();

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
