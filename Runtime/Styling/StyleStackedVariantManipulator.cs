using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Where an inner kind's signal comes from, and so which branch of the manipulator below owns it.
    internal enum StackedInnerSource
    {
        ElementLocal,
        Theme,
        Responsive,
        Relational,
    }

    // Applies a stacked-variant leaf payload iff BOTH the outer gate (set by the owning manipulator when its
    // own condition holds) AND this inner variant's own signal are active, implementing variant
    // stacking (dark:hover:bg-red == apply bg-red iff dark AND hovered, order-independent). One instance per
    // (target, owner, inner-kind, leaf) lives in ReconcilerContext.StackedVariantManipulators and is torn down
    // with the three top-level variant manipulators. It subscribes to the SAME signal source the matching
    // top-level manipulator uses, but applies nothing until the outer gate opens. The leaf itself may STILL be
    // a variant (dark:hover:focus:...): StyleVariantPayload.Apply recurses, spawning a further stacked
    // manipulator gated by THIS one's combined state.
    internal sealed class StyleStackedVariantManipulator : Manipulator, IVariantSettleTarget
    {
        private readonly ReconcilerContext _ctx;
        private readonly StyleVariantKind _innerKind;
        private readonly StackedInnerSource _source;
        // Non-null exactly for a relational inner: the source it resolves and the state it reacts to.
        private readonly (bool IsPeer, StyleVariantClass.RelationalState State)? _relational;
        private readonly string _innerName; // relational name of a NAMED inner (group-hover/sidebar:), else ""
        private readonly string[] _leaf;
        private readonly int _priority;
        // The position of the WHOLE stacked class (dark:hover:shadow-lg) in the className, not of the leaf it
        // peels to — one written token is one declaration site, and that is the position a reader would point
        // at when this leaf ties with a plain hover: payload on the shared layer.
        private readonly int[] _declarations;

        private bool _outerOn;
        private bool _innerOn;
        private bool _applied;
        private ElementLocalVariantSignals _elementSignals = null!;
        private ResponsiveWidthSource _widthSource = null!;
        private RelationalVariantSignals _relSignals = null!;

        public StyleStackedVariantManipulator(
            ReconcilerContext ctx, StyleVariantKind innerKind, string? innerName, string?[] leaf, int priority,
            int declaration)
        {
            _declarations = new[] { declaration };
            _ctx = ctx;
            _innerKind = innerKind;
            _source = SourceOf(innerKind);
            _relational = StyleVariantClass.RelationalOf(innerKind);
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

#pragma warning disable CS8524 // no discard arm — see the remarks on StyleVariantKind
        private static StackedInnerSource SourceOf(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.Hover or StyleVariantKind.Focus or StyleVariantKind.FocusVisible
                or StyleVariantKind.Active or StyleVariantKind.Checked => StackedInnerSource.ElementLocal,
            StyleVariantKind.Dark => StackedInnerSource.Theme,
            StyleVariantKind.Sm or StyleVariantKind.Md or StyleVariantKind.Lg
                or StyleVariantKind.Xl or StyleVariantKind.Xxl => StackedInnerSource.Responsive,
            StyleVariantKind.GroupHover or StyleVariantKind.GroupFocus
                or StyleVariantKind.GroupFocusWithin or StyleVariantKind.GroupActive
                or StyleVariantKind.PeerHover or StyleVariantKind.PeerFocus
                or StyleVariantKind.PeerFocusWithin or StyleVariantKind.PeerActive
                or StyleVariantKind.PeerChecked => StackedInnerSource.Relational,
        };

        // Edge-based inners survive an outer-gate close (see ReconcilerContext.GateStackedVariant):
        // their signals fire only on state edges, so a re-created manipulator could not re-seed a
        // continuously-held hover/focus. Level-based inners (dark, responsive) re-derive their truth
        // on attach and are detached on close to release their subscriptions.
        internal bool RetainsAcrossOuterClose => _source switch
        {
            StackedInnerSource.ElementLocal or StackedInnerSource.Relational => true,
            StackedInnerSource.Theme or StackedInnerSource.Responsive => false,
        };
#pragma warning restore CS8524

        private bool TracksChecked =>
            _innerKind == StyleVariantKind.Checked
            || _relational is { State: StyleVariantClass.RelationalState.Checked };

        // Forwards a drag session's synthetic release to the shared signal source (see
        // ElementLocalVariantSignals.SettleRelease); a non-element-local inner (dark:/sm:) has no press
        // state to settle and no signals instance, so the null-conditional is the whole guard.
        public void SettleRelease() => _elementSignals?.SettleRelease();

        // Forwards a snap-back's synthetic focus loss (see ElementLocalVariantSignals.SettleFocusLoss).
        public void SettleFocusLoss() => _elementSignals?.SettleFocusLoss();

        protected override void RegisterCallbacksOnTarget()
        {
            if (_source == StackedInnerSource.ElementLocal)
            {
                _elementSignals ??= new ElementLocalVariantSignals(OnElementSignal);
                _elementSignals.Hook(target, seedChecked: TracksChecked, registerChecked: TracksChecked);
            }
            else if (_source == StackedInnerSource.Theme)
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
                    if (_source == StackedInnerSource.Responsive)
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

            if (_source == StackedInnerSource.ElementLocal)
            {
                _elementSignals?.Unhook();
            }
            else if (_source == StackedInnerSource.Theme)
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

        #region element-local signals (hover / focus / focus-visible / active / checked; shared detection via ElementLocalVariantSignals)

        // Opens/closes the inner gate when the detected element-local signal matches this stack's inner kind.
        // SetInner dedups, so the focus-visible-drop-on-PointerDown (a no-op unless the ring is on) and any
        // repeated edge are inert. The signal source applies the focus-visible heuristic and worldBound
        // bubbling, so this only routes the edge.
        private void OnElementSignal(VariantSignal signal, bool on)
        {
#pragma warning disable CS8524 // no discard arm: a signal this stack cannot route has to warn
            var matches = signal switch
            {
                VariantSignal.Hover => _innerKind == StyleVariantKind.Hover,
                VariantSignal.Focus => _innerKind == StyleVariantKind.Focus,
                VariantSignal.FocusVisible => _innerKind == StyleVariantKind.FocusVisible,
                VariantSignal.Active => _innerKind == StyleVariantKind.Active,
                VariantSignal.Checked => _innerKind == StyleVariantKind.Checked,
            };
#pragma warning restore CS8524
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
            if (_source == StackedInnerSource.Responsive)
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

        private void ResolveRelational()
        {
            UnhookRelational();
            if (_relational is not { } rel)
            {
                return;
            }
            // A checked inner's value is seeded at hook time rather than arriving as an edge, so a re-resolve
            // has to drop what the previous source seeded before reading the new one — the same order
            // StyleRelationalVariantManipulator's Binding.Resolve keeps.
            if (TracksChecked)
            {
                SetInner(false);
            }
            // A named inner (dark:group-hover/sidebar:) resolves the `group/sidebar` source, not the unnamed one.
            var sourceClass = StyleRelationalVariantManipulator.SourceClassFor(rel.IsPeer, _innerName);
            var source = rel.IsPeer
                ? StyleRelationalVariantManipulator.FindPrevSiblingWithClass(target, sourceClass, _ctx)
                : StyleRelationalVariantManipulator.FindAncestorWithClass(target, sourceClass);
            if (source == null)
            {
                return;
            }
            _relSignals ??= new RelationalVariantSignals(OnRelSignal);
            _relSignals.Hook(source, seedChecked: TracksChecked, registerChecked: TracksChecked);
        }

        private void UnhookRelational()
        {
            _relSignals?.Unhook();
        }

        // Opens/closes the inner gate when the detected relational signal matches this stack's inner kind.
        private void OnRelSignal(RelationalVariantSignal signal, bool on)
        {
            if (_relational is { } rel && StyleVariantClass.StateOf(signal) == rel.State)
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
            StyleVariantPayload.Apply(target, _leaf, want, _priority, _ctx, this, _declarations);
        }

        private void ClearApplied()
        {
            if (!_applied)
            {
                return;
            }
            _applied = false;
            StyleVariantPayload.Apply(target, _leaf, false, _priority, _ctx, this, _declarations);
        }
        #endregion
    }
}
