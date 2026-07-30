using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The className position of every state payload, grouped so the five arrays travel together rather than
    // as five more parameters on each of the two entry points that carry them.
    internal readonly struct VariantDeclarations
    {
        public static readonly VariantDeclarations None = new(
            Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

        public VariantDeclarations(int[] hover, int[] focus, int[] focusVisible, int[] active, int[] @checked)
        {
            Hover = hover ?? Array.Empty<int>();
            Focus = focus ?? Array.Empty<int>();
            FocusVisible = focusVisible ?? Array.Empty<int>();
            Active = active ?? Array.Empty<int>();
            Checked = @checked ?? Array.Empty<int>();
        }

        public int[] Hover { get; }

        public int[] Focus { get; }

        public int[] FocusVisible { get; }

        public int[] Active { get; }

        public int[] Checked { get; }
    }

    // A relational binding has no focus-visible state of its own, so its focus-WITHIN payloads ride the
    // FocusVisible slot here and in VariantDeclarations — which is what lets both variant families share one
    // payload/declaration pair (see StyleRelationalVariantManipulator).
    internal readonly struct VariantPayloads
    {
        public VariantPayloads(string[] hover, string[] focus, string[] focusVisible, string[] active,
            string[] @checked)
        {
            Hover = hover ?? Array.Empty<string>();
            Focus = focus ?? Array.Empty<string>();
            FocusVisible = focusVisible ?? Array.Empty<string>();
            Active = active ?? Array.Empty<string>();
            Checked = @checked ?? Array.Empty<string>();
        }

        public string[] Hover { get; }

        public string[] Focus { get; }

        public string[] FocusVisible { get; }

        public string[] Active { get; }

        public string[] Checked { get; }
    }

    // Toggles utility payloads in response to hover / focus / active state, implementing the general
    // hover: / focus: / active: variants for any utility (a USS class such as
    // bg-blue-500 or an arbitrary value such as w-[200px]).
    // Mirrors StyleGestureClassManipulator's lifecycle: the reconciler attaches one per
    // element that has variant tokens, keeps it in ReconcilerContext.VariantManipulators, and
    // removes it on cleanup / dispose. Single-pointer assumption, like the gesture manipulator.
    // Focus uses FocusEvent / BlurEvent (the element itself), matching the
    // USS :focus pseudo-class — non-focusable elements simply never trigger it.
    // focus-visible: additionally distinguishes <em>keyboard/programmatic</em> focus from
    // <em>pointer</em> focus, mirroring the CSS :focus-visible heuristic. Since a click on a
    // focusable element dispatches PointerDownEvent immediately before the
    // FocusEvent, a focus preceded by a pointer-down on this element is treated as a
    // pointer focus (no focus-visible); any other focus (Tab navigation, Focus()) lights
    // it up. A subsequent pointer-down while focused also clears it, matching browsers dropping the
    // focus ring on mouse interaction.
    // Hover uses the <em>bubbling</em> PointerOverEvent / PointerOutEvent pair, not
    // the non-bubbling PointerEnter/PointerLeave: when a child element (a label/icon) covers the
    // interior, only a bubbling event reaches this element while the pointer is over that child — matching the
    // CSS :hover ancestor chain. On PointerOut the hover is cleared only
    // once the pointer has actually left this element's bounds; while it merely crosses between descendants the
    // payload is kept, avoiding a per-crossing remove/re-add that restarts any transition.
    internal sealed class StyleVariantManipulator : Manipulator, IVariantSettleTarget
    {
        private string[] _hover;
        private string[] _focus;
        private string[] _focusVisible;
        private string[] _active;
        private string[] _checked;
        // Each payload's position in the className, kept per state alongside the payloads themselves so a
        // payload can be ranked against one a DIFFERENT owner applied at the same layer — a stacked
        // dark:hover: shares this manipulator's hover layer. Identified by reference, like PriorityFor.
        private int[] _hoverDeclarations;
        private int[] _focusDeclarations;
        private int[] _focusVisibleDeclarations;
        private int[] _activeDeclarations;
        private int[] _checkedDeclarations;
        private bool _isHovered;
        private bool _isFocused;
        private bool _isFocusVisible;
        private bool _isActive;
        private bool _isChecked;

        // Owns the element-local signal detection (pointer/focus/checked edges, focus-visible heuristic,
        // worldBound bubbling); this manipulator keeps the per-state payload bookkeeping below.
        private ElementLocalVariantSignals _signals = null!;

        private readonly ReconcilerContext _ctx;

        public StyleVariantManipulator(ReconcilerContext ctx, VariantPayloads payloads,
            VariantDeclarations declarations)
        {
            _ctx = ctx;
            _hover = payloads.Hover;
            _focus = payloads.Focus;
            _focusVisible = payloads.FocusVisible;
            _active = payloads.Active;
            _checked = payloads.Checked;
            _hoverDeclarations = declarations.Hover;
            _focusDeclarations = declarations.Focus;
            _focusVisibleDeclarations = declarations.FocusVisible;
            _activeDeclarations = declarations.Active;
            _checkedDeclarations = declarations.Checked;
        }

        // Applies (on) or clears (off) the payloads for every state currently flagged active, under the
        // current payload sets. The (state-flag, payload-array) pairing lives here once so a payload swap or
        // a detach cannot ghost a state by missing one rung of the ladder.
        private void ReapplyActiveStates(bool on)
        {
            if (_isHovered) ApplyPayloads(_hover, on);
            if (_isFocused) ApplyPayloads(_focus, on);
            if (_isFocusVisible) ApplyPayloads(_focusVisible, on);
            if (_isActive) ApplyPayloads(_active, on);
            if (_isChecked) ApplyPayloads(_checked, on);
        }

        // Swaps the payload sets, re-applying any currently-active state under the new sets.
        public void UpdatePayloads(VariantPayloads payloads, VariantDeclarations declarations)
        {
            if (target != null) ReapplyActiveStates(false);

            _hover = payloads.Hover;
            _focus = payloads.Focus;
            _focusVisible = payloads.FocusVisible;
            _active = payloads.Active;
            _checked = payloads.Checked;
            _hoverDeclarations = declarations.Hover;
            _focusDeclarations = declarations.Focus;
            _focusVisibleDeclarations = declarations.FocusVisible;
            _activeDeclarations = declarations.Active;
            _checkedDeclarations = declarations.Checked;

            if (target != null) ReapplyActiveStates(true);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            _signals ??= new ElementLocalVariantSignals(OnSignal);
            // seedChecked lights up an already-checked control on attach: ChangeEvent fires only on a
            // change, so a Toggle mounted with value == true is read at hook time.
            _signals.Hook(target, seedChecked: _checked.Length > 0, registerChecked: true);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ReapplyActiveStates(false);
            _isHovered = false;
            _isFocused = false;
            _isFocusVisible = false;
            _isActive = false;
            _isChecked = false;

            _signals?.Unhook();
        }

        // Forwards a drag session's synthetic release to the shared signal source (see
        // ElementLocalVariantSignals.SettleRelease); the per-state dedup below makes it idempotent.
        public void SettleRelease() => _signals?.SettleRelease();

        // Forwards a snap-back's synthetic focus loss (see ElementLocalVariantSignals.SettleFocusLoss).
        public void SettleFocusLoss() => _signals?.SettleFocusLoss();

        // Maps a detected element-local signal edge to its payload, deduping on the per-state bookkeeping so
        // a repeated edge (e.g. a bubbling PointerOver, or a no-op checked change) does not churn the payload.
        private void OnSignal(VariantSignal signal, bool on)
        {
            switch (signal)
            {
                case VariantSignal.Hover:
                    if (on != _isHovered) { _isHovered = on; ApplyPayloads(_hover, on); }
                    break;
                case VariantSignal.Focus:
                    if (on != _isFocused) { _isFocused = on; ApplyPayloads(_focus, on); }
                    break;
                case VariantSignal.FocusVisible:
                    if (on != _isFocusVisible) { _isFocusVisible = on; ApplyPayloads(_focusVisible, on); }
                    break;
                case VariantSignal.Active:
                    if (on != _isActive) { _isActive = on; ApplyPayloads(_active, on); }
                    break;
                case VariantSignal.Checked:
                    if (on != _isChecked) { _isChecked = on; ApplyPayloads(_checked, on); }
                    break;
            }
        }

        // Applies (or clears) each payload. A payload containing [ that parses as an arbitrary
        // value is applied as an inline style; otherwise it is toggled as a USS class.
        private void ApplyPayloads(string[] payloads, bool on)
            => StyleVariantPayload.Apply(target, payloads, on, PriorityFor(payloads), _ctx, this,
                DeclarationsFor(payloads));

        // The className positions belonging to the state whose payload array this is, paired the same way
        // PriorityFor pairs the layer.
        private int[] DeclarationsFor(string[] payloads) =>
            ReferenceEquals(payloads, _checked) ? _checkedDeclarations
            : ReferenceEquals(payloads, _active) ? _activeDeclarations
            : ReferenceEquals(payloads, _focusVisible) ? _focusVisibleDeclarations
            : ReferenceEquals(payloads, _focus) ? _focusDeclarations
            : _hoverDeclarations;

        // Arbitrary-value layering priority for the state whose payload array this is (identified by reference),
        // so e.g. an active arbitrary value layers over a hover one, and clearing active falls back to hover.
        private int PriorityFor(string[] payloads) =>
            ReferenceEquals(payloads, _checked) ? StyleLayerPriority.Checked
            : ReferenceEquals(payloads, _active) ? StyleLayerPriority.Active
            : ReferenceEquals(payloads, _focusVisible) ? StyleLayerPriority.FocusVisible
            : ReferenceEquals(payloads, _focus) ? StyleLayerPriority.Focus
            : StyleLayerPriority.Hover;
    }
}
