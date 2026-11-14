using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Pools of recyclable VisualElement leaves. Main thread only (not thread-safe).
    internal static class VNodePool
    {
        // Sized to absorb peak reconcile churn (typically ~30 mounts/unmounts in deep reflows).
        private const int MaxLabelPoolSize = 32;
        private const int MaxButtonPoolSize = 32;
        private const int MaxTogglePoolSize = 32;
        private const int MaxSliderPoolSize = 32;
        private const int MaxTextFieldPoolSize = 32;

        // A pool of recyclable VisualElement leaves. The cap check and the reset-only-when-pooling discipline
        // (a full pool drops the element WITHOUT resetting, matching the per-type behavior these collapsed)
        // live here once; each leaf kind supplies only its factory and its type-specific reset helper.
        private sealed class ElementPool<T> where T : VisualElement
        {
            private readonly Stack<T> _pool = new();
            private readonly Func<T> _factory;
            private readonly Action<T> _reset;
            private readonly int _max;

            public ElementPool(Func<T> factory, Action<T> reset, int max)
            {
                _factory = factory;
                _reset = reset;
                _max = max;
            }

            public int Count => _pool.Count;

            public T Rent() => _pool.Count > 0 ? _pool.Pop() : _factory();

            public void Return(T element)
            {
                if (element == null) return;
                if (_pool.Count >= _max) return;
                _reset(element);
                _pool.Push(element);
            }

            public void Clear() => _pool.Clear();
        }

        #region Label pool

        // Pooled labels have been reset via FiberLabelPoolHelper.ResetLabelForReuse.
        private static readonly ElementPool<Label> s_labelPool =
            new(() => new SkewPickLabel(string.Empty), FiberLabelPoolHelper.ResetLabelForReuse, MaxLabelPoolSize);

        // The marker _preflight.uss resets margin and padding under. It is added here, where every Label
        // Velvet creates is rented, and not to the pool's reset, so a Label UI Toolkit builds inside one of
        // its own controls never carries it.
        internal const string VelvetLabelClassName = "velvet-label";

        public static Label RentLabel(string text)
        {
            var label = s_labelPool.Rent();
            label.AddToClassList(VelvetLabelClassName);
            label.text = text ?? string.Empty;
            return label;
        }

        // Called by FiberElementCleaner after the label has been removed from the DOM
        // hierarchy and Velvet-managed resources have been released.
        public static void ReturnLabel(Label label) => s_labelPool.Return(label);

        #endregion

        #region Button pool

        // Pooled buttons have been reset via FiberButtonPoolHelper.ResetButtonForReuse.
        private static readonly ElementPool<Button> s_buttonPool =
            new(() => new SkewPickButton(), FiberButtonPoolHelper.ResetButtonForReuse, MaxButtonPoolSize);

        // The marker _preflight.uss gives a Button Velvet creates its white-space under, added at the rent for
        // the reason VelvetLabelClassName is.
        internal const string VelvetButtonClassName = "velvet-button";

        public static Button RentButton()
        {
            var button = s_buttonPool.Rent();
            button.AddToClassList(VelvetButtonClassName);
            return button;
        }

        // Called by FiberElementCleaner after the button has been removed from the DOM
        // hierarchy and Velvet-managed resources (event bindings, gesture manipulators) have been released.
        public static void ReturnButton(Button button) => s_buttonPool.Return(button);

        #endregion

        #region Toggle pool

        // Pooled toggles have been reset via FiberTogglePoolHelper.ResetToggleForReuse.
        private static readonly ElementPool<Toggle> s_togglePool =
            new(() => new Toggle(), FiberTogglePoolHelper.ResetToggleForReuse, MaxTogglePoolSize);

        public static Toggle RentToggle() => s_togglePool.Rent();

        public static void ReturnToggle(Toggle toggle) => s_togglePool.Return(toggle);

        #endregion

        #region Slider pool

        // Pooled sliders have been reset via FiberSliderPoolHelper.ResetSliderForReuse.
        private static readonly ElementPool<Slider> s_sliderPool =
            new(FiberSliderKeyboard.Create, FiberSliderPoolHelper.ResetSliderForReuse, MaxSliderPoolSize);

        public static Slider RentSlider() => s_sliderPool.Rent();

        public static void ReturnSlider(Slider slider)
        {
            if (!FiberSliderPoolHelper.CanReuse(slider)) return;
            s_sliderPool.Return(slider);
        }

        #endregion

        #region SliderInt pool

        private static readonly ElementPool<SliderInt> s_sliderIntPool =
            new(FiberSliderKeyboard.CreateInt, FiberSliderPoolHelper.ResetSliderIntForReuse, MaxSliderPoolSize);

        public static SliderInt RentSliderInt() => s_sliderIntPool.Rent();

        public static void ReturnSliderInt(SliderInt slider)
        {
            if (!FiberSliderPoolHelper.CanReuse(slider)) return;
            s_sliderIntPool.Return(slider);
        }

        #endregion

        #region TextField pool

        // Pooled TextFields have been reset via FiberTextFieldPoolHelper.ResetTextFieldForReuse,
        // guaranteeing no PII (password / email / player name) ghosting from a prior consumer.
        private static readonly ElementPool<TextField> s_textFieldPool =
            new(() =>
            {
                var field = new TextField();
                FieldLineBox.Attach(field);
                return field;
            }, FiberTextFieldPoolHelper.ResetTextFieldForReuse, MaxTextFieldPoolSize);

        public static TextField RentTextField() => s_textFieldPool.Rent();

        public static void ReturnTextField(TextField textField) => s_textFieldPool.Return(textField);

        #endregion

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            s_labelPool.Clear();
            s_buttonPool.Clear();
            s_togglePool.Clear();
            s_sliderPool.Clear();
            s_sliderIntPool.Clear();
            s_textFieldPool.Clear();
        }
#endif
    }
}
