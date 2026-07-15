#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Manages event registration / unregistration.
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

        // The owning context's batch scheduler. Used to flush the immediate batch synchronously at the end of a
        // discrete event handler so the UI updates before the next frame. Null when constructed without one (isolated unit
        // tests of binding registration): the discrete flag is still bracketed, but no synchronous flush runs.
        private readonly FiberBatchScheduler? _batchScheduler;

        internal FiberEventBindingManager(FiberBatchScheduler? batchScheduler = null)
        {
            _batchScheduler = batchScheduler;
        }

        // Skips re-registration if the same delegate is already registered.
        public void Bind(VisualElement element, FiberEventBinding binding)
        {
            if (element == null || binding == null)
            {
                return;
            }

            var newDelegate = GetDelegate(binding);
            if (newDelegate != null && _boundDelegates.TryGetValue(element, out var existingDelegates))
            {
                foreach (var d in existingDelegates)
                {
                    if (d == newDelegate)
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
                // Discrete user-input events (a distinct, atomic interaction): a hook update they trigger takes the
                // Urgent lane and the immediate batch flushes synchronously when the handler returns.
                case PointerDownBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case PointerUpBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case KeyDownBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case KeyUpBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case FocusInBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case FocusOutBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case FocusBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case BlurBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                // Continuous events (a high-frequency stream such as pointer move): updates batch to the next frame like a
                // Normal-lane render; no synchronous flush.
                case PointerMoveBinding b: BindCallback(actions, element, b.Handler); break;
                case PointerEnterBinding b: BindCallback(actions, element, b.Handler); break;
                case PointerLeaveBinding b: BindCallback(actions, element, b.Handler); break;
                case WheelBinding b: BindCallback(actions, element, b.Handler); break;
                case GeometryChangedBinding b: BindCallback(actions, element, b.Handler); break;
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

        // Checks whether the bindings already registered on the element match the new event array.
        // If they match, re-binding is not necessary.
        // Order-sensitive: assumes the insertion order of _boundDelegates matches newEvents.
        // Currently only called from PatchCommon, where Bind invocation order guarantees this.
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

            if (delegates.Count != newEvents.Length)
            {
                return false;
            }

            for (var i = 0; i < newEvents.Length; i++)
            {
                var newDelegate = GetDelegate(newEvents[i]);
                // Unknown binding types where GetDelegate returns null are always treated as mismatch (fall back to rebind on the safe side).
                if (newDelegate == null || delegates[i] != newDelegate)
                {
                    return false;
                }
            }

            return true;
        }

        // Unbinds every event from every element.
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

        // Invoked by FiberCrossPanelEventDispatcher (see that class for the full walk algorithm) when a
        // native event that already finished bubbling within its own panel needs to continue toward the
        // logical ancestor chain OUTSIDE that panel — a portal/world-space host panel has no physical
        // ancestor beyond its own root, so nothing native can carry the event further from there.
        // Resolves element's own binding matching evt's runtime type and invokes its raw Handler
        // directly, bypassing UI Toolkit's dispatcher entirely: native RegisterCallback<T> plumbing
        // never runs here, since element may not even share a panel with evt's original target.
        // ClickedBinding/ChangeEventBinding<T> are deliberately NOT handled here: Button.clicked has no
        // underlying bubbling event to carry across the boundary (Clickable detects a click from
        // PointerDown/Up internally and invokes its Action delegate directly, never dispatching a
        // synthesizable event object of its own), and INotifyValueChanged<T>'s ChangeEvent<T> is
        // field-implementation-specific in the same way — both stay panel-local until a dedicated
        // design extends this.
        // Returns true when a matching binding was invoked (informational only; the caller's walk
        // continues regardless — a miss here does not stop propagation up the logical chain).
        internal bool TryInvokeSynthetic(VisualElement element, EventBase evt)
        {
            if (element == null || evt == null || !_bindingsByElement.TryGetValue(element, out var bindings))
            {
                return false;
            }

            var invoked = false;
            foreach (var binding in bindings)
            {
                switch (binding)
                {
                    case PointerDownBinding b when evt is PointerDownEvent pe:
                        RunDiscrete(() => b.Handler?.Invoke(pe));
                        invoked = true;
                        break;
                    case PointerUpBinding b when evt is PointerUpEvent pe:
                        RunDiscrete(() => b.Handler?.Invoke(pe));
                        invoked = true;
                        break;
                    case KeyDownBinding b when evt is KeyDownEvent ke:
                        RunDiscrete(() => b.Handler?.Invoke(ke));
                        invoked = true;
                        break;
                    case KeyUpBinding b when evt is KeyUpEvent ke:
                        RunDiscrete(() => b.Handler?.Invoke(ke));
                        invoked = true;
                        break;
                    case FocusInBinding b when evt is FocusInEvent fe:
                        RunDiscrete(() => b.Handler?.Invoke(fe));
                        invoked = true;
                        break;
                    case FocusOutBinding b when evt is FocusOutEvent fe:
                        RunDiscrete(() => b.Handler?.Invoke(fe));
                        invoked = true;
                        break;
                    case FocusBinding b when evt is FocusEvent fe:
                        RunDiscrete(() => b.Handler?.Invoke(fe));
                        invoked = true;
                        break;
                    case BlurBinding b when evt is BlurEvent fe:
                        RunDiscrete(() => b.Handler?.Invoke(fe));
                        invoked = true;
                        break;
                    case PointerMoveBinding b when evt is PointerMoveEvent pe:
                        b.Handler?.Invoke(pe);
                        invoked = true;
                        break;
                    case PointerEnterBinding b when evt is PointerEnterEvent pe:
                        b.Handler?.Invoke(pe);
                        invoked = true;
                        break;
                    case PointerLeaveBinding b when evt is PointerLeaveEvent pe:
                        b.Handler?.Invoke(pe);
                        invoked = true;
                        break;
                    case WheelBinding b when evt is WheelEvent we:
                        b.Handler?.Invoke(we);
                        invoked = true;
                        break;
                    case GeometryChangedBinding b when evt is GeometryChangedEvent ge:
                        b.Handler?.Invoke(ge);
                        invoked = true;
                        break;
                }
            }
            return invoked;
        }

        private static void BindCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler)
            where T : EventBase<T>, new()
        {
            element.RegisterCallback(handler);
            actions.Add(() => element.UnregisterCallback(handler));
        }

        // Registers a discrete user-input callback, bracketing each invocation with RunDiscrete so
        // hook updates it triggers take the Urgent lane and flush synchronously at the handler's end.
        // Used for the discrete events (pointer down/up, key down/up, focus/blur); continuous events
        // (pointer move/enter/leave, wheel, geometry) keep the plain BindCallback<T>.
        private void BindDiscreteCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler)
            where T : EventBase<T>, new()
        {
            EventCallback<T> wrapped = evt => RunDiscrete(() => handler?.Invoke(evt));
            element.RegisterCallback(wrapped);
            actions.Add(() => element.UnregisterCallback(wrapped));
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

        // Brackets a discrete user-input handler: marks FiberWorkLoop.IsInDiscreteEvent for its
        // duration so hook-triggered renders take the Urgent lane, then — at the outermost discrete boundary —
        // flushes the owning context's immediate batch synchronously so the update commits in the same frame.
        // The flag is restored before the flush so updates scheduled by effects run
        // during the flush fall back to the Normal next-frame lane rather than recursing synchronously.
        // Two deliberate limitations of this synchronous flush, which the single-context lane stays correct for
        // but which differ in flush timing for edge configurations: (1) only the owning context's batch is
        // drained synchronously, so Urgent updates a handler schedules on other ReconcilerContexts (e.g. a
        // shared-Store subscriber in a separately mounted tree) still commit on their own next-frame drain;
        // (2) layout-effect-scheduled updates during the synchronous flush take the Normal next-frame lane
        // rather than being re-flushed synchronously before paint.
        private void RunDiscrete(Action? handler)
        {
            var wasInDiscreteEvent = FiberWorkLoop.IsInDiscreteEvent;
            FiberWorkLoop.IsInDiscreteEvent = true;
            try
            {
                handler?.Invoke();
            }
            finally
            {
                FiberWorkLoop.IsInDiscreteEvent = wasInDiscreteEvent;
                if (!wasInDiscreteEvent)
                {
                    _batchScheduler?.FlushImmediate();
                }
            }
        }

        private static Delegate? GetDelegate(FiberEventBinding binding)
        {
            return binding switch
            {
                ClickedBinding clicked => clicked.Handler,
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
