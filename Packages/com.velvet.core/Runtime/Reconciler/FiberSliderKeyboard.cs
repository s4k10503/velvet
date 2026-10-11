#nullable enable
using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The arithmetic V.Slider and V.SliderInt differ in, so one keyboard and one prop applier serve both.
    // Min, Max and Clamp are spelled per type rather than through IComparable<T>, whose order puts NaN below
    // every number where Mathf.Min and Mathf.Max do not.
    internal interface ISliderNumber<T>
    {
        double ToDouble(T value);
        T FromDouble(double value);
        T Min(T a, T b);
        T Max(T a, T b);
        T Clamp(T value, T low, T high);
    }

    internal readonly struct FloatSliderNumber : ISliderNumber<float>
    {
        public double ToDouble(float value) => value;
        public float FromDouble(double value) => (float)value;
        public float Min(float a, float b) => Mathf.Min(a, b);
        public float Max(float a, float b) => Mathf.Max(a, b);
        public float Clamp(float value, float low, float high) => Mathf.Clamp(value, low, high);
    }

    // FromDouble is only handed what Snap returns: a whole number inside the range, either a grid line counted
    // from an integer low value in integer steps or the bound it was clamped to, so the cast neither truncates
    // nor overflows.
    internal readonly struct IntSliderNumber : ISliderNumber<int>
    {
        public double ToDouble(int value) => value;
        public int FromDouble(double value) => (int)value;
        public int Min(int a, int b) => Mathf.Min(a, b);
        public int Max(int a, int b) => Mathf.Max(a, b);
        public int Clamp(int value, int low, int high) => Mathf.Clamp(value, low, high);
    }

    // A V.Slider's or V.SliderInt's input is Radix's Slider: Home and End go to the low and high value, PageUp,
    // PageDown and the arrows step by `step` with no Submit first, and a value a key, a drag or a write to
    // the slider's value gives it lands on the step grid; the controlled `value:` is written as declared.
    // Which arrow steps back follows Radix's BACK_KEYS for the four slide directions; PageDown is always back. The key callbacks are registered TrickleDown and stop the event, so the slider's
    // own handlers never see the keys they take over; SliderDirectionPanelTests pins each family. A key aimed
    // at the slider's numeric input field is left to it.
    internal static class FiberSliderKeyboard
    {
        private const double DefaultStep = 1d;
        private const int SkipMultiplier = 10;

        private static readonly ConditionalWeakTable<VisualElement, StepBox> s_steps = new();

        private sealed class StepBox
        {
            public double Value;
        }

        public static Slider Create() =>
            Input<float, FloatSliderNumber>.Register(new Slider(), Slider.inputUssClassName);

        public static SliderInt CreateInt() =>
            Input<int, IntSliderNumber>.Register(new SliderInt(), SliderInt.inputUssClassName);

        public static void SetStep(VisualElement slider, double? step)
        {
            s_steps.Remove(slider);
            if (step is { } value)
            {
                s_steps.Add(slider, new StepBox { Value = value });
            }
        }

        public static void ForgetStep(VisualElement slider) => s_steps.Remove(slider);

        private static double StepOf(VisualElement slider) =>
            s_steps.TryGetValue(slider, out var box) ? box.Value : DefaultStep;

        private static class Input<T, TNumber>
            where T : struct, IComparable<T>
            where TNumber : struct, ISliderNumber<T>
        {
            private static readonly EventCallback<KeyDownEvent> s_onKeyDown = OnKeyDown;
            private static readonly EventCallback<NavigationMoveEvent> s_onNavigationMove = OnNavigationMove;
            private static readonly EventCallback<ChangeEvent<T>> s_onValueChanged = OnValueChanged;
            private static readonly EventCallback<KeyDownEvent> s_stopAtField = StopHomeEndAtField;

            public static TSlider Register<TSlider>(TSlider slider, string inputUssClassName)
                where TSlider : BaseSlider<T>
            {
                slider.RegisterCallback(s_onKeyDown, TrickleDown.TrickleDown);
                slider.RegisterCallback(s_onNavigationMove, TrickleDown.TrickleDown);
                slider.RegisterCallback(s_onValueChanged, TrickleDown.TrickleDown);
                slider.Q(className: inputUssClassName)?.RegisterCallback(s_stopAtField);
                return slider;
            }

            private static void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.currentTarget is not BaseSlider<T> slider)
                {
                    return;
                }

                if (IsInInputField(evt.target as VisualElement, slider))
                {
                    return;
                }

                var number = default(TNumber);
                switch (evt.keyCode)
                {
                    case KeyCode.Home:
                        slider.value = Snap(slider, number.ToDouble(slider.lowValue));
                        break;
                    case KeyCode.End:
                        slider.value = Snap(slider, number.ToDouble(slider.highValue));
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

            // The slider's own handler takes Home and End from any target, so a Home or End aimed at the field
            // is stopped on the way up. It sits on the slider's input container from Create rather than on the
            // field, which exists only once showInputField is set: SliderDirectionPanelTests pins the first press.
            private static void StopHomeEndAtField(KeyDownEvent evt)
            {
                if (evt.keyCode is KeyCode.Home or KeyCode.End
                    && evt.currentTarget is VisualElement container
                    && container.GetFirstAncestorOfType<BaseSlider<T>>() is { } slider
                    && IsInInputField(evt.target as VisualElement, slider))
                {
                    evt.StopPropagation();
                }
            }

            private static void OnNavigationMove(NavigationMoveEvent evt)
            {
                if (evt.currentTarget is not BaseSlider<T> slider || IsInInputField(evt.target as VisualElement, slider))
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
            // silently, and a fresh change from the value it held before reports the snapped one. The
            // replacement is on the grid, so it passes through here untouched; a snap back to the value held
            // before reports nothing.
            private static void OnValueChanged(ChangeEvent<T> evt)
            {
                if (evt.currentTarget is not BaseSlider<T> slider || evt.target != slider)
                {
                    return;
                }

                var number = default(TNumber);
                var snapped = Snap(slider, number.ToDouble(evt.newValue));
                if (number.ToDouble(snapped) == number.ToDouble(evt.newValue))
                {
                    return;
                }

                evt.StopImmediatePropagation();
                slider.SetValueWithoutNotify(snapped);
                if (number.ToDouble(snapped) == number.ToDouble(evt.previousValue))
                {
                    return;
                }

                using var replacement = ChangeEvent<T>.GetPooled(evt.previousValue, snapped);
                replacement.target = slider;
                slider.SendEvent(replacement);
            }

            private static bool IsInInputField(VisualElement? target, BaseSlider<T> slider)
            {
                var field = slider.Q(className: BaseSlider<T>.textFieldClassName);
                return field != null && (target == field || field.Contains(target));
            }

            // Radix's updateValues: onto the step grid counted from the low value, then into the range.
            private static T Snap(BaseSlider<T> slider, double value)
            {
                var number = default(TNumber);
                var low = number.ToDouble(slider.lowValue);
                var high = number.ToDouble(slider.highValue);
                var step = StepOf(slider);
                var decimals = DecimalCount((float)step);
                var snapped = RoundTo(JsRound((value - low) / step) * step + low, decimals);
                return number.FromDouble(Math.Min(Math.Max(snapped, low), high));
            }

            // Radix's getNextStepValue: a value on the grid moves by `multiplier` steps, one off it goes to the
            // next grid line in the direction of travel.
            private static double Stepped(BaseSlider<T> slider, int direction, int multiplier)
            {
                var number = default(TNumber);
                var low = number.ToDouble(slider.lowValue);
                var step = StepOf(slider);
                var decimals = DecimalCount((float)step);
                var value = number.ToDouble(slider.value);
                var stepsFromMin = (value - low) / step;
                var nearest = JsRound(stepsFromMin);
                var aligned = RoundTo(nearest * step + low, decimals) == RoundTo(value, decimals);
                var next = aligned ? nearest + multiplier * direction
                    : direction == 1 ? Math.Ceiling(stepsFromMin) : Math.Floor(stepsFromMin);
                return RoundTo(next * step + low, decimals);
            }
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
