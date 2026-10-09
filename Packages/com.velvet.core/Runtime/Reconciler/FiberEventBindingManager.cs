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

        // The owning context's batch scheduler. Used to flush the immediate batch synchronously at the end of a
        // discrete event handler so the UI updates before the next frame. Null when constructed without one (isolated unit
        // tests of binding registration): the discrete flag is still bracketed, but no synchronous flush runs.
        private readonly FiberBatchScheduler? _batchScheduler;

        internal FiberEventBindingManager(FiberBatchScheduler? batchScheduler = null)
        {
            _batchScheduler = batchScheduler;
        }

        // Skips re-registration if the same delegate is already registered for the same kind of binding. The
        // kind is part of the match because one factory can take one delegate in two places of the same
        // delegate type — V.TextField's onValueChanged: and onSubmit: are both Action<string> — and those are
        // two registrations.
        public void Bind(VisualElement element, FiberEventBinding binding)
        {
            if (element == null || binding == null)
            {
                return;
            }

            var newDelegate = GetDelegate(binding);
            if (newDelegate != null && _bindingsByElement.TryGetValue(element, out var existingBindings))
            {
                foreach (var existing in existingBindings)
                {
                    if (existing.GetType() == binding.GetType() && GetDelegate(existing) == newDelegate)
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
        // than the event dispatcher, so each is gated on the element implementing that hook, and the
        // TextField ones, gated on a TextField for the composite RegisterTextFieldBinding reads. A binding
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
                case TextFieldBinding when element is TextField field:
                    RegisterTextFieldBinding(actions, field, binding);
                    break;
                default:
                    RegisterEventBinding(actions, element, binding);
                    break;
            }
        }

        // A TextField is a composite whose input is the text element inside it. Focus and blur answer for
        // focus entering and leaving the field as a whole: the field hands focus from its input to itself on
        // Enter, and back again later, and a DOM input has no such step, so a move whose other end is inside
        // the field is not reported.
        // Submit is read on the input's own bubble pass, after the input has handled the key, so the value it
        // reads already holds what that Enter committed. After that Enter the field itself holds focus, and a
        // browser submits again on a later Enter, so a key landing on the field is read on the field's own
        // pass; a key the input let through reaches the field's callback too, which reads only its own target.
        // Whether an IME composition was open is read on the field's trickle-down pass instead, before the
        // input acts on the key, which is the moment a browser's isComposing describes.
        // The soft keyboard's Done reaches no key handler: the engine closes the keyboard and blurs the input.
        // So the keyboard is held from focus-in and its status read once the field has committed on the
        // focus-out that blur sends.
        // TextFieldEventPropTests pins the focus, blur, Enter and composition readings against the engine, and
        // the Done reading through SoftKeyboard, since an editor opens no soft keyboard.
        private void RegisterTextFieldBinding(List<Action> actions, TextField field, FiberEventBinding binding)
        {
            switch (binding)
            {
                case TextFieldFocusBinding b:
                    BindFocusCrossing(actions, field, b.Handler);
                    break;
                case TextFieldBlurBinding b:
                    BindFocusCrossing(actions, field, b.Handler);
                    break;
                case TextFieldSubmitBinding b when field.textEdition is TextElement input:
                    BindSubmit(actions, field, input, b.Handler);
                    break;
            }
        }

        private void BindSubmit(List<Action> actions, TextField field, TextElement input, Action<string>? handler)
        {
            var composingAtKeyDown = false;
            UnityEngine.TouchScreenKeyboard? keyboard = null;
            EventCallback<KeyDownEvent> beforeInput = _ => composingAtKeyDown = IsComposing(input);
            EventCallback<KeyDownEvent> afterInput = evt =>
            {
                if (evt.target == evt.currentTarget && !composingAtKeyDown && IsSubmitKey(field, evt))
                {
                    RunDiscrete(() => handler?.Invoke(field.value));
                }
            };
            EventCallback<FocusInEvent> keyboardOpened = _ => keyboard = SoftKeyboard.Of(field);
            EventCallback<FocusOutEvent> keyboardClosed = _ =>
            {
                if (keyboard != null && SoftKeyboard.StatusOf(keyboard) == UnityEngine.TouchScreenKeyboard.Status.Done
                    && !field.multiline)
                {
                    RunDiscrete(() => handler?.Invoke(field.value));
                }
            };
            field.RegisterCallback(beforeInput, TrickleDown.TrickleDown);
            input.RegisterCallback(afterInput);
            field.RegisterCallback(afterInput);
            field.RegisterCallback(keyboardOpened);
            field.RegisterCallback(keyboardClosed);
            actions.Add(() =>
            {
                field.UnregisterCallback(beforeInput, TrickleDown.TrickleDown);
                input.UnregisterCallback(afterInput);
                field.UnregisterCallback(afterInput);
                field.UnregisterCallback(keyboardOpened);
                field.UnregisterCallback(keyboardClosed);
            });
        }

        // The input's own record of an open IME composition, which the engine keeps from the composition
        // string as keys and IME events arrive. False where an engine no longer has the members.
        private static bool IsComposing(TextElement input)
        {
            var manipulator = s_editingManipulator?.GetValue(input);
            var utilities = manipulator == null ? null : s_editingUtilities?.GetValue(manipulator);
            return utilities != null && s_compositionActive?.GetValue(utilities) is true;
        }

        private static readonly System.Reflection.PropertyInfo? s_editingManipulator =
            EngineMember.TextEditingManipulator.ResolveProperty();

        private static readonly System.Reflection.FieldInfo? s_editingUtilities =
            EngineMember.TextEditingUtilities.ResolveField();

        private static readonly System.Reflection.FieldInfo? s_compositionActive =
            EngineMember.TextCompositionActive.ResolveField();

        // A text input's own editor stops the propagation of the keys it takes, so a key callback on the
        // control's bubble pass misses what the user types. On the trickle-down pass it reaches them before
        // the input does, as React's onKeyDown reaches a key before the browser acts on it, and a handler
        // stopping propagation there keeps the input from taking the key. TextFieldEventPropTests pins both
        // through V.TextField, and TextInputKeyBindingTests the first through V.Motion.
        private static TrickleDown KeyPhase(VisualElement element)
            => IsTextInput(element.GetType()) ? TrickleDown.TrickleDown : TrickleDown.NoTrickleDown;

        private static bool IsTextInput(Type elementType)
        {
            var type = elementType;
            while (type != null && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TextInputBaseField<>)))
            {
                type = type.BaseType;
            }
            return type != null;
        }

        private void BindFocusCrossing<T>(List<Action> actions, TextField field, EventCallback<T>? handler)
            where T : FocusEventBase<T>, new()
        {
            EventCallback<T> wrapped = evt =>
            {
                if (!IsWithin(field, evt.relatedTarget))
                {
                    RunDiscrete(() => handler?.Invoke(evt));
                }
            };
            field.RegisterCallback(wrapped, TrickleDown.TrickleDown);
            actions.Add(() => field.UnregisterCallback(wrapped, TrickleDown.TrickleDown));
        }

        private static bool IsWithin(TextField field, Focusable? other)
        {
            var element = other as VisualElement;
            return element == field || field.Contains(element);
        }

        // Control or Command held without Alt submits nothing on any platform, whichever of the two the
        // platform treats as its command modifier: react-migration.md owns the browser comparison, and
        // TextFieldEventPropTests pins each modifier alone and with Alt. In a multi-line field Enter submits
        // nothing, as in a <textarea>.
        private static bool IsSubmitKey(TextField field, KeyDownEvent evt)
            => !field.multiline
               && (evt.character == '\n' || evt.character == '\r')
               && !((evt.ctrlKey || evt.commandKey) && !evt.altKey);

        private void RegisterEventBinding(List<Action> actions, VisualElement element, FiberEventBinding binding)
        {
            switch (binding)
            {
                // Discrete user-input events (a distinct, atomic interaction): a hook update they trigger takes the
                // Urgent lane and the immediate batch flushes synchronously when the handler returns.
                case PointerDownBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case PointerUpBinding b: BindDiscreteCallback(actions, element, b.Handler); break;
                case KeyDownBinding b: BindDiscreteCallback(actions, element, b.Handler, KeyPhase(element)); break;
                case KeyUpBinding b: BindDiscreteCallback(actions, element, b.Handler, KeyPhase(element)); break;
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
        // Returns true when a matching pointer, key, focus or geometry binding was invoked (informational
        // only; the caller's walk continues regardless — a miss here does not stop propagation up the
        // logical chain).
        internal bool TryInvokeSynthetic(VisualElement element, EventBase evt)
        {
            if (element == null || evt == null || !_bindingsByElement.TryGetValue(element, out var bindings))
            {
                return false;
            }

            var invoked = false;
            foreach (var binding in bindings)
            {
                InvokeSyntheticField(element, binding, evt);
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
        private void InvokeSyntheticField(VisualElement element, FiberEventBinding binding, EventBase evt)
        {
            switch (binding)
            {
                case ClickedBinding b when element is Button && evt is ClickEvent && element.enabledInHierarchy:
                    RunDiscrete(b.Handler);
                    break;
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
                case PointerEnterBinding b when evt is PointerEnterEvent pe:
                    b.Handler?.Invoke(pe);
                    return true;
                case PointerLeaveBinding b when evt is PointerLeaveEvent pe:
                    b.Handler?.Invoke(pe);
                    return true;
                case WheelBinding b when evt is WheelEvent we:
                    b.Handler?.Invoke(we);
                    return true;
                case GeometryChangedBinding b when evt is GeometryChangedEvent ge:
                    b.Handler?.Invoke(ge);
                    return true;
                default:
                    return false;
            }
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
        private void BindDiscreteCallback<T>(List<Action> actions, VisualElement element, EventCallback<T>? handler,
            TrickleDown phase = TrickleDown.NoTrickleDown)
            where T : EventBase<T>, new()
        {
            EventCallback<T> wrapped = evt => RunDiscrete(() => handler?.Invoke(evt));
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
                TextFieldBinding b => b.HandlerDelegate,
                _ => null,
            };
        }
    }
}
