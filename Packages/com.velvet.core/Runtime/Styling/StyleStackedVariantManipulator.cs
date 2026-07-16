using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Applies a stacked-variant leaf payload iff BOTH the outer gate (set by the owning manipulator when its
    // own condition holds) AND this inner variant's own signal are active, implementing variant
    // stacking (dark:hover:bg-red == apply bg-red iff dark AND hovered, order-independent). One instance per
    // (target, owner, inner-kind, leaf) lives in ReconcilerContext.StackedVariantManipulators and is torn down
    // with the three top-level variant manipulators. It subscribes to the SAME signal source the matching
    // top-level manipulator uses, but applies nothing until the outer gate opens. The leaf itself may STILL be
    // a variant (dark:hover:focus:...): StyleVariantPayload.Apply recurses, spawning a further stacked
    // manipulator gated by THIS one's combined state.
    internal sealed class StyleStackedVariantManipulator : Manipulator
    {
        private readonly ReconcilerContext _ctx;
        private readonly StyleVariantKind _innerKind;
        private readonly string _innerName; // relational name of a NAMED inner (group-hover/sidebar:), else ""
        private readonly string[] _leaf;
        private readonly int _priority;

        private bool _outerOn;
        private bool _innerOn;
        private bool _applied;
        private ElementLocalVariantSignals _elementSignals = null!;
        private ResponsiveWidthSource _widthSource = null!;
        private RelationalVariantSignals _relSignals = null!;

        public StyleStackedVariantManipulator(
            ReconcilerContext ctx, StyleVariantKind innerKind, string? innerName, string?[] leaf, int priority)
        {
            _ctx = ctx;
            _innerKind = innerKind;
            _innerName = innerName ?? string.Empty;
            _leaf = leaf != null
                ? Array.ConvertAll(leaf, static x => x ?? string.Empty)
                : Array.Empty<string>();
            _priority = priority;
        }

        // Called by the owning manipulator each time its own gate flips.
        public void SetOuterGate(bool on)
        {
            if (_outerOn == on)
            {
                return;
            }
            _outerOn = on;
            Sync();
        }

        // The newer variant kinds (checked:, group/peer-focus-within:, peer-checked:) are intentionally
        // absent here: they are supported as TOP-LEVEL variants but not yet as the INNER of a stack
        // (dark:checked:…). An unrecognized inner kind falls through to the relational branch and stays
        // inert (no signal ever flips the inner gate) rather than crashing.
        private bool IsElementLocal =>
            _innerKind is StyleVariantKind.Hover or StyleVariantKind.Focus
                or StyleVariantKind.FocusVisible or StyleVariantKind.Active;

        private bool IsRelational =>
            _innerKind is StyleVariantKind.GroupHover or StyleVariantKind.GroupFocus or StyleVariantKind.GroupActive
                or StyleVariantKind.PeerHover or StyleVariantKind.PeerFocus or StyleVariantKind.PeerActive;

        // Edge-based inners survive an outer-gate close (see ReconcilerContext.GateStackedVariant):
        // their pointer/focus signals fire only on state edges, so a re-created manipulator could
        // not re-seed a continuously-held hover/focus. Level-based inners (dark, responsive)
        // re-derive their truth on attach and are detached on close to release their subscriptions.
        internal bool RetainsAcrossOuterClose => IsElementLocal || IsRelational;

        // Forwards a drag session's synthetic release to the shared signal source (see
        // ElementLocalVariantSignals.SettleRelease); a non-element-local inner (dark:/sm:) has no press
        // state to settle and no signals instance, so the null-conditional is the whole guard.
        internal void SettleRelease() => _elementSignals?.SettleRelease();

        // Forwards a snap-back's synthetic focus loss (see ElementLocalVariantSignals.SettleFocusLoss).
        internal void SettleFocusLoss() => _elementSignals?.SettleFocusLoss();

        protected override void RegisterCallbacksOnTarget()
        {
            if (IsElementLocal)
            {
                _elementSignals ??= new ElementLocalVariantSignals(OnElementSignal);
                // The stacked inner never tracks checked:, so the ChangeEvent path is skipped.
                _elementSignals.Hook(target, seedChecked: false, registerChecked: false);
            }
            else if (_innerKind == StyleVariantKind.Dark)
            {
                VelvetTheme.DarkModeChanged += OnDarkChanged;
                EvaluateDark();
            }
            else // responsive or relational
            {
                target.RegisterCallback<AttachToPanelEvent>(OnAttach);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
                if (target.panel != null)
                {
                    if (StyleVariantClass.IsResponsive(_innerKind))
                    {
                        _widthSource ??= new ResponsiveWidthSource(EvaluateResponsive);
                        _widthSource.Hook(StyleResponsiveScope.ResolveWidthSource(target, target.panel.visualTree));
                        EvaluateResponsive();
                    }
                    else
                    {
                        ResolveRelational();
                    }
                }
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ClearApplied();
            _innerOn = false;
            _outerOn = false;

            if (IsElementLocal)
            {
                _elementSignals?.Unhook();
            }
            else if (_innerKind == StyleVariantKind.Dark)
            {
                VelvetTheme.DarkModeChanged -= OnDarkChanged;
            }
            else
            {
                target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                _widthSource?.Unhook();
                UnhookRelational();
            }
        }

        #region element-local signals (hover / focus / focus-visible / active; shared detection via ElementLocalVariantSignals)

        // Opens/closes the inner gate when the detected element-local signal matches this stack's inner kind.
        // SetInner dedups, so the focus-visible-drop-on-PointerDown (a no-op unless the ring is on) and any
        // repeated edge are inert. The signal source applies the focus-visible heuristic and worldBound
        // bubbling, so this only routes the edge.
        private void OnElementSignal(VariantSignal signal, bool on)
        {
            var matches = signal switch
            {
                VariantSignal.Hover => _innerKind == StyleVariantKind.Hover,
                VariantSignal.Focus => _innerKind == StyleVariantKind.Focus,
                VariantSignal.FocusVisible => _innerKind == StyleVariantKind.FocusVisible,
                VariantSignal.Active => _innerKind == StyleVariantKind.Active,
                _ => false,
            };
            if (matches)
            {
                SetInner(on);
            }
        }
        #endregion

        #region dark

        private void OnDarkChanged() => EvaluateDark();
        private void EvaluateDark() => SetInner(VelvetTheme.IsDark);
        #endregion

        #region responsive / relational attach lifecycle

        private void OnAttach(AttachToPanelEvent evt)
        {
            if (StyleVariantClass.IsResponsive(_innerKind))
            {
                _widthSource ??= new ResponsiveWidthSource(EvaluateResponsive);
                _widthSource.Hook(StyleResponsiveScope.ResolveWidthSource(target, evt.destinationPanel?.visualTree));
                EvaluateResponsive();
            }
            else
            {
                ResolveRelational();
            }
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            _widthSource?.Unhook();
            UnhookRelational();
            SetInner(false);
        }
        #endregion

        #region responsive

        private void EvaluateResponsive()
        {
            var width = _widthSource?.Width ?? 0f;
            SetInner(width >= StyleVariantClass.BreakpointPx(_innerKind));
        }
        #endregion

        #region group / peer (shared detection via RelationalVariantSignals)

        private bool RelIsHover => _innerKind is StyleVariantKind.GroupHover or StyleVariantKind.PeerHover;
        private bool RelIsFocus => _innerKind is StyleVariantKind.GroupFocus or StyleVariantKind.PeerFocus;
        private bool RelIsActive => _innerKind is StyleVariantKind.GroupActive or StyleVariantKind.PeerActive;

        private void ResolveRelational()
        {
            UnhookRelational();
            var isGroup = _innerKind is StyleVariantKind.GroupHover or StyleVariantKind.GroupFocus
                or StyleVariantKind.GroupActive;
            // A named inner (dark:group-hover/sidebar:) resolves the `group/sidebar` source, not the unnamed one.
            var sourceClass = StyleRelationalVariantManipulator.SourceClassFor(!isGroup, _innerName);
            var source = isGroup
                ? StyleRelationalVariantManipulator.FindAncestorWithClass(target, sourceClass)
                : StyleRelationalVariantManipulator.FindPrevSiblingWithClass(target, sourceClass);
            if (source == null)
            {
                return;
            }
            // The stacked relational inner never tracks peer-checked, so the ChangeEvent path is skipped.
            _relSignals ??= new RelationalVariantSignals(OnRelSignal);
            _relSignals.Hook(source, seedChecked: false, registerChecked: false);
        }

        private void UnhookRelational()
        {
            _relSignals?.Unhook();
        }

        // Opens/closes the inner gate when the detected relational signal matches this stack's inner kind. The
        // stacked relational inner supports hover/focus/active only — focus-within and peer-checked are ignored.
        private void OnRelSignal(RelationalVariantSignal signal, bool on)
        {
            var matches = signal switch
            {
                RelationalVariantSignal.Hover => RelIsHover,
                RelationalVariantSignal.Focus => RelIsFocus,
                RelationalVariantSignal.Active => RelIsActive,
                _ => false,
            };
            if (matches)
            {
                SetInner(on);
            }
        }
        #endregion

        #region gating

        private void SetInner(bool on)
        {
            if (_innerOn == on)
            {
                return;
            }
            _innerOn = on;
            Sync();
        }

        // Apply the leaf iff both gates hold; recurse so a still-nested leaf spawns a further stacked
        // manipulator gated by this combined state.
        private void Sync()
        {
            var want = _outerOn && _innerOn;
            if (want == _applied)
            {
                return;
            }
            _applied = want;
            StyleVariantPayload.Apply(target, _leaf, want, _priority, _ctx, this);
        }

        private void ClearApplied()
        {
            if (!_applied)
            {
                return;
            }
            _applied = false;
            StyleVariantPayload.Apply(target, _leaf, false, _priority, _ctx, this);
        }
        #endregion
    }
}
