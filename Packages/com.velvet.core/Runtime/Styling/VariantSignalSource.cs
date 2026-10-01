#nullable enable
using System;
using System.Collections.Generic;
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
    // sweeps (drag release, focus-loss fallback, controlled value write) enumerate ONE shape instead of
    // per-type calls.
    internal interface IVariantSettleTarget
    {
        void SettleRelease();
        void SettleFocusLoss();
        void SettleChecked(bool value);
    }

    // The synthetic-settle surface a relational (group-/peer-) variant consumer implements. Kept apart from
    // IVariantSettleTarget because the two are keyed opposite ways: an element-local consumer is registered
    // against the element that was written, while a relational one is registered against the element that
    // CONSUMES the payload and only knows its source from the inside. So the source travels as an argument
    // and each consumer answers for itself.
    internal interface IRelationalSettleTarget
    {
        void SettleCheckedFromSource(VisualElement source, bool value);
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
            foreach (var m in SnapshotStacked(ctx))
            {
                if (m.target == element)
                {
                    action(m);
                }
            }
        }

        // The stacked registry is copied before either sweep walks it, never enumerated live: settling a
        // consumer applies its payload, and a payload that is itself a variant re-enters
        // ReconcilerContext.GateStackedVariant, which adds to or removes from this very dictionary. Walking it
        // live threw "Collection was modified" out of the reconcile; StackedVariantEdgeTests pins the case.
        // Selecting by the manipulator's own target rather than by its key's is the same set — the gate adds
        // each manipulator to the element its key names.
        private static StyleStackedVariantManipulator[] SnapshotStacked(ReconcilerContext ctx)
        {
            var count = ctx.StackedVariantManipulators.Count;
            if (count == 0)
            {
                return Array.Empty<StyleStackedVariantManipulator>();
            }
            var snapshot = new StyleStackedVariantManipulator[count];
            ctx.StackedVariantManipulators.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        // Offers a checked settle raised on source to every relational consumer in the context. Both registries
        // are keyed by the consuming element, so there is nothing to look the source up in; each consumer
        // compares it against the source it hooked, which costs one reference comparison per binding and walks
        // no part of the tree. The caller gates this on a bool control whose controlled value actually changed,
        // which is what keeps the scan off the ordinary render path.
        public static void SettleCheckedFromSource(VisualElement source, ReconcilerContext ctx, bool value)
        {
            foreach (var kv in ctx.RelationalVariantManipulators)
            {
                kv.Value.SettleCheckedFromSource(source, value);
            }
            foreach (var m in SnapshotStacked(ctx))
            {
                m.SettleCheckedFromSource(source, value);
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
        // keeps the original registration footprint). When seedChecked is true and the target reports itself
        // already checked, the initial Checked edge is emitted here (ChangeEvent fires only on change, so a
        // mounted-true value must be read at hook time). The read admits every control that reports a bool,
        // not only Toggle: the registration above is untyped, so a narrower read would disagree with the
        // change path about which controls drive checked: — CheckedVariantBehaviorTests pins the case.
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
                if (seedChecked && target is INotifyValueChanged<bool> { value: true })
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

        // Observes a synthetic checked change, raised by the writer of a value the control takes without
        // notification (see FiberNodePatcher.RaiseCheckedSignal). Gated on the registration choice so a
        // consumer that opted out of the checked path never sees a Checked edge.
        public void SettleChecked(bool value)
        {
            if (_target != null && _registerChecked)
            {
                _emit(VariantSignal.Checked, value);
            }
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
            // This is the user-driven half; a value written by the prop path raises no ChangeEvent and
            // arrives through SettleChecked instead.
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
        Disabled,
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
        private DisabledVariantSignal? _disabled;

        public RelationalVariantSignals(Action<RelationalVariantSignal, bool> emit) => _emit = emit;

        // Registers the detection callbacks on the source. registerChecked enables the peer-checked path
        // (ChangeEvent + the initial already-checked read via seedChecked, since ChangeEvent fires only on
        // change); group bindings pass false. The seed reads any control reporting a bool, for the reason
        // ElementLocalVariantSignals.Hook gives.
        public void Hook(VisualElement source, bool seedChecked, bool registerChecked, bool trackDisabled)
        {
            _source = source;
            _registerChecked = registerChecked;
            if (trackDisabled)
            {
                _disabled ??= new DisabledVariantSignal(on => _emit(RelationalVariantSignal.Disabled, on));
                _disabled.Hook(source);
            }

            source.RegisterCallback<PointerOverEvent>(OnPointerOver);
            source.RegisterCallback<PointerOutEvent>(OnPointerOut);
            source.RegisterCallback<PointerDownEvent>(OnPointerDown);
            source.RegisterCallback<PointerUpEvent>(OnPointerUp);
            source.RegisterCallback<FocusInEvent>(OnFocusIn);
            source.RegisterCallback<FocusOutEvent>(OnFocusOut);

            if (registerChecked)
            {
                source.RegisterCallback<ChangeEvent<bool>>(OnChange);
                if (seedChecked && source is INotifyValueChanged<bool> { value: true })
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
            _disabled?.Unhook();

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

        // Observes a synthetic checked change on a source, raised by the writer of a value the control takes
        // without notification (the element-local counterpart is ElementLocalVariantSignals.SettleChecked).
        // The settle is offered to every relational consumer rather than to the ones registered against the
        // written element, because no source-to-consumer index exists — so the element it was raised on is
        // passed in and compared against the one THIS binding hooked, which is the same own-source filter
        // OnChange applies to a bubbling event.
        public void SettleChecked(VisualElement source, bool value)
        {
            if (_registerChecked && ReferenceEquals(_source, source))
            {
                _emit(RelationalVariantSignal.Checked, value);
            }
        }
    }
    // Hooks every source a relational binding matched and reports a state lit while ANY of them holds it:
    // `.peer:hover ~ x` matches whichever preceding peer is hovered, and `.group:hover x` whichever ancestor
    // group is. An on edge is forwarded as it arrives and the consumer dedups it; an off edge is forwarded
    // only once no hooked source still holds the state.
    //
    // The set follows the tree the way a selector does: Retarget hooks sources that joined and releases the
    // ones that left, keeping each source that stayed with the state it holds, and a source the cleaner
    // takes out of the tree is released at once (DropDeparted), before its element can be pooled and rented
    // elsewhere. ReconcilerContext.RelationalVariantSources indexes the sets by the source elements they hook.
    internal sealed class RelationalSourceSet
    {
        private readonly ReconcilerContext _ctx;
        private readonly Action<RelationalVariantSignal, bool> _emit;
        private readonly List<Source> _sources = new();
        private readonly int[] _holding = new int[RelationalSignalCount];

        private static readonly int RelationalSignalCount = Enum.GetValues(typeof(RelationalVariantSignal)).Length;

        public RelationalSourceSet(ReconcilerContext ctx, Action<RelationalVariantSignal, bool> emit)
        {
            _ctx = ctx;
            _emit = emit;
        }

        // Hooks every element of sources not hooked yet and releases every hooked one sources no longer names.
        public void Retarget(List<VisualElement> sources, bool seedChecked, bool registerChecked, bool trackDisabled)
        {
            for (var i = _sources.Count - 1; i >= 0; i--)
            {
                if (!sources.Contains(_sources[i].Element))
                {
                    Release(i);
                }
            }
            foreach (var element in sources)
            {
                if (IndexOf(element) < 0)
                {
                    var source = new Source(this, element);
                    _sources.Add(source);
                    Index(element).Add(this);
                    source.Signals.Hook(element, seedChecked, registerChecked, trackDisabled);
                }
            }
        }

        // Unhooks every source without reporting an edge: the consumer resets its own applied state around this.
        public void Unhook()
        {
            foreach (var source in _sources)
            {
                source.Signals.Unhook();
                Unindex(source.Element);
            }
            _sources.Clear();
            Array.Clear(_holding, 0, _holding.Length);
        }

        public void SettleChecked(VisualElement source, bool value)
        {
            foreach (var hooked in _sources)
            {
                hooked.Signals.SettleChecked(source, value);
            }
        }

        // Releases element from every set that hooks it, reporting the off edges only it was holding.
        public static void DropDeparted(ReconcilerContext ctx, VisualElement element)
        {
            if (!ctx.RelationalVariantSources.TryGetValue(element, out var sets))
            {
                return;
            }
            foreach (var set in sets.ToArray())
            {
                set.Release(set.IndexOf(element));
            }
        }

        private int IndexOf(VisualElement element)
        {
            for (var i = 0; i < _sources.Count; i++)
            {
                if (ReferenceEquals(_sources[i].Element, element))
                {
                    return i;
                }
            }
            return -1;
        }

        private void Release(int index)
        {
            var source = _sources[index];
            _sources.RemoveAt(index);
            source.Signals.Unhook();
            Unindex(source.Element);
            for (var slot = 0; slot < RelationalSignalCount; slot++)
            {
                if (source.Holding[slot] && --_holding[slot] == 0)
                {
                    _emit((RelationalVariantSignal)slot, false);
                }
            }
        }

        private List<RelationalSourceSet> Index(VisualElement element)
        {
            if (!_ctx.RelationalVariantSources.TryGetValue(element, out var sets))
            {
                sets = new List<RelationalSourceSet>();
                _ctx.RelationalVariantSources[element] = sets;
            }
            return sets;
        }

        private void Unindex(VisualElement element)
        {
            if (!_ctx.RelationalVariantSources.TryGetValue(element, out var sets))
            {
                return;
            }
            sets.Remove(this);
            if (sets.Count == 0)
            {
                _ctx.RelationalVariantSources.Remove(element);
            }
        }

        private void OnSourceSignal(Source source, RelationalVariantSignal signal, bool on)
        {
            var slot = (int)signal;
            if (source.Holding[slot] != on)
            {
                source.Holding[slot] = on;
                _holding[slot] += on ? 1 : -1;
            }
            if (on || _holding[slot] == 0)
            {
                _emit(signal, on);
            }
        }

        private sealed class Source
        {
            public readonly bool[] Holding = new bool[RelationalSignalCount];
            public readonly RelationalVariantSignals Signals;
            public readonly VisualElement Element;

            public Source(RelationalSourceSet set, VisualElement element)
            {
                Element = element;
                Signals = new RelationalVariantSignals((signal, on) => set.OnSourceSignal(this, signal, on));
            }
        }
    }

    // Reports each edge of the target's :disabled state — the target or an ancestor has enabledSelf off — to
    // a callback, for the disabled: variant and for a stacked disabled: inner. Level-based: the state is read
    // at Hook and again at every attach, since the ancestor chain it watches is only known once the target is
    // in its final place.
    internal sealed class DisabledVariantSignal
    {
        private readonly Action<bool> _emit;
        private readonly object? _onWrite;
        private readonly List<VisualElement> _chain = new();
        private VisualElement? _target; // non-null only while hooked
        private bool _disabled;

        public DisabledVariantSignal(Action<bool> emit)
        {
            _emit = emit;
            _onWrite = EnabledSelfWrites.CreateCallback(Evaluate);
        }

        public bool IsHooked => _target != null;

        public void Hook(VisualElement target)
        {
            _target = target;
            _disabled = false;
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            HookChain();
            Evaluate();
        }

        // Emits nothing: the consumer clears its own applied state when it unhooks.
        public void Unhook()
        {
            if (_target == null)
            {
                return;
            }

            UnhookChain();
            _target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            _target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            _target = null;
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            // MUTANT_SURVIVES(equivalent, line removed): every attach but the first follows a detach, which has
            // already emptied the chain; at the first, re-registering a callback an element already holds is a
            // no-op, and Unhook unregistering it twice is one too.
            UnhookChain();
            HookChain();
            Evaluate();
        }

        private void OnDetach(DetachFromPanelEvent evt) => UnhookChain();

        // The write that disables the target can land on any ancestor, and is announced on that ancestor only.
        private void HookChain()
        {
            for (var element = _target; element != null; element = element.hierarchy.parent)
            {
                EnabledSelfWrites.Register(element, _onWrite);
                _chain.Add(element);
            }
        }

        private void UnhookChain()
        {
            foreach (var element in _chain)
            {
                EnabledSelfWrites.Unregister(element, _onWrite);
            }
            // MUTANT_SURVIVES(equivalent): an entry left behind is only ever unregistered again, which is a
            // no-op for a callback the element no longer holds.
            _chain.Clear();
        }

        // Reads enabledSelf up the chain instead of the target's enabledInHierarchy: outside an event dispatch
        // UI Toolkit announces an enabledSelf write before it propagates the new state to descendants, so the
        // target's own flag is still the old one when the announcement arrives. DisabledVariantTests'
        // Given_AnAncestorWrittenOutsideADispatch case holds that order.
        private void Evaluate()
        {
            if (_target == null)
            {
                return;
            }

            var disabled = false;
            for (var element = _target; element != null && !disabled; element = element.hierarchy.parent)
            {
                disabled = !element.enabledSelf;
            }

            if (disabled == _disabled)
            {
                return;
            }
            _disabled = disabled;
            _emit(disabled);
        }
    }

    // UI Toolkit announces an enabledSelf write through PropertyChangedEvent, an internal event. The type is reached
    // by name; DisabledVariantTests' Given_AnAncestorWrittenOutsideADispatch case fails where the engine stops
    // sending it.
    internal static class EnabledSelfWrites
    {
        private interface IRegistrar
        {
            object Create(Action onWrite);
            void Register(VisualElement element, object callback);
            void Unregister(VisualElement element, object callback);
        }

        private sealed class Registrar<TEvent> : IRegistrar where TEvent : EventBase<TEvent>, new()
        {
            public object Create(Action onWrite) => new EventCallback<TEvent>(_ => onWrite());

            // IncludeDisabled keeps delivery from depending on whether the engine skips a disabled target for
            // this event: the element it announces is often the one just disabled.
            public void Register(VisualElement element, object callback)
                => element.RegisterCallback((EventCallback<TEvent>)callback, CallbackOptions.IncludeDisabled);

            public void Unregister(VisualElement element, object callback)
                => element.UnregisterCallback((EventCallback<TEvent>)callback, CallbackOptions.IncludeDisabled);
        }

        private static readonly IRegistrar? s_registrar = CreateRegistrar();

        private static IRegistrar? CreateRegistrar()
        {
            var eventType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.PropertyChangedEvent");
            return eventType == null
                ? null
                : (IRegistrar)Activator.CreateInstance(typeof(Registrar<>).MakeGenericType(eventType));
        }

        public static object? CreateCallback(Action onWrite) => s_registrar?.Create(onWrite);

        public static void Register(VisualElement element, object? callback)
        {
            if (callback != null) s_registrar?.Register(element, callback);
        }

        public static void Unregister(VisualElement element, object? callback)
        {
            if (callback != null) s_registrar?.Unregister(element, callback);
        }
    }
}
