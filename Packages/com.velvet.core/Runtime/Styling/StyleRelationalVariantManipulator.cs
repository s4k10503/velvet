using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-(relation, name) payload set extracted from an element's class list, handed to the relational
    // manipulator. name is "" for the unnamed group/peer; a non-empty name is the named form
    // (group-hover/sidebar: → IsPeer false, Name "sidebar"). Payloads.Checked is only meaningful for peer
    // (group has no checked state); it stays empty for a group binding.
    internal readonly struct RelationalBindingConfig
    {
        public readonly bool IsPeer;
        public readonly string Name;
        public readonly VariantPayloads Payloads;
        // Each payload's rule place, aligned slot-for-slot with Payloads — see VariantDeclarations.
        public readonly VariantDeclarations Declarations;

        public RelationalBindingConfig(bool isPeer, string name, VariantPayloads payloads,
            VariantDeclarations declarations)
        {
            IsPeer = isPeer;
            Name = name ?? string.Empty;
            Payloads = payloads;
            Declarations = declarations;
        }
    }

    // Toggles relational variant payloads — the group/peer variants:
    // group-hover: / group-focus: / group-active: react to every ANCESTOR marked with the group class.
    // peer-hover: / peer-focus: / peer-active: / peer-checked: react to every preceding SIBLING marked
    // with the peer class. group-disabled: / peer-disabled: react to such a source being disabled.
    // The NAMED forms are supported too: group-hover/sidebar: reacts to ancestors marked
    // `group/sidebar`, peer-checked/email: to preceding siblings marked `peer/email`. One element
    // may consume several distinct named (and the unnamed) sources at once, so the manipulator holds a LIST of
    // bindings — one per (relation, name) — each resolving and subscribing to its own sources independently.
    // The manipulator lives on the consuming (child) element. Focus variants use FocusInEvent / FocusOutEvent
    // (focus-within semantics) since group/peer sources are commonly containers or inputs. Sources are
    // resolved on AttachToPanelEvent. Lifecycle mirrors the other variant manipulators
    // (ReconcilerContext.RelationalVariantManipulators).
    internal sealed class StyleRelationalVariantManipulator : Manipulator, IRelationalSettleTarget
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

        // Retargets every relational consumer's sources against the tree as it now stands. Runs at every
        // top-level pass end, so a source a render inserts, moves, marks or unmarks is picked up or released.
        // Rejected: a flag set only where a render creates, moves or re-marks a source. It saved the walk on a
        // pass that changes nothing relational and nothing else, so no case could hold it to anything.
        internal static void RetargetAll(ReconcilerContext ctx)
        {
            if (ctx.RelationalVariantManipulators.Count == 0 && ctx.StackedVariantManipulators.Count == 0)
            {
                return;
            }
            // Copied first: a retarget can light a payload, and a payload that is itself a variant adds to or
            // removes from the stacked registry (see ReconcilerContext.CopyStackedVariantsOn).
            foreach (var manipulator in new List<StyleRelationalVariantManipulator>(ctx.RelationalVariantManipulators.Values))
            {
                manipulator.Retarget();
            }
            foreach (var stacked in new List<StyleStackedVariantManipulator>(ctx.StackedVariantManipulators.Values))
            {
                stacked.RetargetRelational();
            }
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
            DropStackedVariants();
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
            DropStackedVariants();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        private void OnAttach(AttachToPanelEvent evt) => ResolveAll();

        // A binding is the owner its stacked payloads are keyed by (see ApplyPayloads), so the ones its bindings
        // gated go with them.
        private void DropStackedVariants()
        {
            foreach (var b in _bindings)
            {
                _ctx.DropStackedVariants(b);
            }
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            ResetApplied();
            UnhookAll();
        }

        private void Retarget()
        {
            if (target?.panel == null)
            {
                return;
            }
            foreach (var b in _bindings)
            {
                b.Retarget(target);
            }
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

        // Forwards a controlled write's synthetic checked edge to each binding; a binding that hooked a
        // different source, or none, drops it (RelationalVariantSignals.SettleChecked).
        public void SettleCheckedFromSource(VisualElement source, bool value)
        {
            foreach (var b in _bindings)
            {
                b.SettleChecked(source, value);
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
        private void ApplyPayloads(object owner, string[] payloads, int[] declarations, bool on, long priority)
            => StyleVariantPayload.Apply(target, payloads, on, priority, _ctx, owner, declarations);

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

        // Every source a relational binding reacts to, nearest first: each preceding declared sibling carrying
        // cls for a peer, each ancestor carrying it for a group.
        internal static void FindSources(VisualElement element, bool isPeer, string cls, ReconcilerContext ctx,
            List<VisualElement> into)
        {
            into.Clear();
            if (isPeer)
            {
                FindPrevSiblingsWithClass(element, cls, ctx, into);
                return;
            }
            for (var p = element.parent; p != null; p = p.parent)
            {
                if (p.ClassListContains(cls))
                {
                    into.Add(p);
                }
            }
        }

        // Walks declared siblings, not physical ones, on both ends: the consumer starts from the slot its
        // outermost node holds — a structural wrapper's, or a z-managed element's placeholder's
        // (FiberZLayerCoordinator.TryGetLogicalPosition) — and each sibling is read through its placeholder
        // and its wrapper to the element that declared the class.
        private static void FindPrevSiblingsWithClass(VisualElement element, string cls, ReconcilerContext ctx,
            List<VisualElement> into)
        {
            var physicalParent = element.parent;
            var outer = physicalParent != null
                && ReferenceEquals(ctx.WrapperToInnerMap.GetValueOrDefault(physicalParent), element)
                    ? physicalParent
                    : element;
            if (!FiberZLayerCoordinator.TryGetLogicalPosition(ctx, outer, out var parent, out var index))
            {
                return;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                var sibling = parent!.ElementAt(i);
                if (ctx.ZLayerPlaceholders.TryGetValue(sibling, out var relocated))
                {
                    sibling = relocated;
                }
                if (ctx.WrapperToInnerMap.TryGetValue(sibling, out var inner))
                {
                    sibling = inner;
                }
                if (sibling.ClassListContains(cls))
                {
                    into.Add(sibling);
                }
            }
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
            // Payloads, their rule places and their applied flags, one slot per
            // StyleVariantClass.RelationalState (see RelationalStateCount) — so every state this binding can
            // hook for is reached by iterating rather than by naming each one at each of the sites below.
            private readonly string[][] _payloads;
            private readonly int[][] _declarations;
            private readonly bool[] _applied;

            private RelationalSourceSet _signals = null!;
            private readonly List<VisualElement> _found = new();

            public Binding(StyleRelationalVariantManipulator owner, RelationalBindingConfig config)
            {
                _owner = owner;
                _isPeer = config.IsPeer;
                _name = config.Name ?? string.Empty;
                var count = StyleVariantClass.RelationalStateCount;
                _payloads = new string[count][];
                _declarations = new int[count][];
                _applied = new bool[count];
                for (var slot = 0; slot < count; slot++)
                {
                    var state = (StyleVariantClass.RelationalState)slot;
                    _payloads[slot] = PayloadFor(config.Payloads, state);
                    _declarations[slot] = DeclarationFor(config.Declarations, state);
                }
            }

            // The class that marks this binding's source (see SourceClassFor).
            private string SourceClass => SourceClassFor(_isPeer, _name);

            private bool HasAnyState()
            {
                for (var slot = 0; slot < _payloads.Length; slot++)
                {
                    if (_payloads[slot].Length > 0 && DrivesState((StyleVariantClass.RelationalState)slot))
                    {
                        return true;
                    }
                }
                return false;
            }

            // Checked is the one state with no group spelling (StyleVariantClass lists peer-checked and no
            // group-checked), so a group binding hooks no source for it even when handed a checked payload.
            private bool DrivesState(StyleVariantClass.RelationalState state)
                => _isPeer || state != StyleVariantClass.RelationalState.Checked;

            public void Resolve(VisualElement target)
            {
                Unhook();

                Retarget(target);
            }

            // Checked is registered and seeded only for peer (group has no checked state); a source already
            // hooked keeps the state it holds.
            public void Retarget(VisualElement target)
            {
                if (!HasAnyState())
                {
                    return;
                }
                FindSources(target, _isPeer, SourceClass, _owner._ctx, _found);
                _signals ??= new RelationalSourceSet(_owner._ctx, OnSignal);
                var trackDisabled = _payloads[(int)StyleVariantClass.RelationalState.Disabled].Length > 0;
                _signals.Retarget(_found, seedChecked: _isPeer, registerChecked: _isPeer, trackDisabled);
            }

            public void Unhook()
            {
                _signals?.Unhook();
            }

            public void SettleChecked(VisualElement source, bool value)
            {
                _signals?.SettleChecked(source, value);
            }

            public void ResetApplied()
            {
                for (var slot = 0; slot < _applied.Length; slot++)
                {
                    if (_applied[slot]) { _applied[slot] = false; Apply(slot, false); }
                }
            }

            // Maps a detected relational signal edge to its payload at the per-state priority, deduping on the
            // applied-state bookkeeping so a repeated edge (e.g. a bubbling PointerOver, or a no-op checked
            // change) does not churn the payload.
            private void OnSignal(RelationalVariantSignal signal, bool on)
            {
                var slot = (int)StyleVariantClass.StateOf(signal);
                if (on == _applied[slot])
                {
                    return;
                }
                _applied[slot] = on;
                Apply(slot, on);
            }

            // Pass THIS binding as the stacked-gate owner so distinct named bindings never share a gate key.
            private void Apply(int slot, bool on)
                => _owner.ApplyPayloads(this, _payloads[slot], _declarations[slot], on,
                    PriorityFor((StyleVariantClass.RelationalState)slot));

            // Where a relational state's payloads and their rule places sit in the pair, which is
            // shared with the state-variant manipulator: that manipulator's third slot is focus-VISIBLE, a
            // state no relational source has, so relational focus-within rides it. These two switches and
            // PriorityFor below carry no discard arm — see the remarks on StyleVariantKind.
#pragma warning disable CS8524 // no discard arm — see the remarks on StyleVariantKind
            private static string[] PayloadFor(VariantPayloads payloads, StyleVariantClass.RelationalState state)
                => (state switch
                {
                    StyleVariantClass.RelationalState.Hover => payloads.Hover,
                    StyleVariantClass.RelationalState.Focus => payloads.Focus,
                    StyleVariantClass.RelationalState.FocusWithin => payloads.FocusVisible,
                    StyleVariantClass.RelationalState.Active => payloads.Active,
                    StyleVariantClass.RelationalState.Checked => payloads.Checked,
                    StyleVariantClass.RelationalState.Disabled => payloads.Disabled,
                }) ?? Array.Empty<string>();

            private static int[] DeclarationFor(VariantDeclarations declarations, StyleVariantClass.RelationalState state)
                => (state switch
                {
                    StyleVariantClass.RelationalState.Hover => declarations.Hover,
                    StyleVariantClass.RelationalState.Focus => declarations.Focus,
                    StyleVariantClass.RelationalState.FocusWithin => declarations.FocusVisible,
                    StyleVariantClass.RelationalState.Active => declarations.Active,
                    StyleVariantClass.RelationalState.Checked => declarations.Checked,
                    StyleVariantClass.RelationalState.Disabled => declarations.Disabled,
                }) ?? Array.Empty<int>();

            // Each sub-state gets its OWN priority so two active on the same property layer independently and
            // clearing one falls back to the other. Group and peer have distinct priority sets; the priority is
            // shared across names. Consequence (a documented limitation, not a layering case the variant spec defines):
            // two named bindings of the SAME state writing the SAME arbitrary-value property — group-hover/a:w-[10px]
            // group-hover/b:w-[20px] — share one (property, priority) layer, so the last to apply wins and the
            // first to clear drops it while the other source may still be active. Two bindings writing the SAME
            // plain USS class collide likewise (USS class toggling is not ref-counted) — the same pre-existing
            // behavior any two variants sharing a class already have (hover:bg-on focus:bg-on). Give distinct
            // names distinct payloads to avoid it. The stacked-inner case is kept independent via per-binding owners.
            private long PriorityFor(StyleVariantClass.RelationalState state) => state switch
            {
                StyleVariantClass.RelationalState.Hover
                    => _isPeer ? StyleLayerPriority.PeerHover : StyleLayerPriority.GroupHover,
                StyleVariantClass.RelationalState.Focus
                    => _isPeer ? StyleLayerPriority.PeerFocus : StyleLayerPriority.GroupFocus,
                StyleVariantClass.RelationalState.FocusWithin
                    => _isPeer ? StyleLayerPriority.PeerFocusWithin : StyleLayerPriority.GroupFocusWithin,
                StyleVariantClass.RelationalState.Active
                    => _isPeer ? StyleLayerPriority.PeerActive : StyleLayerPriority.GroupActive,
                StyleVariantClass.RelationalState.Checked => StyleLayerPriority.PeerChecked,
                StyleVariantClass.RelationalState.Disabled
                    => _isPeer ? StyleLayerPriority.PeerDisabled : StyleLayerPriority.GroupDisabled,
            };
#pragma warning restore CS8524
        }
    }
}
