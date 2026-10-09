#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Uses an "unbind all → bind all" diff strategy. Event count is typically 1-3, so this is lightweight enough.
    // Unity internally reuses functor pools, so GC pressure is also low.
    internal sealed class FiberEventBindingManager
    {
        private readonly Dictionary<VisualElement, List<Action>> _unbindActions = new();
        private readonly Dictionary<VisualElement, List<Delegate>> _boundDelegates = new();
        // Mirrors _boundDelegates, keyed by the same element, but retains the typed FiberEventBinding
        // wrapper instead of the bare Handler delegate. Native UI Toolkit dispatch never needs this —
        // RegisterCallback<T> already knows T from the generic call site. It exists solely for
        // FiberCrossPanelEventDispatcher.TryInvoke, which receives an already-constructed EventBase
        // instance at a point where the native dispatcher is NOT involved (an event that bubbled to a
        // portal/world-space host panel's root and is being carried across the panel boundary to the
        // logical ancestor chain) and must resolve "does this element have a handler for THIS runtime
        // event type" without any generic type parameter to dispatch on.
        private readonly Dictionary<VisualElement, List<FiberEventBinding>> _bindingsByElement = new();

        // The click every ClickedEventBinding on a button hands its handler, replaced at the start of each
        // click by the callback BindClickedEvent puts ahead of them on Button.clicked.
        private readonly Dictionary<Button, ClickedEventSlot> _clickedEventSlots = new();

        private sealed class ClickedEventSlot
        {
            public ClickedEvent Current = new();
        }

        // The portal bridges FiberCrossPanelEventDispatcher.AttachBridge put on their anchors, one per anchor
        // however many hosts and portal targets attach one there, each held until the last of them releases it.
        private readonly Dictionary<VisualElement, PortalBridge> _bridges = new();

        private sealed class PortalBridge
        {
            // Run ahead of the anchor's own bubble bindings; answers whether an ancestor it ran stopped
            // propagation, which keeps the anchor's own from running.
            public Func<EventBase, bool> BubblePrelude = null!;

            // Moves the bridge's capture listeners behind the anchor's own capture bindings once those are
            // registered again.
            public Action Rebound = null!;

            public Action Detach = null!;
            public int Holds = 1;
        }

        // Answers whether anchor already carries a bridge, taking one more hold on it where it does.
        internal bool HoldBridge(VisualElement anchor)
        {
            var held = _bridges.TryGetValue(anchor, out var bridge);
            if (held) bridge!.Holds++;
            return held;
        }

        internal void SetBridge(VisualElement anchor, Func<EventBase, bool> bubblePrelude, Action rebound, Action detach) =>
            _bridges[anchor] = new PortalBridge { BubblePrelude = bubblePrelude, Rebound = rebound, Detach = detach };

        internal void ReleaseBridge(VisualElement anchor)
        {
            if (!_bridges.TryGetValue(anchor, out var bridge)) return;
            bridge.Holds--;
            if (bridge.Holds > 0) return;
            _bridges.Remove(anchor);
            bridge.Detach();
        }

        internal bool IsBridgeAnchor(VisualElement element) => _bridges.ContainsKey(element);

        // Called once an element's bindings were registered again after UnbindAll.
        internal void Rebound(VisualElement element)
        {
            if (_bridges.TryGetValue(element, out var bridge)) bridge.Rebound();
        }

        private bool StoppedByBubblePrelude(VisualElement element, EventBase evt)
        {
            if (!_bridges.TryGetValue(element, out var bridge)) return false;
            return bridge.BubblePrelude(evt);
        }

        // The owning context's batch scheduler. Used to flush the immediate batch synchronously at the end of a
        // discrete event handler so the UI updates before the next frame. Null when constructed without one (isolated unit
        // tests of binding registration): the discrete flag is still bracketed, but no synchronous flush runs.
        private readonly FiberBatchScheduler? _batchScheduler;

        internal FiberEventBindingManager(FiberBatchScheduler? batchScheduler = null)
        {
            _batchScheduler = batchScheduler;
        }

        // Skips re-registration if the same delegate is already registered in the same phase.
        public void Bind(VisualElement element, FiberEventBinding binding)
        {
            if (element == null || binding == null)
            {
                return;
            }

            var newDelegate = GetDelegate(binding);
            if (newDelegate != null && _boundDelegates.TryGetValue(element, out var existingDelegates))
            {
                // Holds an entry for every delegate the list above holds, at the same index.
                _bindingsByElement.TryGetValue(element, out var existingBindings);
                for (var i = 0; i < existingDelegates.Count; i++)
                {
                    if (existingDelegates[i] == newDelegate && IsCapture(existingBindings![i]) == IsCapture(binding))
                    {
                        return;
                    }
                }
            }

            if (!_unbindActions.TryGetValue(element, out var actions))
            {
                actions = new List<Action>();
                _unbindActions[element] = actions;
            }

            if (!_boundDelegates.TryGetValue(element, out var delegates))
            {
                delegates = new List<Delegate>();
                _boundDelegates[element] = delegates;
            }

            if (newDelegate != null)
            {
                delegates.Add(newDelegate);
                if (!_bindingsByElement.TryGetValue(element, out var typedBindings))
                {
                    typedBindings = new List<FiberEventBinding>();
                    _bindingsByElement[element] = typedBindings;
                }
                typedBindings.Add(binding);
            }

            RegisterFieldBinding(actions, element, binding);
        }

        // The bindings that ride an element-specific hook (Button.clicked, INotifyValueChanged<T>) rather
        // than the event dispatcher, so each is gated on the element implementing that hook. A binding
        // whose element does not falls through to RegisterEventBinding, which matches none of its cases
        // either, and registers nothing.
        private void RegisterFieldBinding(List<Action> actions, VisualElement element, FiberEventBinding binding)
        {
            switch (binding)
            {
                case ClickedBinding clicked when element is Button button:
                {
                    var handler = clicked.Handler;
                    Action wrapped = () => RunDiscrete(handler);
                    button.clicked += wrapped;
                    actions.Add(() => button.clicked -= wrapped);
                    break;
                }
                case ClickedEventBinding clickedEvent when element is Button button:
                    BindClickedEvent(actions, button, clickedEvent.Handler);
                    break;
                case ChangeEventBinding<float> floatChange when element is INotifyValueChanged<float> floatField:
                    BindDiscreteValueChanged(actions, floatField, floatChange.Handler);
                    break;
                case ChangeEventBinding<bool> boolChange when element is INotifyValueChanged<bool> boolField:
                    BindDiscreteValueChanged(actions, boolField, boolChange.Handler);
                    break;
                case ChangeEventBinding<string> stringChange when element is INotifyValueChanged<string> stringField:
                    BindDiscreteValueChanged(actions, stringField, stringChange.Handler);
                    break;
                case ChangeEventBinding<int> intChange when element is INotifyValueChanged<int> intField:
                    BindDiscreteValueChanged(actions, intField, intChange.Handler);
                    break;
                default:
                    RegisterEventBinding(actions, element, binding);
                    break;
            }
        }

        private void RegisterEventBinding(List<Action> actions, VisualElement element, FiberEventBinding binding)
        {
            switch (binding)
            {
                // Discrete user-input events (a distinct, atomic interaction): a hook update they trigger takes the
                // Urgent lane and the immediate batch flushes synchronously when the handler returns.
                case PointerDownBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case PointerUpBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case KeyDownBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case KeyUpBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case FocusInBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case FocusOutBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case FocusBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                case BlurBinding b: BindDiscreteCallback(actions, element, b.Handler, b.Capture); break;
                // Continuous events (a high-frequency stream such as pointer move): updates batch to the next frame like a
                // Normal-lane render; no synchronous flush.
                case PointerMoveBinding b: BindCallback(actions, element, b.Handler, b.Capture); break;
                case PointerEnterBinding b: BindBubbleCallback(actions, element, b.Handler); break;
                case PointerLeaveBinding b: BindBubbleCallback(actions, element, b.Handler); break;
                case WheelBinding b: BindCallback(actions, element, b.Handler, b.Capture); break;
                case GeometryChangedBinding b: BindBubbleCallback(actions, element, b.Handler); break;
            }
        }

        public void UnbindAll(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            if (!_unbindActions.TryGetValue(element, out var actions))
            {
                return;
            }

            foreach (var unbind in actions)
            {
                unbind?.Invoke();
            }

            actions.Clear();
            _unbindActions.Remove(element);
            _boundDelegates.Remove(element);
            _bindingsByElement.Remove(element);
        }

        // Order-sensitive: assumes the insertion order of _boundDelegates matches newEvents. Called where
        // Bind invocation order guarantees this, so it reads newEvents the way Bind read them, passing over
        // an entry Bind would skip as a repeat of an earlier one.
        public bool HasSameBindings(VisualElement element, FiberEventBinding[] newEvents)
        {
            if (element == null)
            {
                return newEvents == null || newEvents.Length == 0;
            }

            if (!_boundDelegates.TryGetValue(element, out var delegates))
            {
                return newEvents == null || newEvents.Length == 0;
            }

            if (newEvents == null || newEvents.Length == 0)
            {
                return delegates.Count == 0;
            }

            _bindingsByElement.TryGetValue(element, out var bindings);
            var bound = 0;
            for (var i = 0; i < newEvents.Length; i++)
            {
                var newDelegate = GetDelegate(newEvents[i]);
                // Unknown binding types where GetDelegate returns null are always treated as mismatch (fall back to rebind on the safe side).
                if (newDelegate == null)
                {
                    return false;
                }

                var capture = IsCapture(newEvents[i]);
                if (RepeatsEarlier(newEvents, i, newDelegate, capture))
                {
                    continue;
                }

                if (bound >= delegates.Count || delegates[bound] != newDelegate
                    || IsCapture(bindings![bound]) != capture)
                {
                    return false;
                }
                bound++;
            }

            return bound == delegates.Count;
        }

        private static bool RepeatsEarlier(FiberEventBinding[] events, int index, Delegate handler, bool capture)
        {
            var repeats = false;
            for (var i = 0; i < index && !repeats; i++)
            {
                repeats = GetDelegate(events[i]) == handler && IsCapture(events[i]) == capture;
            }
            return repeats;
        }

        private static bool IsCapture(FiberEventBinding binding) =>
            binding is FiberDispatchedEventBinding { Capture: true };

        public void Clear()
        {
            foreach (var kvp in _unbindActions)
            {
                foreach (var unbind in kvp.Value)
                {
                    unbind?.Invoke();
                }
            }

            _unbindActions.Clear();
            _boundDelegates.Clear();
            _bindingsByElement.Clear();
        }

        // Invoked by FiberCrossPanelEventDispatcher (see that class for the walks) for an element no native
        // dispatch reaches: a logical ancestor off the event target's physical path, or an element the layer
        // router picked and its logical ancestors. Resolves element's own binding matching evt's
        // runtime type and invokes its raw Handler directly, bypassing UI Toolkit's dispatcher entirely:
        // native RegisterCallback<T> plumbing never runs here, since element may not even share a panel
        // with evt's original target. A capture walk runs only capture bindings, and a bubble walk every
        // other binding. Returns true when a matching binding was invoked (informational only; the caller's
        // walk continues regardless — a miss here does not stop propagation up the logical chain).
        internal bool TryInvokeSynthetic(VisualElement element, EventBase evt, bool capture)
        {
            if (element == null || evt == null || !_bindingsByElement.TryGetValue(element, out var bindings))
            {
                return false;
            }

            var invoked = false;
            ClickedEvent? click = null;
            foreach (var binding in bindings)
            {
                if (IsCapture(binding) != capture) continue;
                InvokeSyntheticField(element, binding, evt, ref click);
                if (InvokeSyntheticDiscrete(binding, evt) || InvokeSyntheticContinuous(binding, evt))
                {
                    invoked = true;
                }
            }
            return invoked;
        }

        // ClickedBinding answers ClickEvent and ChangeEventBinding<T> answers ChangeEvent<T>, each only on the
        // element kind RegisterFieldBinding binds it to, and a click only on an enabled Button, as Clickable
        // and a disabled DOM button refuse one.
        private void InvokeSyntheticField(VisualElement element, FiberEventBinding binding, EventBase evt,
            ref ClickedEvent? click)
        {
            switch (binding)
            {
                case ClickedBinding b when element is Button && evt is ClickEvent && element.enabledInHierarchy:
                    RunDiscrete(b.Handler);
                    break;
                case ClickedEventBinding b when element is Button && evt is ClickEvent && element.enabledInHierarchy:
                {
                    var shared = click ??= new ClickedEvent();
                    RunDiscrete(() => b.Handler?.Invoke(shared));
                    break;
                }
                case ChangeEventBinding<float> b when element is INotifyValueChanged<float>:
                    InvokeSyntheticChange(b.Handler, evt);
                    break;
                case ChangeEventBinding<bool> b when element is INotifyValueChanged<bool>:
                    InvokeSyntheticChange(b.Handler, evt);
                    break;
                case ChangeEventBinding<string> b when element is INotifyValueChanged<string>:
                    InvokeSyntheticChange(b.Handler, evt);
                    break;
                case ChangeEventBinding<int> b when element is INotifyValueChanged<int>:
                    InvokeSyntheticChange(b.Handler, evt);
                    break;
            }
        }

        private void InvokeSyntheticChange<T>(Action<T>? handler, EventBase evt)
        {
            if (evt is ChangeEvent<T> change)
            {
                RunDiscrete(() => handler?.Invoke(change.newValue));
            }
        }

        private bool InvokeSyntheticDiscrete(FiberEventBinding binding, EventBase evt)
        {
            switch (binding)
            {
                case PointerDownBinding b when evt is PointerDownEvent pe:
                    RunDiscrete(() => b.Handler?.Invoke(pe));
                    return true;
                case PointerUpBinding b when evt is PointerUpEvent pe:
                    RunDiscrete(() => b.Handler?.Invoke(pe));
                    return true;
                case KeyDownBinding b when evt is KeyDownEvent ke:
                    RunDiscrete(() => b.Handler?.Invoke(ke));
                    return true;
                case KeyUpBinding b when evt is KeyUpEvent ke:
                    RunDiscrete(() => b.Handler?.Invoke(ke));
                    return true;
                case FocusInBinding b when evt is FocusInEvent fe:
                    RunDiscrete(() => b.Handler?.Invoke(fe));
                    return true;
                case FocusOutBinding b when evt is FocusOutEvent fe:
                    RunDiscrete(() => b.Handler?.Invoke(fe));
                    return true;
                case FocusBinding b when evt is FocusEvent fe:
                    RunDiscrete(() => b.Handler?.Invoke(fe));
                    return true;
                case BlurBinding b when evt is BlurEvent fe:
                    RunDiscrete(() => b.Handler?.Invoke(fe));
                    return true;
                default:
                    return false;
            }
        }

        private static bool InvokeSyntheticContinuous(FiberEventBinding binding, EventBase evt)
        {
            switch (binding)
            {
                case PointerMoveBinding b when evt is PointerMoveEvent pe:
                    b.Handler?.Invoke(pe);
                    return true;
                case WheelBinding b when evt is WheelEvent we:
                    b.Handler?.Invoke(we);
                    return true;
                default:
                    return false;
            }
        }

        private void BindCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler,
            bool capture)
            where T : EventBase<T>, new()
        {
            if (!capture)
            {
                BindBubbleCallback(actions, element, handler);
                return;
            }
            element.RegisterCallback(handler, TrickleDown.TrickleDown);
            actions.Add(() => element.UnregisterCallback(handler, TrickleDown.TrickleDown));
        }

        private void BindBubbleCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler)
            where T : EventBase<T>, new()
        {
            EventCallback<T> wrapped = evt =>
            {
                if (StoppedByBubblePrelude(element, evt)) return;
                handler?.Invoke(evt);
            };
            element.RegisterCallback(wrapped);
            actions.Add(() => element.UnregisterCallback(wrapped));
        }

        // The callback registered first runs first on Button.clicked, so the one registered here ahead of
        // the button's first ClickedEventBinding opens each click's event before any handler reads it.
        private void BindClickedEvent(List<Action> actions, Button button, Action<ClickedEvent>? handler)
        {
            if (!_clickedEventSlots.TryGetValue(button, out var slot))
            {
                slot = new ClickedEventSlot();
                _clickedEventSlots[button] = slot;
                Action open = () => slot.Current = new ClickedEvent();
                button.clicked += open;
                actions.Add(() =>
                {
                    button.clicked -= open;
                    _clickedEventSlots.Remove(button);
                });
            }

            Action wrapped = () => RunDiscrete(() => handler?.Invoke(slot.Current));
            button.clicked += wrapped;
            actions.Add(() => button.clicked -= wrapped);
        }

        // Registers a discrete user-input callback, bracketing each invocation with RunDiscrete so
        // hook updates it triggers take the Urgent lane and flush synchronously at the handler's end.
        private void BindDiscreteCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler,
            bool capture)
            where T : EventBase<T>, new()
        {
            var phase = capture ? TrickleDown.TrickleDown : TrickleDown.NoTrickleDown;
            EventCallback<T> wrapped = evt =>
            {
                if (!capture && StoppedByBubblePrelude(element, evt)) return;
                RunDiscrete(() => handler?.Invoke(evt));
            };
            element.RegisterCallback(wrapped, phase);
            actions.Add(() => element.UnregisterCallback(wrapped, phase));
        }

        // Registers a discrete value-changed callback (text / toggle / slider input counts as a discrete interaction),
        // bracketing the handler with RunDiscrete so the update takes the Urgent lane and flushes
        // synchronously at the handler's end. Collapses the per-T ChangeEventBinding<T> cases.
        private void BindDiscreteValueChanged<T>(List<Action> actions, INotifyValueChanged<T> field, Action<T>? handler)
        {
            var callback = new EventCallback<ChangeEvent<T>>(evt => RunDiscrete(() => handler?.Invoke(evt.newValue)));
            field.RegisterValueChangedCallback(callback);
            actions.Add(() => field.UnregisterValueChangedCallback(callback));
        }

        // The discrete-input bracket lives in FiberDiscreteEventScope (shared with the drag-and-drop
        // session, whose start/end/cancel commits must behave exactly like click handlers).
        // Two deliberate limitations of its synchronous flush, which the single-context lane stays correct for
        // but which differ in flush timing for edge configurations: (1) only the owning context's batch is
        // drained synchronously, so Urgent updates a handler schedules on other ReconcilerContexts (e.g. a
        // shared-Store subscriber in a separately mounted tree) still commit on their own next-frame drain;
        // (2) layout-effect-scheduled updates during the synchronous flush take the Normal next-frame lane
        // rather than being re-flushed synchronously before paint.
        private void RunDiscrete(Action? handler)
            => FiberDiscreteEventScope.Run(handler, _batchScheduler);

        private static Delegate? GetDelegate(FiberEventBinding binding)
        {
            return binding switch
            {
                ClickedBinding clicked => clicked.Handler,
                ClickedEventBinding clickedEvent => clickedEvent.Handler,
                ChangeEventBinding<float> floatChange => floatChange.Handler,
                ChangeEventBinding<bool> boolChange => boolChange.Handler,
                ChangeEventBinding<string> stringChange => stringChange.Handler,
                ChangeEventBinding<int> intChange => intChange.Handler,
                PointerDownBinding b => b.Handler,
                PointerUpBinding b => b.Handler,
                PointerMoveBinding b => b.Handler,
                PointerEnterBinding b => b.Handler,
                PointerLeaveBinding b => b.Handler,
                WheelBinding b => b.Handler,
                KeyDownBinding b => b.Handler,
                KeyUpBinding b => b.Handler,
                FocusInBinding b => b.Handler,
                FocusOutBinding b => b.Handler,
                FocusBinding b => b.Handler,
                BlurBinding b => b.Handler,
                GeometryChangedBinding b => b.Handler,
                _ => null,
            };
        }
    }
}
