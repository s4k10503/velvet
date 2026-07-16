#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element-local interaction signals a variant manipulator reacts to. FocusVisible is the CSS
    // :focus-visible distinction (keyboard/programmatic focus, not pointer focus); Checked is the element's
    // own toggle state.
    internal enum VariantSignal
    {
        Hover,
        Focus,
        FocusVisible,
        Active,
        Checked,
    }

    // The synthetic-settle surface every element-local variant consumer implements, so the reconciler
    // sweeps (drag release, focus-loss fallback) enumerate ONE shape instead of per-type calls.
    internal interface IVariantSettleTarget
    {
        void SettleRelease();
        void SettleFocusLoss();
    }

    // Enumerates every registered element-local variant consumer for one element through the shared
    // settle surface, so the reconciler-side sweeps cannot drift on WHICH registries participate.
    internal static class VariantSettleSweep
    {
        public static void ForEach(VisualElement element, ReconcilerContext ctx, Action<IVariantSettleTarget> action)
        {
            if (ctx.GestureManipulators.TryGetValue(element, out var gesture))
            {
                action(gesture);
            }
            if (ctx.VariantManipulators.TryGetValue(element, out var variant))
            {
                action(variant);
            }
            if (ctx.StackedVariantManipulators.Count > 0)
            {
                foreach (var kv in ctx.StackedVariantManipulators)
                {
                    if (kv.Key.target == element)
                    {
                        action(kv.Value);
                    }
                }
            }
        }
    }

    // Detects element-local interaction state on a target and reports each on/off TRANSITION EDGE to a
    // callback. It owns ONLY the detection — the bubbling-PointerOut worldBound check, the
    // focus-visible-vs-pointer-focus heuristic, and the own-target checked filter — so every consumer
    // (StyleVariantManipulator's payload toggling, the stacked-variant inner gate) shares one
    // implementation. The source does NOT dedup: a consumer guards on its own per-state bookkeeping (as the
    // originals already did) and decides what an edge means (apply a payload, or open an inner gate).
    //
    // The callback is captured once at construction; the target and the checked-registration choice are
    // captured per Hook so one instance can be reused across hook/unhook cycles (re-pointed at a new target)
    // without reallocating. Unhook reads the captured choice, so the register/unregister pair cannot drift.
    internal sealed class ElementLocalVariantSignals
    {
        private readonly Action<VariantSignal, bool> _emit;
        private VisualElement? _target;    // non-null only while hooked
        private bool _registerChecked;    // captured in Hook so Unhook stays symmetric

        // True when a PointerDown on this element was the immediate cause of the next FocusEvent — used to
        // suppress focus-visible for pointer focus. A one-shot flag, reset at Hook and consumed by the focus
        // it suppresses.
        private bool _pointerFocus;

        public ElementLocalVariantSignals(Action<VariantSignal, bool> emit) => _emit = emit;

        // Registers the detection callbacks on target. When registerChecked is false the ChangeEvent path is
        // skipped entirely (a consumer that does not support a checked signal — e.g. the stacked inner gate —
        // keeps the original registration footprint). When seedChecked is true and the target is an
        // already-checked Toggle, the initial Checked edge is emitted here (ChangeEvent fires only on change,
        // so a mounted-true value must be read at hook time).
        public void Hook(VisualElement target, bool seedChecked, bool registerChecked)
        {
            _target = target;
            _registerChecked = registerChecked;
            _pointerFocus = false;

            target.RegisterCallback<PointerOverEvent>(OnPointerOver);
            target.RegisterCallback<PointerOutEvent>(OnPointerOut);
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<FocusEvent>(OnFocus);
            target.RegisterCallback<BlurEvent>(OnBlur);

            if (registerChecked)
            {
                target.RegisterCallback<ChangeEvent<bool>>(OnCheckedChange);
                if (seedChecked && target is Toggle toggle && toggle.value)
                {
                    _emit(VariantSignal.Checked, true);
                }
            }
        }

        public void Unhook()
        {
            if (_target == null)
            {
                return;
            }

            _target.UnregisterCallback<PointerOverEvent>(OnPointerOver);
            _target.UnregisterCallback<PointerOutEvent>(OnPointerOut);
            _target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            _target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            _target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            _target.UnregisterCallback<FocusEvent>(OnFocus);
            _target.UnregisterCallback<BlurEvent>(OnBlur);
            if (_registerChecked)
            {
                _target.UnregisterCallback<ChangeEvent<bool>>(OnCheckedChange);
            }

            _target = null;
            _pointerFocus = false;
        }

        private void OnPointerOver(PointerOverEvent evt) => _emit(VariantSignal.Hover, true);

        private void OnPointerOut(PointerOutEvent evt)
        {
            // PointerOut bubbles, so it also fires when the pointer crosses onto a descendant. Treat it as a
            // real leave only once the pointer is outside this element's bounds; while inside, hover/active
            // persist (the pointer is over a descendant). A null target (an out delivered after Unhook on a
            // re-entrant flush) is treated as a leave, never dereferenced.
            if (_target != null && _target.worldBound.Contains(evt.position))
            {
                return;
            }
            _emit(VariantSignal.Hover, false);
            // Releasing outside the element ends the active state too (no PointerUp arrives).
            _emit(VariantSignal.Active, false);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            // A pointer-down on this element is what causes a pointer focus: remember it so the imminent
            // FocusEvent is not treated as focus-visible, and drop focus-visible if already lit (mouse
            // interaction removes the keyboard focus ring).
            _pointerFocus = true;
            _emit(VariantSignal.FocusVisible, false);
            _emit(VariantSignal.Active, true);
        }

        private void OnPointerUp(PointerUpEvent evt) => ReleasePress();

        private void OnPointerCancel(PointerCancelEvent evt) => ReleasePress();

        // Observes a synthetic release: a drag session that captured the pointer swallows the real
        // PointerUp (StopImmediatePropagation before the bubble phase), so the Active edge these
        // callbacks would have produced never arrives — without this, whileTap / active: stick on after
        // every completed drag. Consumers' own per-state bookkeeping dedups a redundant call.
        public void SettleRelease() => ReleasePress();

        // The one release body all three release paths share, so a change to release semantics cannot
        // drift between a real pointer-up, a cancel, and a drag's synthetic settle.
        private void ReleasePress()
        {
            _pointerFocus = false;
            _emit(VariantSignal.Active, false);
        }

        // Observes a synthetic focus loss: a containment snap-back reverts a landing whose queued focus
        // events can interleave such that the reverted element never receives a terminating Blur — its
        // focus / focus-visible payloads then stick lit on an unfocused element. Consumers' own
        // per-state dedup makes a redundant call a no-op.
        public void SettleFocusLoss()
        {
            _pointerFocus = false;
            _emit(VariantSignal.Focus, false);
            _emit(VariantSignal.FocusVisible, false);
        }

        private void OnFocus(FocusEvent evt)
        {
            _emit(VariantSignal.Focus, true);
            // focus-visible lights up only when the focus was NOT driven by a pointer-down on this element
            // (keyboard navigation or a programmatic Focus()), mirroring CSS :focus-visible.
            if (!_pointerFocus)
            {
                _emit(VariantSignal.FocusVisible, true);
            }
            _pointerFocus = false;
        }

        private void OnBlur(BlurEvent evt)
        {
            _emit(VariantSignal.Focus, false);
            _emit(VariantSignal.FocusVisible, false);
        }

        private void OnCheckedChange(ChangeEvent<bool> evt)
        {
            // Element-local: only the target's OWN checked state drives checked:. A ChangeEvent bubbling up
            // from a descendant control is ignored, mirroring CSS :checked on the input itself.
            // Limitation: this tracks user-driven changes and the attach-time initial read. A purely
            // programmatic change via SetValueWithoutNotify (how a fully-controlled FieldValue prop is
            // applied) dispatches no ChangeEvent, so checked: does not re-sync for that path — UI Toolkit
            // exposes no signal for it. User clicks DO fire ChangeEvent before the controlled re-render, so
            // the common controlled-toggle case still works.
            if (!ReferenceEquals(evt.target, _target))
            {
                return;
            }
            _emit(VariantSignal.Checked, evt.newValue);
        }
    }

    // Tracks the panel root's width and reports each geometry change to a callback. Owns the
    // RegisterCallback/UnregisterCallback pair for the root's GeometryChangedEvent in one place, so the
    // responsive (sm:/md:/...) breakpoint evaluation shared by the conditional and stacked variant
    // manipulators cannot let its hook/unhook drift apart. The callback is captured once at construction; the
    // root is captured per Hook so one instance is reused across panel attach/detach without reallocating.
    internal sealed class ResponsiveWidthSource
    {
        private readonly Action _onGeometryChanged;
        private VisualElement? _root;    // non-null only while hooked

        public ResponsiveWidthSource(Action onGeometryChanged) => _onGeometryChanged = onGeometryChanged;

        // The tracked root's resolved width, or 0 when unhooked.
        public float Width => _root?.resolvedStyle.width ?? 0f;

        public void Hook(VisualElement? root)
        {
            if (_root == root)
            {
                return;
            }

            Unhook();
            _root = root;
            _root?.RegisterCallback<GeometryChangedEvent>(OnGeometry);
        }

        public void Unhook()
        {
            if (_root != null)
            {
                _root.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                _root = null;
            }
        }

        private void OnGeometry(GeometryChangedEvent evt) => _onGeometryChanged();
    }

    // The relational (group-/peer-) signals a binding reacts to, detected on the resolved SOURCE element.
    // Focus and FocusWithin share the source's FocusIn (it bubbles, so it fires for the source itself or any
    // descendant gaining focus); Checked is the peer source's own toggle state.
    internal enum RelationalVariantSignal
    {
        Hover,
        Focus,
        FocusWithin,
        Active,
        Checked,
    }

    // Detects relational interaction state on a resolved group/peer SOURCE element and reports each on/off
    // TRANSITION EDGE to a callback. Owns only the detection — the bubbling-PointerOut worldBound check on the
    // source and the own-source checked filter — so the relational binding and the stacked-variant relational
    // inner share one implementation. The source does not dedup; consumers guard on their own per-state
    // bookkeeping. The callback is captured once; the source element and checked-registration choice are
    // captured per Hook so one instance is reused across resolves (re-pointed at a new source) without
    // reallocating, and Unhook reads the captured choice so the pair cannot drift.
    internal sealed class RelationalVariantSignals
    {
        private readonly Action<RelationalVariantSignal, bool> _emit;
        private VisualElement? _source;    // non-null only while hooked
        private bool _registerChecked;    // captured in Hook so Unhook stays symmetric

        public RelationalVariantSignals(Action<RelationalVariantSignal, bool> emit) => _emit = emit;

        // Registers the detection callbacks on the source. registerChecked enables the peer-checked path
        // (ChangeEvent + the initial already-checked Toggle read via seedChecked, since ChangeEvent fires only
        // on change); group bindings and the stacked relational inner pass false.
        public void Hook(VisualElement source, bool seedChecked, bool registerChecked)
        {
            _source = source;
            _registerChecked = registerChecked;

            source.RegisterCallback<PointerOverEvent>(OnPointerOver);
            source.RegisterCallback<PointerOutEvent>(OnPointerOut);
            source.RegisterCallback<PointerDownEvent>(OnPointerDown);
            source.RegisterCallback<PointerUpEvent>(OnPointerUp);
            source.RegisterCallback<FocusInEvent>(OnFocusIn);
            source.RegisterCallback<FocusOutEvent>(OnFocusOut);

            if (registerChecked)
            {
                source.RegisterCallback<ChangeEvent<bool>>(OnChange);
                if (seedChecked && source is Toggle toggle && toggle.value)
                {
                    _emit(RelationalVariantSignal.Checked, true);
                }
            }
        }

        public void Unhook()
        {
            if (_source == null)
            {
                return;
            }

            _source.UnregisterCallback<PointerOverEvent>(OnPointerOver);
            _source.UnregisterCallback<PointerOutEvent>(OnPointerOut);
            _source.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            _source.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            _source.UnregisterCallback<FocusInEvent>(OnFocusIn);
            _source.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            if (_registerChecked)
            {
                _source.UnregisterCallback<ChangeEvent<bool>>(OnChange);
            }

            _source = null;
        }

        private void OnPointerOver(PointerOverEvent evt) => _emit(RelationalVariantSignal.Hover, true);

        private void OnPointerOut(PointerOutEvent evt)
        {
            // Bubbling Out: still inside the source (crossing its descendants) keeps hover/active. A null
            // source (an out delivered after Unhook on a re-entrant flush) is treated as a leave — this
            // matches the original handler's `_source != null && ...` short-circuit and never dereferences.
            if (_source != null && _source.worldBound.Contains(evt.position))
            {
                return;
            }
            _emit(RelationalVariantSignal.Hover, false);
            _emit(RelationalVariantSignal.Active, false);
        }

        private void OnPointerDown(PointerDownEvent evt) => _emit(RelationalVariantSignal.Active, true);

        private void OnPointerUp(PointerUpEvent evt) => _emit(RelationalVariantSignal.Active, false);

        private void OnFocusIn(FocusInEvent evt)
        {
            // FocusIn bubbles, so it fires for the source itself OR any descendant gaining focus — the focus
            // and focus-within layers share this one signal (the consumer applies them at distinct priorities).
            _emit(RelationalVariantSignal.Focus, true);
            _emit(RelationalVariantSignal.FocusWithin, true);
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            _emit(RelationalVariantSignal.Focus, false);
            _emit(RelationalVariantSignal.FocusWithin, false);
        }

        private void OnChange(ChangeEvent<bool> evt)
        {
            // peer-checked reflects the source's OWN checked state; a bubbling ChangeEvent from a descendant
            // of the source is ignored.
            if (!ReferenceEquals(evt.target, _source))
            {
                return;
            }
            _emit(RelationalVariantSignal.Checked, evt.newValue);
        }
    }
}
