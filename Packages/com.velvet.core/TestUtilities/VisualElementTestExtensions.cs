// annotations only: incremental nullable hygiene. See the comment at the top of Velvet's core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Test-only VisualElement extension methods. Provides helpers for firing UI events when no panel is attached.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public static class VisualElementTestExtensions
    {
        private static FieldInfo? s_clickableClickedField;
        private static FieldInfo? s_callbackRegistryField;
        private static FieldInfo? s_currentTargetField;
        private static MethodInfo? s_invokeCallbacksMethod;

        /// <summary>
        /// Directly invokes the clicked handler registered on a Button. Equivalent to React Testing Library's <c>fireEvent.click(button)</c>.
        /// </summary>
        /// <remarks>
        /// In tests without a panel attached, SendEvent / NavigationSubmitEvent / ClickEvent are not routed, so Button.clicked never fires.
        /// This extension uses reflection to obtain <see cref="Clickable"/>'s <c>clicked</c> event backing field (compiler-generated private field) and invoke it.
        /// Stable as long as Unity's <c>Clickable.clicked</c> event declaration stays in the same form (field-like event), but note the dependency on Unity internals.
        /// </remarks>
        public static void SimulateClick(this Button button)
        {
            if (button == null) throw new ArgumentNullException(nameof(button));
            var clickable = button.clickable
                ?? throw new InvalidOperationException(
                    $"Cannot SimulateClick: Button (name='{button.name}') has no Clickable manipulator.");

            var field = GetClickableClickedField();
            if (field.GetValue(clickable) is not Action invoke)
            {
                throw new InvalidOperationException(
                    $"Button (name='{button.name}') has no clicked handler registered. The onClick wiring may be missing.");
            }

            invoke();
        }

        /// <summary>
        /// Directly invokes the registered ChangeEvent handler on an <see cref="INotifyValueChanged{T}"/>. Equivalent to React Testing Library's <c>fireEvent.change(input, { value })</c>.
        /// Updates the value via <see cref="INotifyValueChanged{T}.SetValueWithoutNotify"/> so the UI state stays consistent.
        /// </summary>
        /// <remarks>
        /// In tests without a panel attached, the <see cref="ChangeEvent{T}"/> that <c>field.value = newValue</c> would SendEvent is not routed (panel == null).
        /// This extension reflectively calls <see cref="CallbackEventHandler"/>'s internal field <c>m_CallbackRegistry</c> and the internal method
        /// <c>EventCallbackRegistry.InvokeCallbacks(evt, BubbleUp)</c> (marked <c>// For unit tests only</c>) on it.
        /// Unity performs a `target.elementPanel == panel` check, but since `panel = target.elementPanel` it passes when both sides are null.
        /// Because <c>EventBase.currentTarget</c> has an internal setter, we assign the private field <c>m_CurrentTarget</c> directly.
        /// Always fires the handler even when the old and new values are equal (different from Unity's standard <c>field.value</c> setter, which short-circuits as no-op).
        /// Note: when <see cref="VisualElement.enabledInHierarchy"/> is <c>false</c>, Unity's callback list skips callbacks.
        /// Test disabled state through state properties such as <c>enabledSelf</c> rather than verifying callback firing.
        /// Note: even if the registry is already initialized by other events (e.g. PointerDown), if no <see cref="ChangeEvent{T}"/> handler is registered the call silently no-ops
        /// (no exception is thrown). Detect this in subsequent assertions, or assert the registration state before calling this helper if required.
        /// </remarks>
        public static void SimulateChange<T>(this INotifyValueChanged<T> field, T newValue)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (field is not VisualElement element)
            {
                throw new InvalidOperationException(
                    $"INotifyValueChanged<{typeof(T).Name}> must also be a VisualElement. Actual type: {field.GetType().Name}.");
            }

            var oldValue = field.value;
            field.SetValueWithoutNotify(newValue);

            var registry = GetCallbackRegistryField().GetValue(element);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"VisualElement (name='{element.name}') has no callback registry. " +
                    $"RegisterValueChangedCallback / RegisterCallback may not have been called yet.");
            }

            using var evt = ChangeEvent<T>.GetPooled(oldValue, newValue);
            evt.target = element;
            GetCurrentTargetField().SetValue(evt, element);

            GetInvokeCallbacksMethod().Invoke(registry, new object[] { evt, PropagationPhase.BubbleUp });
        }

        /// <summary>
        /// Dispatches an arbitrary <typeparamref name="TEvent"/> to the callbacks registered on
        /// <paramref name="element"/> via <c>RegisterCallback</c>. In a panel-less EditMode test SendEvent is not
        /// routed, so this reuses the same internal <c>m_CallbackRegistry</c> + <c>InvokeCallbacks</c> reflection
        /// as <see cref="SimulateChange{T}"/> to fire pointer / key / focus events that have no value-change
        /// shortcut. Pass a pooled event, e.g. <c>using var e = PointerMoveEvent.GetPooled(); el.SimulateEvent(e);</c>.
        /// </summary>
        public static void SimulateEvent<TEvent>(this VisualElement element, TEvent evt)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            var registry = GetCallbackRegistryField().GetValue(element);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"VisualElement (name='{element.name}') has no callback registry. RegisterCallback may not have been called yet.");
            }

            evt.target = element;
            GetCurrentTargetField().SetValue(evt, element);
            GetInvokeCallbacksMethod().Invoke(registry, new object[] { evt, PropagationPhase.BubbleUp });
        }

        /// <summary>
        /// Like <see cref="SimulateEvent{TEvent}(VisualElement, TEvent)"/>, but models the BUBBLE-phase arrival
        /// of an event that originated at a descendant: <paramref name="element"/> is the currentTarget (the
        /// ancestor whose callbacks run) while <paramref name="target"/> is the event's target (the descendant
        /// the event actually started on). The plain overload sets target == element, which conflates "the
        /// element itself fired" with "a descendant bubbled up"; this overload keeps them distinct so an
        /// ancestor handler that gates on <c>evt.target</c> — e.g. focus-within / <c>:has(:focus)</c>, which must
        /// react to a focused DESCENDANT but not to the element focusing itself — can be exercised off panel.
        /// </summary>
        public static void SimulateBubbledEvent<TEvent>(this VisualElement element, TEvent evt, VisualElement target)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var registry = GetCallbackRegistryField().GetValue(element);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"VisualElement (name='{element.name}') has no callback registry. RegisterCallback may not have been called yet.");
            }

            evt.target = target;
            GetCurrentTargetField().SetValue(evt, element);
            GetInvokeCallbacksMethod().Invoke(registry, new object[] { evt, PropagationPhase.BubbleUp });
        }

        /// <summary>
        /// Returns the first <see cref="Label"/> in <paramref name="root"/>'s subtree (pre-order DFS, including
        /// <paramref name="root"/> itself) whose <see cref="TextElement.text"/> exactly equals <paramref name="text"/>,
        /// or <c>null</c> if none matches. The match is full-equality, not substring.
        /// </summary>
        public static Label? FindLabelByText(this VisualElement root, string text)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (root is Label label && label.text == text) return label;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = root.ElementAt(i).FindLabelByText(text);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Returns the first <see cref="Label"/> in <paramref name="root"/>'s subtree (pre-order DFS, including
        /// <paramref name="root"/> itself), regardless of its text, or <c>null</c> if the subtree has no Label.
        /// </summary>
        public static Label? FindFirstLabel(this VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (root is Label label) return label;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = root.ElementAt(i).FindFirstLabel();
                if (found != null) return found;
            }
            return null;
        }

        private static FieldInfo GetClickableClickedField()
        {
            return s_clickableClickedField ??=
                typeof(Clickable).GetField(nameof(Clickable.clicked), BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find Clickable.clicked's backing field. Unity internal API may have changed.");
        }

        private static FieldInfo GetCallbackRegistryField()
        {
            return s_callbackRegistryField ??=
                typeof(CallbackEventHandler).GetField("m_CallbackRegistry", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find CallbackEventHandler.m_CallbackRegistry. Unity internal API may have changed.");
        }

        private static FieldInfo GetCurrentTargetField()
        {
            return s_currentTargetField ??=
                typeof(EventBase).GetField("m_CurrentTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find EventBase.m_CurrentTarget. Unity internal API may have changed.");
        }

        private static MethodInfo GetInvokeCallbacksMethod()
        {
            // EventCallbackRegistry is internal sealed so typeof() is not usable. Get it via FieldType (= declaring type).
            // Pin the signature explicitly so we won't mis-resolve if Unity adds another overload with the same name.
            return s_invokeCallbacksMethod ??= GetCallbackRegistryField().FieldType.GetMethod(
                "InvokeCallbacks",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(EventBase), typeof(PropagationPhase) },
                modifiers: null)
                ?? throw new InvalidOperationException(
                    "Could not find EventCallbackRegistry.InvokeCallbacks(EventBase, PropagationPhase). Unity internal API may have changed.");
        }

        /// <summary>
        /// Dispatches a primary-button pointer press through the target's panel, constructed from an
        /// IMGUI system event — the constructor path that maintains the engine's own
        /// <c>PointerDeviceState</c> (pressed buttons), which downstream <c>pressedButtons</c> checks
        /// read from later moves. Shared by the pointer-gesture fixtures (EditMode and PlayMode), so the
        /// event shape cannot drift between the suites.
        /// </summary>
        public static void SendPointerDownEvent(this VisualElement target, UnityEngine.Vector2 position)
        {
            using var evt = PointerDownEvent.GetPooled(new UnityEngine.Event
            {
                type = UnityEngine.EventType.MouseDown, mousePosition = position, button = 0, clickCount = 1,
            });
            evt.target = target;
            target.SendEvent(evt);
        }

        /// <summary>Dispatches a primary-button drag move to the target; see <see cref="SendPointerDownEvent"/>.</summary>
        public static void SendPointerMoveEvent(this VisualElement target, UnityEngine.Vector2 position)
        {
            using var evt = PointerMoveEvent.GetPooled(new UnityEngine.Event
            {
                type = UnityEngine.EventType.MouseDrag, mousePosition = position, button = 0,
            });
            evt.target = target;
            target.SendEvent(evt);
        }

        /// <summary>Dispatches a primary-button release to the target; see <see cref="SendPointerDownEvent"/>.</summary>
        public static void SendPointerUpEvent(this VisualElement target, UnityEngine.Vector2 position)
        {
            using var evt = PointerUpEvent.GetPooled(new UnityEngine.Event
            {
                type = UnityEngine.EventType.MouseUp, mousePosition = position, button = 0, clickCount = 1,
            });
            evt.target = target;
            target.SendEvent(evt);
        }

        /// <summary>
        /// Dispatches a drag move with NO preset target: the panel's own dispatching strategy resolves
        /// the destination — the capturing element when a capture is held, else picking. The only
        /// dispatch shape that exercises the engine's real capture routing (a preset target
        /// short-circuits it).
        /// </summary>
        public static void SendPointerMoveUntargeted(this IPanel panel, UnityEngine.Vector2 position)
        {
            using var evt = PointerMoveEvent.GetPooled(new UnityEngine.Event
            {
                type = UnityEngine.EventType.MouseDrag, mousePosition = position, button = 0,
            });
            panel.visualTree.SendEvent(evt);
        }
    }
}
