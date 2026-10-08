#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Home sends a V.Slider to lowValue and End to highValue whatever its direction and inverted flag, the
    // keys' meaning in WAI-ARIA's slider pattern and Radix's Slider. The callback is registered TrickleDown and
    // stops the event, so the slider's own key handler never sees Home or End; SliderDirectionPanelTests pins
    // both and the input-field exception. A key aimed at the slider's numeric input field is left to it.
    internal static class FiberSliderKeyboard
    {
        private static readonly EventCallback<KeyDownEvent> s_onKeyDown = OnKeyDown;

        public static Slider Create()
        {
            var slider = new Slider();
            slider.RegisterCallback(s_onKeyDown, TrickleDown.TrickleDown);
            return slider;
        }

        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.currentTarget is not Slider slider || IsInInputField(evt.target as VisualElement, slider))
            {
                return;
            }

            if (evt.keyCode == KeyCode.Home)
            {
                slider.value = slider.lowValue;
            }
            else if (evt.keyCode == KeyCode.End)
            {
                slider.value = slider.highValue;
            }
            else
            {
                return;
            }

            evt.StopImmediatePropagation();
        }

        private static bool IsInInputField(VisualElement? target, Slider slider)
        {
            var field = slider.Q(className: Slider.textFieldClassName);
            return field != null && target != null && field.Contains(target);
        }
    }
}
