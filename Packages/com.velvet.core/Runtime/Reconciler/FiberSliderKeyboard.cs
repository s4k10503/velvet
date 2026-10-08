#nullable enable
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A V.Slider's input is Radix's Slider: Home and End go to the low and high value, PageUp, PageDown and the
    // arrows step by `step` with no Submit first, and a value a key, a drag or a write to Slider.value gives the
    // slider lands on the step grid; the controlled `value:` is written as declared.
    // Which arrow steps back follows Radix's BACK_KEYS for the four slide directions; PageDown is always back. The key callbacks are registered TrickleDown and stop the event, so the slider's
    // own handlers never see the keys they take over; SliderDirectionPanelTests pins each family. A key aimed
    // at the slider's numeric input field is left to it.
    internal static class FiberSliderKeyboard
    {
        private const float DefaultStep = 1f;
        private const int SkipMultiplier = 10;

        private static readonly EventCallback<KeyDownEvent> s_onKeyDown = OnKeyDown;
        private static readonly EventCallback<NavigationMoveEvent> s_onNavigationMove = OnNavigationMove;
        private static readonly EventCallback<ChangeEvent<float>> s_onValueChanged = OnValueChanged;
        private static readonly EventCallback<KeyDownEvent> s_stopAtField = StopHomeEndAtField;
        private static readonly ConditionalWeakTable<Slider, StepBox> s_steps = new();

        private sealed class StepBox
        {
            public float Value;
        }

        public static Slider Create()
        {
            var slider = new Slider();
            slider.RegisterCallback(s_onKeyDown, TrickleDown.TrickleDown);
            slider.RegisterCallback(s_onNavigationMove, TrickleDown.TrickleDown);
            slider.RegisterCallback(s_onValueChanged, TrickleDown.TrickleDown);
            return slider;
        }

        public static void SetStep(Slider slider, float? step)
        {
            s_steps.Remove(slider);
            if (step is { } value)
            {
                s_steps.Add(slider, new StepBox { Value = value });
            }
        }

        public static void ForgetStep(Slider slider) => s_steps.Remove(slider);

        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.currentTarget is not Slider slider)
            {
                return;
            }

            if (IsInInputField(evt.target as VisualElement, slider))
            {
                if (evt.keyCode is KeyCode.Home or KeyCode.End)
                {
                    slider.Q(className: Slider.textFieldClassName).RegisterCallback(s_stopAtField);
                }

                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Home:
                    slider.value = Snap(slider, slider.lowValue);
                    break;
                case KeyCode.End:
                    slider.value = Snap(slider, slider.highValue);
                    break;
                case KeyCode.PageUp:
                    slider.value = Snap(slider, Stepped(slider, 1, SkipMultiplier));
                    break;
                case KeyCode.PageDown:
                    slider.value = Snap(slider, Stepped(slider, -1, SkipMultiplier));
                    break;
                default:
                    return;
            }

            evt.StopImmediatePropagation();
        }

        // A Home or End the field's text editor leaves unhandled would bubble to the slider's own handler, which
        // puts them at the swapped ends; this stops it at the field.
        private static void StopHomeEndAtField(KeyDownEvent evt)
        {
            if (evt.keyCode is KeyCode.Home or KeyCode.End)
            {
                evt.StopPropagation();
            }
        }

        private static void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.currentTarget is not Slider slider || IsInInputField(evt.target as VisualElement, slider))
            {
                return;
            }

            var back = IsBackArrow(evt.direction, slider.direction == SliderDirection.Vertical, slider.inverted);
            if (back == null)
            {
                return;
            }

            slider.value = Snap(slider, Stepped(slider, back.Value ? -1 : 1, evt.shiftKey ? SkipMultiplier : 1));
            evt.StopImmediatePropagation();
            slider.focusController?.IgnoreEvent(evt);
        }

        // A change that is off the step grid is swallowed and replaced: the slider takes the snapped value
        // silently, and a fresh change from the value it held before reports the snapped one. The replacement
        // is on the grid, so it passes through here untouched; a snap back to the value held before reports
        // nothing.
        private static void OnValueChanged(ChangeEvent<float> evt)
        {
            if (evt.currentTarget is not Slider slider || evt.target != slider)
            {
                return;
            }

            var snapped = Snap(slider, evt.newValue);
            if (snapped == evt.newValue)
            {
                return;
            }

            evt.StopImmediatePropagation();
            slider.SetValueWithoutNotify(snapped);
            if (snapped == evt.previousValue)
            {
                return;
            }

            using var replacement = ChangeEvent<float>.GetPooled(evt.previousValue, snapped);
            replacement.target = slider;
            slider.SendEvent(replacement);
        }

        // Radix's BACK_KEYS: Down is back and Left is back, except that a horizontal inverted slider swaps Left
        // for Right and a vertical inverted one swaps Down for Up. Null for a move that is not an arrow.
        private static bool? IsBackArrow(NavigationMoveEvent.Direction direction, bool vertical, bool inverted)
        {
            var fromRight = !vertical && inverted;
            var fromTop = vertical && inverted;
            return direction switch
            {
                NavigationMoveEvent.Direction.Left => !fromRight,
                NavigationMoveEvent.Direction.Right => fromRight,
                NavigationMoveEvent.Direction.Down => !fromTop,
                NavigationMoveEvent.Direction.Up => fromTop,
                _ => null,
            };
        }

        private static bool IsInInputField(VisualElement? target, Slider slider)
        {
            var field = slider.Q(className: Slider.textFieldClassName);
            return field != null && target != null && (target == field || field.Contains(target));
        }

        private static double StepOf(Slider slider) =>
            s_steps.TryGetValue(slider, out var box) ? box.Value : DefaultStep;

        // Radix's updateValues: onto the step grid counted from the low value, then into the range.
        private static float Snap(Slider slider, double value)
        {
            var step = StepOf(slider);
            var decimals = DecimalCount((float)step);
            var snapped = RoundTo(JsRound((value - slider.lowValue) / step) * step + slider.lowValue, decimals);
            return (float)Math.Min(Math.Max(snapped, slider.lowValue), slider.highValue);
        }

        // Radix's getNextStepValue: a value on the grid moves by `multiplier` steps, one off it goes to the
        // next grid line in the direction of travel.
        private static double Stepped(Slider slider, int direction, int multiplier)
        {
            var step = StepOf(slider);
            var decimals = DecimalCount((float)step);
            double value = slider.value;
            var stepsFromMin = (value - slider.lowValue) / step;
            var nearest = JsRound(stepsFromMin);
            var aligned = RoundTo(nearest * step + slider.lowValue, decimals) == RoundTo(value, decimals);
            var next = aligned ? nearest + multiplier * direction
                : direction > 0 ? Math.Ceiling(stepsFromMin) : Math.Floor(stepsFromMin);
            return RoundTo(next * step + slider.lowValue, decimals);
        }

        // JavaScript's Math.round sends a half toward positive infinity, which Math.Round does not.
        private static double JsRound(double value) => Math.Floor(value + 0.5);

        private static double RoundTo(double value, int decimals)
        {
            var scale = Math.Pow(10, decimals);
            return JsRound(value * scale) / scale;
        }

        // Radix's getDecimalCount, over the float's shortest round-trip text.
        private static int DecimalCount(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            var exponentAt = text.IndexOf('E');
            var coefficient = exponentAt < 0 ? text : text.Substring(0, exponentAt);
            var dotAt = coefficient.IndexOf('.');
            var fraction = dotAt < 0 ? 0 : coefficient.Length - dotAt - 1;
            return exponentAt < 0
                ? fraction
                : Math.Max(0, fraction - int.Parse(text.Substring(exponentAt + 1), CultureInfo.InvariantCulture));
        }
    }
}
