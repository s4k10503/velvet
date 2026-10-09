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
        {
            element.SetEnabled(enabled ?? true);
            // :disabled matches the element and everything beneath it, with no class change on any of them.
            StyleAnimateDriver.NotifySubtreeStyleChanged(element);
        }

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

        private static readonly long s_hiddenPriority = StyleLayerPriority.ImportantOf(StyleLayerPriority.Base);

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
        // recorded value back, and for a TextField or a Slider so does redeclaring a neighbour, since the
        // bag's presence is what admits the members this render left undeclared. Recording is idempotent, so the next
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
                // MUTANT_SURVIVES(equivalent, line removed): a callback left behind writes only what a later
                // tenancy's record declares, which that tenancy's own registration writes as well; the line
                // keeps one from staying on a pooled element.
                textField.UnregisterCallback(s_reassertTouchKeyboard, TrickleDown.TrickleDown);
                s_textFieldDefaults.Remove(textField);
                ForgetShownText(textField);
            }
            else if (element is Slider slider)
            {
                s_sliderDefaults.Remove(slider);
                FiberSliderKeyboard.ForgetStep(slider);
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
            }
            else
            {
                FiberElementFactory.ApplyFieldValue(element, value);
            }

            if (element is TextField field)
            {
                RecordShownText(field);
            }
        }

        // The range keeps the rule it had while it was the record's only content: both bounds written, a null
        // one as 0 or 10, whenever either bound's declaration changed and at no other time. Writing them on
        // every call would rewrite a range a refCallback set each time a render changes only the direction
        // or the flag. Those two take ApplyTextField's recorded-default shape, for the reason given there.
        // SliderDirectionPropTests measures both.
        //
        // A declared value that arrives with a range change is written in between: the range widens to hold
        // both ranges, the value is placed inside the new one without a notification, and only then do the
        // bounds narrow, so the value neither falls to the old range nor reports a clamp the render never asked
        // for. SliderDirectionPropTests measures both.
        public static void ApplySlider(
            VisualElement element, SliderSettings? previous, SliderSettings? settings, object? declaredValue = null)
        {
            if (element is not Slider sliderEl)
            {
                return;
            }

            FiberSliderKeyboard.SetStep(sliderEl, settings?.Step);
            if (previous?.LowValue != settings?.LowValue || previous?.HighValue != settings?.HighValue)
            {
                var low = Resolve(settings?.LowValue, 0f);
                var high = Resolve(settings?.HighValue, 10f);
                if (declaredValue is float value)
                {
                    sliderEl.lowValue = UnityEngine.Mathf.Min(sliderEl.lowValue, low);
                    sliderEl.highValue = UnityEngine.Mathf.Max(sliderEl.highValue, high);
                    sliderEl.SetValueWithoutNotify(UnityEngine.Mathf.Clamp(value, low, high));
                }

                sliderEl.lowValue = low;
                sliderEl.highValue = high;
            }

            if (!s_sliderDefaults.TryGetValue(sliderEl, out var built))
            {
                if (settings?.Direction == null && settings?.Inverted == null)
                {
                    return;
                }

                built = new SliderDefaults();
                s_sliderDefaults.Add(sliderEl, built);
            }

            ApplyDirection(sliderEl, settings?.Direction, built);
            ApplyInverted(sliderEl, settings?.Inverted, built);
        }

        private static void ApplyDirection(Slider slider, SliderDirection? declared, SliderDefaults built)
        {
            if (declared is { } value)
            {
                built.Direction ??= new Recorded<SliderDirection>(slider.direction);
                slider.direction = value;
            }
            else if (built.Direction != null)
            {
                slider.direction = built.Direction.Value;
            }
        }

        private static void ApplyInverted(Slider slider, bool? declared, SliderDefaults built)
        {
            if (declared is { } value)
            {
                built.Inverted ??= new Recorded<bool>(slider.inverted);
                slider.inverted = value;
            }
            else if (built.Inverted != null)
            {
                slider.inverted = built.Inverted.Value;
            }
        }

        private sealed class SliderDefaults
        {
            public Recorded<SliderDirection>? Direction;
            public Recorded<bool>? Inverted;
        }

        private static readonly ConditionalWeakTable<Slider, SliderDefaults> s_sliderDefaults = new();

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
            // Ordering: after the delayed flag, so a render releasing that flag commits the typed text while
            // its line breaks are still there, before multiline coming off in the same render reaches it.
            // TextFieldMultilineKeyboardPropTests holds the break across that render, and
            // TextFieldMultilineEngineTests pins the engine dropping it as multiline comes off.
            ApplyMultilineFlag(tfEl, settings?.Multiline, built);
            ApplyKeyboardType(tfEl, settings?.KeyboardType, built);
            ApplyAutoCorrectionFlag(tfEl, settings?.AutoCorrection, built);
            SyncTouchKeyboardReassert(tfEl, settings, built);
        }

        private static void ApplyMultilineFlag(TextField field, bool? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.Multiline ??= new Recorded<bool>(field.multiline);
                WriteMultiline(field, value);
            }
            else if (built.Multiline != null)
            {
                WriteMultiline(field, built.Multiline.Value);
            }
        }

        // Turning multiline on puts the field's value back on screen, which on a delayed field holding an
        // uncommitted edit replaces the typed text with the value it has not received yet, so that edit is
        // carried across the write through the silent setter. Turning it off carries nothing across the
        // write, since the engine leaves the edit there without its line breaks, but it does hold the edit:
        // without them it can equal the value's single-line display. A write leaving the flag as it was holds
        // nothing, so a render that only repeats the declaration does not hold typing Velvet never carried.
        // With no edit pending, what the write left on screen becomes the record; left unrecorded, the text
        // multiline coming off leaves reads as typed to the next multiline or limit write, which then holds it
        // on screen for a blur to commit. TextFieldMultilineKeyboardPropTests pins each of these.
        private static void WriteMultiline(TextField field, bool value)
        {
            var edit = HasUncommittedEdit(field) ? field.text : null;
            var turns = field.multiline != value;
            field.multiline = value;
            if (edit == null)
            {
                RecordShownText(field);
                return;
            }

            if (!turns)
            {
                return;
            }

            if (value)
            {
                ((INotifyValueChanged<string>)(TextElement)field.textEdition).SetValueWithoutNotify(edit);
            }

            HoldEdit(field);
        }

        // The engine writes keyboardType back to Default and autoCorrection back to false when the field
        // hands focus from its input back to itself (Enter, Shift+Enter in multiline, Escape), and a render
        // repeating the same settings writes nothing, so a declaration would stay lost from then on. While
        // one stands, it is written again as focus comes back in, registered for the trickle-down pass so the
        // field sees the event before the input inside it. TextFieldTouchKeyboardReassertTests pins the
        // engine's write and the rewrite for Enter.
        // Registered once and unregistered when the last declaration goes or the element returns to the
        // pool (ForgetRecordedDefaults); the callback reads the declarations off the record rather than
        // capturing them, so it is one static delegate for every field.
        private static void SyncTouchKeyboardReassert(
            TextField field, TextFieldSettings? settings, TextFieldDefaults built)
        {
            built.DeclaredKeyboardType = settings?.KeyboardType;
            built.DeclaredAutoCorrection = settings?.AutoCorrection;
            var reasserts = built.DeclaredKeyboardType.HasValue || built.DeclaredAutoCorrection.HasValue;
            if (reasserts == built.ReassertsOnFocusIn)
            {
                return;
            }

            if (reasserts)
            {
                field.RegisterCallback(s_reassertTouchKeyboard, TrickleDown.TrickleDown);
            }
            else
            {
                // MUTANT_SURVIVES(equivalent, line removed): the declarations above are already null, so a
                // callback left registered writes nothing; the line keeps it from running for nothing.
                field.UnregisterCallback(s_reassertTouchKeyboard, TrickleDown.TrickleDown);
            }

            built.ReassertsOnFocusIn = reasserts;
        }

        private static void ReassertTouchKeyboard(FocusInEvent evt)
        {
            if (evt.currentTarget is not TextField field)
            {
                return;
            }

            if (!s_textFieldDefaults.TryGetValue(field, out var built))
            {
                return;
            }

            if (built.DeclaredKeyboardType is { } keyboardType)
            {
                field.keyboardType = keyboardType;
            }

            if (built.DeclaredAutoCorrection is { } autoCorrection)
            {
                field.autoCorrection = autoCorrection;
            }
        }

        private static readonly EventCallback<FocusInEvent> s_reassertTouchKeyboard = ReassertTouchKeyboard;

        private static void ApplyKeyboardType(
            TextField field, UnityEngine.TouchScreenKeyboardType? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.KeyboardType ??= new Recorded<UnityEngine.TouchScreenKeyboardType>(field.keyboardType);
                field.keyboardType = value;
            }
            else if (built.KeyboardType != null)
            {
                field.keyboardType = built.KeyboardType.Value;
            }
        }

        private static void ApplyAutoCorrectionFlag(TextField field, bool? declared, TextFieldDefaults built)
        {
            if (declared is { } value)
            {
                built.AutoCorrection ??= new Recorded<bool>(field.autoCorrection);
                field.autoCorrection = value;
            }
            else if (built.AutoCorrection != null)
            {
                field.autoCorrection = built.AutoCorrection.Value;
            }
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
                WriteMaxLength(field, value);
            }
            else if (built.MaxLength != null)
            {
                WriteMaxLength(field, built.MaxLength.Value);
            }
        }

        // Ordering: the edit is cut to the new limit and written silently BEFORE the limit, then written
        // silently again after it. Narrowing the limit culls the shown text through a notifying setter, so
        // an edit still longer than the limit would be reported to onValueChanged; cut beforehand, the cull
        // finds nothing to change. The limit write then re-shows the committed value over the edit, which
        // is what the second write undoes. The value is not touched, so the commit stays with Enter or
        // blur, and the record keeps the text Velvet last wrote so the restored edit still reads as one.
        // Both writes are skipped on an unchanged limit, which the setter ignores. With no edit, the
        // limit write's own result is what Velvet left on screen and becomes the record.
        // DelayedMaxLengthEditReportTests pins that nothing is reported; TextFieldInputPropTests pins that
        // the edit survives and the no-edit cases.
        private static void WriteMaxLength(TextField field, int limit)
        {
            if (field.maxLength == limit)
            {
                return;
            }

            TextElement? held = null;
            string? edit = null;
            if (HasUncommittedEdit(field) && field.textEdition is TextElement shown)
            {
                held = shown;
                edit = CutToLimit(field.text, limit);
                ((INotifyValueChanged<string>)held).SetValueWithoutNotify(edit);
            }

            field.maxLength = limit;
            if (held == null)
            {
                RecordShownText(field);
                return;
            }

            ((INotifyValueChanged<string>)held).SetValueWithoutNotify(edit!);
            HoldEdit(field);
        }

        // A delayed field holds the user's typing in the shown text while the value lags. Shown text is an
        // edit when it differs from the record — the text Velvet's own writes and the engine's commits last
        // left on screen — and, unless ShownText holds it, both from that record as multiline coming off
        // since would leave it and from the field's display of its own value.
        // The record is taken by each write of Velvet's own text — ApplyFieldValue, WriteMaxLength and
        // WriteMultiline when no edit is pending, and the baseline FiberElementFactory.ApplyProps takes as
        // the element is created — and by a commit through ShownText's callback, since an edit that was
        // committed and then changed again is an edit against the committed text. ForgetRecordedDefaults
        // forgets the record and its callback on every removal, so a recycled field carries neither.
        // The display is asked because code outside Velvet can write the value silently, which moves the
        // shown text and not the record, where typing moves the text and not the value. It is not asked
        // alone: where the limit cuts the value after a line break, the text multiline coming off leaves
        // matches no form of it. Nor is it asked while ShownText holds an edit: a limit change or a turn of
        // multiline over a pending edit holds it, since the cut to a narrower limit, or the line breaks
        // multiline coming off drops, can leave it equal to the value's display.
        internal static bool HasUncommittedEdit(TextField field)
        {
            if (!field.isDelayed || !s_shownText.TryGetValue(field, out var left))
            {
                return false;
            }

            return !left.Shows(field)
                   && (left.HoldsEditOver(field) || (!left.ShowsWithoutBreaks(field) && !ShowsItsValue(field)));
        }

        // The two forms an engine write of the value leaves: the value cut to the limit, which a limit write
        // shows with its line breaks, and on a single-line field a value write's form, breaks dropped and
        // then cut. TextFieldMultilineEngineTests pins both.
        private static bool ShowsItsValue(TextField field)
        {
            var value = field.value ?? string.Empty;
            var text = field.text;
            return text == CutToLimit(value, field.maxLength)
                   || (!field.multiline && text == CutToLimit(value.Replace("\n", string.Empty), field.maxLength));
        }

        private static string CutToLimit(string text, int limit)
        {
            if (limit < 0)
            {
                return text;
            }

            // MUTANT_SURVIVES(equivalent, boundary): at a length equal to the limit Substring(0, limit) returns the text itself.
            return text.Length > limit ? text.Substring(0, limit) : text;
        }

        internal static void RecordShownText(TextField field)
        {
            if (!s_shownText.TryGetValue(field, out var left))
            {
                left = new ShownText(field);
                s_shownText.Add(field, left);
                field.RegisterValueChangedCallback(left.OnChange);
            }

            left.Take();
        }

        // Called after a write that turned multiline or changed the limit over a pending edit.
        private static void HoldEdit(TextField field)
        {
            if (s_shownText.TryGetValue(field, out var left))
            {
                left.Hold();
            }
        }

        internal static void ForgetShownText(TextField field)
        {
            if (s_shownText.TryGetValue(field, out var left))
            {
                field.UnregisterValueChangedCallback(left.OnChange);
                s_shownText.Remove(field);
            }
        }

        private sealed class ShownText
        {
            private readonly TextField _field;
            private string? _text;
            private bool _multiline;
            private bool _holding;
            private string? _heldOver;

            public ShownText(TextField field) => _field = field;

            public void Take()
            {
                _text = _field.text;
                _multiline = _field.multiline;
                _holding = false;
            }

            // A record ends the hold, so a write of Velvet's own text or a commit does. It does not apply while
            // the value differs from the one the edit was held over, or while the shown text equals the value.
            // Typing on does not end it, since what the user types over that value is still theirs. Code
            // outside Velvet writing the value away and back silently takes no record, so the hold applies
            // again once the value is back.
            public void Hold()
            {
                _holding = true;
                _heldOver = _field.value;
            }

            public bool Shows(TextField field) => field.text == _text;

            // Code outside Velvet turning multiline off drops the line breaks from what the field shows, so
            // with no edit it shows the record without them; TextFieldMultilineEngineTests pins that write.
            public bool ShowsWithoutBreaks(TextField field)
                => _multiline && !field.multiline && field.text == _text?.Replace("\n", string.Empty);

            public bool HoldsEditOver(TextField field)
                => _holding && field.value == _heldOver && field.text != field.value;

            // Only the field's own event is a commit of the value; TextFieldInputPropTests pins that an
            // event from the inner element does not move the record.
            public void OnChange(ChangeEvent<string> evt)
            {
                if (evt.target == _field)
                {
                    Take();
                }
            }
        }

        private static readonly ConditionalWeakTable<TextField, ShownText> s_shownText = new();

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
                   || settings.IsDelayed.HasValue
                   || settings.Multiline.HasValue
                   || settings.KeyboardType.HasValue
                   || settings.AutoCorrection.HasValue);

        private sealed class TextFieldDefaults
        {
            public Recorded<bool>? IsPassword;
            public Recorded<string>? Placeholder;
            public Recorded<int>? MaxLength;
            public Recorded<bool>? IsReadOnly;
            public Recorded<bool>? IsDelayed;
            public Recorded<bool>? Multiline;
            public Recorded<UnityEngine.TouchScreenKeyboardType>? KeyboardType;
            public Recorded<bool>? AutoCorrection;

            // What the last render declared, which the focus-in rewrite reads; the records above hold what a
            // drop restores instead.
            public UnityEngine.TouchScreenKeyboardType? DeclaredKeyboardType;
            public bool? DeclaredAutoCorrection;
            public bool ReassertsOnFocusIn;
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
