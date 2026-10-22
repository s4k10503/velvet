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
    // Each class array is applied as a variant payload at its state's priority — whileHoverClass as hover:,
    // whileTapClass as active:, whileFocusClass as focus: — so it outranks the element's base utilities as
    // Tailwind's variant rule outranks the base one, and the base takes the property back on release.
    internal sealed class StyleGestureClassManipulator : Manipulator, IVariantSettleTarget
    {
        // After every rule the className declares at the state's rank, so a gesture class wins a tie with the
        // element's own hover: / active: / focus: rule, as Framer's whileHover overrides the element's own style.
        private static readonly long HoverPriority = StyleLayerPriority.AfterEveryRule(StyleLayerPriority.Hover);
        private static readonly long ActivePriority = StyleLayerPriority.AfterEveryRule(StyleLayerPriority.Active);
        private static readonly long FocusPriority = StyleLayerPriority.AfterEveryRule(StyleLayerPriority.Focus);

        private readonly ReconcilerContext? _ctx;
        private string[] _hoverClasses;
        private string[] _tapClasses;
        private string[] _focusClasses;
        private bool _isHovered;
        private bool _isTapped;
        private bool _isFocused;

        // Owns the element-local signal detection (pointer/focus edges, worldBound bubbling); this
        // manipulator keeps only the per-state gesture-class bookkeeping below.
        private ElementLocalVariantSignals _signals = null!;

        public StyleGestureClassManipulator(string[] hoverClasses, string[] tapClasses, string[] focusClasses,
            ReconcilerContext? ctx = null)
        {
            _ctx = ctx;
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
                Toggle(oldHover, false, HoverPriority);
                Toggle(_hoverClasses, true, HoverPriority);
            }

            if (_isTapped)
            {
                Toggle(oldTap, false, ActivePriority);
                Toggle(_tapClasses, true, ActivePriority);
            }

            if (_isFocused)
            {
                Toggle(oldFocus, false, FocusPriority);
                Toggle(_focusClasses, true, FocusPriority);
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            _signals ??= new ElementLocalVariantSignals(OnSignal);
            // No checked: signal here — gesture classes have no while-checked concept, so the ChangeEvent
            // registration stays off (matching the original hand-rolled wiring's event set).
            _signals.Hook(target, seedChecked: false, registerChecked: false);
        }

        // Forwards a drag session's synthetic release to the shared signal source (see
        // ElementLocalVariantSignals.SettleRelease); the per-state dedup below makes it idempotent.
        public void SettleRelease() => _signals?.SettleRelease();

        // Forwards a snap-back's synthetic focus loss (see ElementLocalVariantSignals.SettleFocusLoss).
        public void SettleFocusLoss() => _signals?.SettleFocusLoss();

        // Required by the settle surface; the signal source drops the edge for a consumer that registered
        // without the checked path, as RegisterCallbacksOnTarget does.
        public void SettleChecked(bool value) => _signals?.SettleChecked(value);

        protected override void UnregisterCallbacksFromTarget()
        {
            if (_isHovered)
            {
                Toggle(_hoverClasses, false, HoverPriority);
            }

            if (_isTapped)
            {
                Toggle(_tapClasses, false, ActivePriority);
            }

            if (_isFocused)
            {
                Toggle(_focusClasses, false, FocusPriority);
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
                        Toggle(_hoverClasses, on, HoverPriority);
                    }
                    break;
                case VariantSignal.Active:
                    if (on != _isTapped)
                    {
                        _isTapped = on;
                        Toggle(_tapClasses, on, ActivePriority);
                    }
                    break;
                case VariantSignal.Focus:
                    if (on != _isFocused)
                    {
                        _isFocused = on;
                        Toggle(_focusClasses, on, FocusPriority);
                    }
                    break;
            }
        }

        private void Toggle(string[] classes, bool on, long priority)
            => StyleVariantPayload.Apply(target, classes, on, priority, _ctx, this);
    }
}
