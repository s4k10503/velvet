using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-(relation, name) payload set extracted from an element's class list, handed to the relational
    // manipulator. name is "" for the unnamed group/peer; a non-empty name is the named form
    // (group-hover/sidebar: → IsPeer false, Name "sidebar"). Checked is only meaningful for peer (group has
    // no checked state); it stays empty for a group binding.
    internal readonly struct RelationalBindingConfig
    {
        public readonly bool IsPeer;
        public readonly string Name;
        public readonly string[] Hover;
        public readonly string[] Focus;
        public readonly string[] FocusWithin;
        public readonly string[] Active;
        public readonly string[] Checked;

        public RelationalBindingConfig(
            bool isPeer, string name,
            string[] hover, string[] focus, string[] focusWithin, string[] active, string[] checkedPayloads)
        {
            IsPeer = isPeer;
            Name = name ?? string.Empty;
            Hover = hover ?? Array.Empty<string>();
            Focus = focus ?? Array.Empty<string>();
            FocusWithin = focusWithin ?? Array.Empty<string>();
            Active = active ?? Array.Empty<string>();
            Checked = checkedPayloads ?? Array.Empty<string>();
        }
    }

    // Toggles relational variant payloads — the group/peer variants:
    // group-hover: / group-focus: / group-active: react to the nearest ANCESTOR marked with the group class.
    // peer-hover: / peer-focus: / peer-active: / peer-checked: react to the nearest preceding SIBLING marked
    // with the peer class.
    // The NAMED forms are supported too: group-hover/sidebar: reacts to the nearest ancestor marked
    // `group/sidebar`, peer-checked/email: to the nearest preceding sibling marked `peer/email`. One element
    // may consume several distinct named (and the unnamed) sources at once, so the manipulator holds a LIST of
    // bindings — one per (relation, name) — each resolving and subscribing to its own source independently.
    // The manipulator lives on the consuming (child) element. Focus variants use FocusInEvent / FocusOutEvent
    // (focus-within semantics) since group/peer sources are commonly containers or inputs. Sources are
    // resolved on AttachToPanelEvent. Lifecycle mirrors the other variant manipulators
    // (ReconcilerContext.RelationalVariantManipulators).
    internal sealed class StyleRelationalVariantManipulator : Manipulator
    {
        internal const string GroupClass = "group";
        internal const string PeerClass = "peer";

        // The class that marks a relational source: `group`/`peer` for the unnamed form, `group/name` /
        // `peer/name` for a named one — the way a named group/peer container is tagged. Shared by
        // the binding here and by the stacked-variant manipulator's relational inner.
        internal static string SourceClassFor(bool isPeer, string name)
        {
            var baseClass = isPeer ? PeerClass : GroupClass;
            return string.IsNullOrEmpty(name) ? baseClass : baseClass + "/" + name;
        }

        private readonly ReconcilerContext _ctx;
        private readonly List<Binding> _bindings = new();

        public StyleRelationalVariantManipulator(ReconcilerContext ctx, List<RelationalBindingConfig>? configs)
        {
            _ctx = ctx;
            BuildBindings(configs);
        }

        public void UpdatePayloads(List<RelationalBindingConfig>? configs)
        {
            // Tear the old bindings fully down (clear applied payloads + unhook sources), rebuild from the new
            // config set, then re-resolve against the live tree if already attached. A full rebuild keeps the
            // (relation, name) set authoritative — a name that disappeared from the class list drops its binding.
            ResetApplied();
            UnhookAll();
            _bindings.Clear();
            BuildBindings(configs);
            if (target?.panel != null)
            {
                ResolveAll();
            }
        }

        private void BuildBindings(List<RelationalBindingConfig>? configs)
        {
            if (configs == null)
            {
                return;
            }
            foreach (var c in configs)
            {
                _bindings.Add(new Binding(this, c));
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            if (target.panel != null)
            {
                ResolveAll();
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ResetApplied();
            UnhookAll();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        private void OnAttach(AttachToPanelEvent evt) => ResolveAll();

        private void OnDetach(DetachFromPanelEvent evt)
        {
            ResetApplied();
            UnhookAll();
        }

        private void ResolveAll()
        {
            foreach (var b in _bindings)
            {
                b.Resolve(target);
            }
        }

        private void UnhookAll()
        {
            foreach (var b in _bindings)
            {
                b.Unhook();
            }
        }

        private void ResetApplied()
        {
            if (target == null)
            {
                return;
            }
            foreach (var b in _bindings)
            {
                b.ResetApplied();
            }
        }

        // Applies (or clears) a binding's payload at the priority of its relational state. The owner passed to
        // StyleVariantPayload.Apply is the BINDING (not the manipulator): a stacked relational payload
        // (group-hover/a:hover:bg-red) routes to ReconcilerContext.GateStackedVariant whose dedup key includes
        // the owner, and the per-state priority is shared across names — so two named bindings of the same
        // state + same inner leaf must use DISTINCT owners or one's exit would tear down the gate the other
        // still needs. Per-binding owners keep their nested manipulators independent.
        private void ApplyPayloads(object owner, string[] payloads, bool on, int priority)
            => StyleVariantPayload.Apply(target, payloads, on, priority, _ctx, owner);

        internal static VisualElement? FindAncestorWithClass(VisualElement element, string cls)
        {
            var p = element.parent;
            while (p != null)
            {
                if (p.ClassListContains(cls))
                {
                    return p;
                }
                p = p.parent;
            }
            return null;
        }

        // ctx resolves the LOGICAL search origin for a z-relocated consumer (FiberZLayerCoordinator.
        // TryGetLogicalPosition): a z-managed element's physical parent is its layer container, whose
        // "preceding siblings" are unrelated same-layer members, not this element's declared siblings — the
        // search must walk from its PLACEHOLDER's position instead. An ordinary element resolves to its own
        // parent/index unchanged. Does NOT cover the reverse direction (the peer/group SOURCE itself being
        // z-relocated): a relocated source's placeholder carries none of its marker classes and this search
        // only ever inspects physical siblings, so that case is a documented gap, not fixed here.
        internal static VisualElement? FindPrevSiblingWithClass(VisualElement element, string cls, ReconcilerContext ctx)
        {
            if (!FiberZLayerCoordinator.TryGetLogicalPosition(ctx, element, out var parent, out var index))
            {
                return null;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                var sibling = parent!.ElementAt(i);
                if (sibling.ClassListContains(cls))
                {
                    return sibling;
                }
            }
            return null;
        }

        // One relational source binding. Owns its source resolution, event subscription, and applied state, so
        // several can coexist on one consuming element (the unnamed group/peer plus any number of named ones).
        // Handlers are instance methods, so each binding's delegates are distinct instances — registering and
        // unregistering stay symmetric and leak-free even when two bindings happen to resolve the same source.
        private sealed class Binding
        {
            private readonly StyleRelationalVariantManipulator _owner;
            private readonly bool _isPeer;
            private readonly string _name; // "" = unnamed
            private readonly string[] _hover;
            private readonly string[] _focus;
            private readonly string[] _focusWithin;
            private readonly string[] _active;
            private readonly string[] _checked;

            private RelationalVariantSignals _signals = null!;
            private bool _aHover, _aFocus, _aFocusWithin, _aActive, _aChecked;

            public Binding(StyleRelationalVariantManipulator owner, RelationalBindingConfig config)
            {
                _owner = owner;
                _isPeer = config.IsPeer;
                _name = config.Name ?? string.Empty;
                _hover = config.Hover;
                _focus = config.Focus;
                _focusWithin = config.FocusWithin;
                _active = config.Active;
                _checked = config.Checked;
            }

            // The class that marks this binding's source (see SourceClassFor).
            private string SourceClass => SourceClassFor(_isPeer, _name);

            private bool HasAnyState()
                => HasAny(_hover) || HasAny(_focus) || HasAny(_focusWithin) || HasAny(_active)
                    || (_isPeer && HasAny(_checked));

            public void Resolve(VisualElement target)
            {
                Unhook();

                // peer-checked is the one state seeded by Resolve itself (the initial-checked read below), not
                // purely by events. Clear any prior application up front so each Resolve re-derives it from
                // scratch against the (possibly changed) source.
                if (_aChecked) { _aChecked = false; Apply(_checked, false, StyleLayerPriority.PeerChecked); }

                var source = _isPeer
                    ? FindPrevSiblingWithClass(target, SourceClass, _owner._ctx)
                    : FindAncestorWithClass(target, SourceClass);
                if (source == null || !HasAnyState())
                {
                    return;
                }

                _signals ??= new RelationalVariantSignals(OnSignal);
                // registerChecked only for peer (group has no checked state). seedChecked reflects an
                // already-checked peer Toggle immediately (_aChecked was cleared above, so no double-apply).
                _signals.Hook(source, seedChecked: HasAny(_checked), registerChecked: _isPeer);
            }

            public void Unhook()
            {
                _signals?.Unhook();
            }

            public void ResetApplied()
            {
                if (_aHover) { _aHover = false; Apply(_hover, false, HoverPriority); }
                if (_aFocus) { _aFocus = false; Apply(_focus, false, FocusPriority); }
                if (_aFocusWithin) { _aFocusWithin = false; Apply(_focusWithin, false, FocusWithinPriority); }
                if (_aActive) { _aActive = false; Apply(_active, false, ActivePriority); }
                if (_aChecked) { _aChecked = false; Apply(_checked, false, StyleLayerPriority.PeerChecked); }
            }

            // Maps a detected relational signal edge to its payload at the per-state priority, deduping on the
            // applied-state bookkeeping so a repeated edge (e.g. a bubbling PointerOver, or a no-op checked
            // change) does not churn the payload.
            private void OnSignal(RelationalVariantSignal signal, bool on)
            {
                switch (signal)
                {
                    case RelationalVariantSignal.Hover:
                        if (on != _aHover) { _aHover = on; Apply(_hover, on, HoverPriority); }
                        break;
                    case RelationalVariantSignal.Focus:
                        if (on != _aFocus) { _aFocus = on; Apply(_focus, on, FocusPriority); }
                        break;
                    case RelationalVariantSignal.FocusWithin:
                        if (on != _aFocusWithin) { _aFocusWithin = on; Apply(_focusWithin, on, FocusWithinPriority); }
                        break;
                    case RelationalVariantSignal.Active:
                        if (on != _aActive) { _aActive = on; Apply(_active, on, ActivePriority); }
                        break;
                    case RelationalVariantSignal.Checked:
                        if (on != _aChecked) { _aChecked = on; Apply(_checked, on, StyleLayerPriority.PeerChecked); }
                        break;
                }
            }

            // Pass THIS binding as the stacked-gate owner so distinct named bindings never share a gate key.
            private void Apply(string[] payloads, bool on, int priority) => _owner.ApplyPayloads(this, payloads, on, priority);

            // Each sub-state gets its OWN priority so two active on the same property layer independently and
            // clearing one falls back to the other. Group and peer have distinct priority sets; the priority is
            // shared across names. Consequence (a documented limitation, not a layering case the variant spec defines):
            // two named bindings of the SAME state writing the SAME arbitrary-value property — group-hover/a:w-[10px]
            // group-hover/b:w-[20px] — share one (property, priority) layer, so the last to apply wins and the
            // first to clear drops it while the other source may still be active. Two bindings writing the SAME
            // plain USS class collide likewise (USS class toggling is not ref-counted) — the same pre-existing
            // behavior any two variants sharing a class already have (hover:bg-on focus:bg-on). Give distinct
            // names distinct payloads to avoid it. The stacked-inner case is kept independent via per-binding owners.
            private int HoverPriority => _isPeer ? StyleLayerPriority.PeerHover : StyleLayerPriority.GroupHover;
            private int FocusPriority => _isPeer ? StyleLayerPriority.PeerFocus : StyleLayerPriority.GroupFocus;
            private int FocusWithinPriority => _isPeer ? StyleLayerPriority.PeerFocusWithin : StyleLayerPriority.GroupFocusWithin;
            private int ActivePriority => _isPeer ? StyleLayerPriority.PeerActive : StyleLayerPriority.GroupActive;

            private static bool HasAny(string[] arr) => arr != null && arr.Length > 0;
        }
    }
}
