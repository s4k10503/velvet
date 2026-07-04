using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Toggles has-[:checked]: / has-[:focus]: payloads on an element styled by a DESCENDANT condition,
    // implementing the event-driven subset of the CSS :has() relational pseudo-class.
    // has-[:checked]: lights up while ANY descendant control (an INotifyValueChanged<bool>, e.g. a Toggle)
    // is checked; has-[:focus]: lights up while a descendant holds focus (focus-within).
    // Lifecycle mirrors StyleVariantManipulator: the reconciler attaches one per element that carries these
    // tokens, keeps it in ReconcilerContext.HasVariantManipulators, and removes it on cleanup / dispose.
    // Both signals ride BUBBLING events dispatched at the descendant: ChangeEvent<bool> bubbles up from the
    // descendant Toggle, and FocusInEvent / FocusOutEvent bubble up for focus-within — so the events reach
    // this element while the change/focus happened on a child (the CSS :has ancestor chain). On a checked
    // event the whole subtree is re-scanned (a sibling toggle may still be on), and an initial scan runs on
    // attach so an already-checked descendant lights the payload immediately.
    // A structural child-set change (a checked / focused descendant added or removed by reconciliation) fires
    // NO ChangeEvent / FocusEvent, so the container's post-children pass (FiberNodePatcher's
    // ApplyHasVariantManipulators) calls Rescan() to re-derive both signals from a fresh probe — the same
    // re-derive-on-any-child-change contract the structural and has-[.class]: side-tables follow.
    // The .class form (has-[.foo]:) is NOT handled here — it is a side-table evaluated by the container
    // post-children pass (see FiberNodePatcher.ApplyHasClassVariants), because its reactivity is to a child
    // being added / removed rather than to a discrete event.
    internal sealed class StyleHasVariantManipulator : Manipulator
    {
        private string[] _checked;
        private string[] _focus;
        private bool _isChecked;
        private bool _isFocused;

        private readonly ReconcilerContext _ctx;

        public StyleHasVariantManipulator(ReconcilerContext ctx, string[] @checked, string[] focus)
        {
            _ctx = ctx;
            _checked = @checked ?? Array.Empty<string>();
            _focus = focus ?? Array.Empty<string>();
        }

        // Re-derives the checked / focus signals from a fresh subtree probe, syncing each payload to the
        // result. Driven by the container's post-children pass (FiberNodePatcher.ApplyHasVariantManipulators)
        // so a descendant ADDED or REMOVED by reconciliation re-derives the payload even though no
        // ChangeEvent / FocusEvent fires for a structural child-set change — mirroring how the structural and
        // has-[.class]: side-tables re-derive on every child-set change. Idempotent.
        public void Rescan()
        {
            if (target == null)
            {
                return;
            }
            if (_checked.Length > 0)
            {
                RescanChecked();
            }
            if (_focus.Length > 0)
            {
                RescanFocus();
            }
        }

        // Swaps the payload sets, re-deriving each signal under the new sets so a shrunk-to-empty set never
        // leaves a stale latched bit. The checked signal is re-derived from a fresh subtree probe; the focus
        // signal is event-driven and a re-render fires no focus event, so the live focus-within state still
        // holds and is merely re-applied under the new payload (cleared only when its set empties).
        public void UpdatePayloads(string[] @checked, string[] focus)
        {
            if (target != null)
            {
                if (_isChecked) ApplyPayloads(_checked, false);
                if (_isFocused) ApplyPayloads(_focus, false);
            }

            _checked = @checked ?? Array.Empty<string>();
            _focus = focus ?? Array.Empty<string>();

            _isChecked = false;
            if (_focus.Length == 0)
            {
                _isFocused = false;
            }

            if (target != null)
            {
                if (_checked.Length > 0) RescanChecked();
                if (_isFocused) ApplyPayloads(_focus, true);
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            // Bubbling events: a descendant's change / focus reaches this element via the bubble-up phase.
            target.RegisterCallback<ChangeEvent<bool>>(OnDescendantChange);
            target.RegisterCallback<FocusInEvent>(OnFocusIn);
            target.RegisterCallback<FocusOutEvent>(OnFocusOut);

            // ChangeEvent<bool> fires only on a change, so seed the checked state from a descendant that is
            // already on at attach time (a Toggle mounted with value == true).
            if (_checked.Length > 0)
            {
                RescanChecked();
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            if (_isChecked) ApplyPayloads(_checked, false);
            if (_isFocused) ApplyPayloads(_focus, false);
            _isChecked = false;
            _isFocused = false;

            target.UnregisterCallback<ChangeEvent<bool>>(OnDescendantChange);
            target.UnregisterCallback<FocusInEvent>(OnFocusIn);
            target.UnregisterCallback<FocusOutEvent>(OnFocusOut);
        }

        private void OnDescendantChange(ChangeEvent<bool> evt)
        {
            // Unlike StyleVariantManipulator's checked: (which gates on evt.target == target), has-[:checked]:
            // deliberately accepts the bubbling event from ANY descendant. The event carries only the one
            // control's new value, so re-scan the whole subtree: another descendant may still be checked.
            if (_checked.Length > 0)
            {
                RescanChecked();
            }
        }

        // Re-derives _isChecked from a fresh subtree scan and syncs the payload to it. Idempotent.
        private void RescanChecked()
        {
            var anyChecked = AnyDescendantChecked(target);
            if (anyChecked == _isChecked)
            {
                return;
            }
            _isChecked = anyChecked;
            ApplyPayloads(_checked, _isChecked);
        }

        // Re-derives _isFocused from a focus-within probe (the panel's currently-focused element is a STRICT
        // descendant of target — focus-within excludes target itself) and syncs the payload. Only runs when a
        // focus controller is observable: off panel there is nothing to query, and the focus signal there is
        // driven solely by directly-dispatched FocusIn / FocusOut events, so probing would wrongly clear that
        // event-set state. This makes a structural child-set change re-derive focus-within from the live,
        // authoritative controller — the robust path when a focused descendant is detached by reconciliation
        // (which need not dispatch a bubbling FocusOut to this still-attached ancestor). Idempotent.
        private void RescanFocus()
        {
            var controller = target.focusController;
            if (controller == null)
            {
                return;
            }
            var withinFocus = controller.focusedElement is VisualElement focused && IsStrictDescendant(focused);
            if (withinFocus == _isFocused)
            {
                return;
            }
            _isFocused = withinFocus;
            ApplyPayloads(_focus, _isFocused);
        }

        // True when any descendant (excluding the element itself) is an INotifyValueChanged<bool> that is on.
        private static bool AnyDescendantChecked(VisualElement root)
        {
            var count = root.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = root.ElementAt(i);
                if (child is INotifyValueChanged<bool> boolField && boolField.value)
                {
                    return true;
                }
                if (AnyDescendantChecked(child))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnFocusIn(FocusInEvent evt)
        {
            // FocusIn bubbles, so it reaches this element when EITHER itself or a descendant gains focus.
            // has-[:focus]: is focus-WITHIN (CSS :has(:focus)): only a focused DESCENDANT lights it, never this
            // element focusing itself — so gate on evt.target being a strict descendant.
            if (_focus.Length > 0 && !_isFocused && evt.target is VisualElement focused && IsStrictDescendant(focused))
            {
                _isFocused = true;
                ApplyPayloads(_focus, true);
            }
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            // FocusOut bubbles too. When focus merely hops between two descendants, FocusOut fires on the old
            // one while the new one (still inside this subtree) takes focus, so clearing unconditionally would
            // briefly drop focus-within during an internal hop. evt.relatedTarget is the element gaining focus:
            // keep the payload while it is still inside this element's subtree; clear only on a real leave.
            if (!_isFocused)
            {
                return;
            }
            if (evt.relatedTarget is VisualElement next && IsStrictDescendant(next))
            {
                return;
            }
            _isFocused = false;
            ApplyPayloads(_focus, false);
        }

        // True when candidate is a STRICT descendant of target (a child at any depth, never target itself).
        // has-[:focus]: is focus-within (CSS :has(:focus)) — the element matches on a focused descendant, not
        // on itself holding focus — so self is excluded by starting the walk at candidate's parent.
        private bool IsStrictDescendant(VisualElement candidate)
        {
            var p = candidate?.parent;
            while (p != null)
            {
                if (ReferenceEquals(p, target))
                {
                    return true;
                }
                p = p.parent;
            }
            return false;
        }

        // Applies (or clears) each payload at the Has layer. A payload that parses as an arbitrary value is
        // applied as an inline style; otherwise it is toggled as a USS class. owner is passed so a payload
        // that is itself a (stacked) variant defers to a nested manipulator, matching the other manipulators.
        private void ApplyPayloads(string[] payloads, bool on)
            => StyleVariantPayload.Apply(target, payloads, on, StyleLayerPriority.Has, _ctx, this);
    }
}
