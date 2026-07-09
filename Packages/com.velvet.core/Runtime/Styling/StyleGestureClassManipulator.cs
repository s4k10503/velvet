using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Manipulator that toggles CSS classes dynamically in response to pointer / focus events,
    // driving the whileHover / whileTap / whileFocus gesture classes.
    // Mirrors StyleVariantManipulator's lifecycle: element-local interaction detection (the bubbling
    // PointerOut worldBound check, the hover/active/focus edge bookkeeping, the single-pointer assumption)
    // is owned by ElementLocalVariantSignals so this manipulator and StyleVariantManipulator cannot drift on
    // that detection logic; this class only maps each signal edge to its gesture class array.
    // whileFocus uses the element's own Focus signal (backed by FocusEvent / BlurEvent — only focusable
    // elements — buttons, fields — ever trigger it). whileTap maps to the Active signal (pointer-down /
    // pointer-up / pointer-cancel / release-outside-bounds).
    internal sealed class StyleGestureClassManipulator : Manipulator
    {
        private string[] _hoverClasses;
        private string[] _tapClasses;
        private string[] _focusClasses;
        private bool _isHovered;
        private bool _isTapped;
        private bool _isFocused;

        // Owns the element-local signal detection (pointer/focus edges, worldBound bubbling); this
        // manipulator keeps only the per-state gesture-class bookkeeping below.
        private ElementLocalVariantSignals _signals = null!;

        public StyleGestureClassManipulator(string[] hoverClasses, string[] tapClasses, string[] focusClasses)
        {
            _hoverClasses = hoverClasses ?? Array.Empty<string>();
            _tapClasses = tapClasses ?? Array.Empty<string>();
            _focusClasses = focusClasses ?? Array.Empty<string>();
        }

        public void UpdateClasses(string[] hoverClasses, string[] tapClasses, string[] focusClasses)
        {
            var oldHover = _hoverClasses;
            var oldTap = _tapClasses;
            var oldFocus = _focusClasses;
            _hoverClasses = hoverClasses ?? Array.Empty<string>();
            _tapClasses = tapClasses ?? Array.Empty<string>();
            _focusClasses = focusClasses ?? Array.Empty<string>();

            if (target == null)
            {
                return;
            }

            if (_isHovered)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldHover);
                StyleAnimationClassUtils.AddClasses(target, _hoverClasses);
            }

            if (_isTapped)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldTap);
                StyleAnimationClassUtils.AddClasses(target, _tapClasses);
            }

            if (_isFocused)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldFocus);
                StyleAnimationClassUtils.AddClasses(target, _focusClasses);
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            _signals ??= new ElementLocalVariantSignals(OnSignal);
            // No checked: signal here — gesture classes have no while-checked concept, so the ChangeEvent
            // registration stays off (matching the original hand-rolled wiring's event set).
            _signals.Hook(target, seedChecked: false, registerChecked: false);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            if (_isHovered)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _hoverClasses);
            }

            if (_isTapped)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _tapClasses);
            }

            if (_isFocused)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _focusClasses);
            }

            _isHovered = false;
            _isTapped = false;
            _isFocused = false;

            _signals?.Unhook();
        }

        // Maps a detected element-local signal edge to its gesture class array, deduping on the per-state
        // bookkeeping (the source does not dedup) so a repeated edge does not churn the class list. Active
        // maps to whileTap (pointer-down/-up/-cancel/release-outside-bounds) and Focus maps to whileFocus
        // (plain FocusEvent/BlurEvent, not the focus-visible distinction); FocusVisible/Checked are not used
        // by gesture classes and fall through the switch untouched.
        private void OnSignal(VariantSignal signal, bool on)
        {
            switch (signal)
            {
                case VariantSignal.Hover:
                    if (on != _isHovered)
                    {
                        _isHovered = on;
                        ToggleClasses(_hoverClasses, on);
                    }
                    break;
                case VariantSignal.Active:
                    if (on != _isTapped)
                    {
                        _isTapped = on;
                        ToggleClasses(_tapClasses, on);
                    }
                    break;
                case VariantSignal.Focus:
                    if (on != _isFocused)
                    {
                        _isFocused = on;
                        ToggleClasses(_focusClasses, on);
                    }
                    break;
            }
        }

        private void ToggleClasses(string[] classes, bool on)
        {
            if (on)
            {
                StyleAnimationClassUtils.AddClasses(target, classes);
            }
            else
            {
                StyleAnimationClassUtils.RemoveClasses(target, classes);
            }
        }
    }
}
