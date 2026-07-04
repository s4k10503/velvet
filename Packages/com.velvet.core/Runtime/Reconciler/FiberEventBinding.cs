using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Abstract base for event bindings.
    /// Uses an "unbind all → bind all" diff strategy (event count is typically 1-3, so this is lightweight).
    /// </summary>
    public abstract class FiberEventBinding
    {
        public abstract string EventId { get; }
    }

    /// <summary>
    /// Binding for the Button.clicked event.
    /// </summary>
    public sealed class ClickedBinding : FiberEventBinding
    {
        public override string EventId => "clicked";
        public Action? Handler { get; init; }
    }

    /// <summary>
    /// Binding for BaseField&lt;T&gt;.RegisterValueChangedCallback events.
    /// </summary>
    public sealed class ChangeEventBinding<T> : FiberEventBinding
    {
        public override string EventId => $"change:{typeof(T).Name}";
        public Action<T>? Handler { get; init; }
    }

    public sealed class PointerDownBinding : FiberEventBinding
    {
        public override string EventId => "pointerdown";
        public EventCallback<PointerDownEvent>? Handler { get; init; }
    }

    public sealed class PointerUpBinding : FiberEventBinding
    {
        public override string EventId => "pointerup";
        public EventCallback<PointerUpEvent>? Handler { get; init; }
    }

    public sealed class PointerMoveBinding : FiberEventBinding
    {
        public override string EventId => "pointermove";
        public EventCallback<PointerMoveEvent>? Handler { get; init; }
    }

    public sealed class PointerEnterBinding : FiberEventBinding
    {
        public override string EventId => "pointerenter";
        public EventCallback<PointerEnterEvent>? Handler { get; init; }
    }

    public sealed class PointerLeaveBinding : FiberEventBinding
    {
        public override string EventId => "pointerleave";
        public EventCallback<PointerLeaveEvent>? Handler { get; init; }
    }

    public sealed class WheelBinding : FiberEventBinding
    {
        public override string EventId => "wheel";
        public EventCallback<WheelEvent>? Handler { get; init; }
    }

    public sealed class KeyDownBinding : FiberEventBinding
    {
        public override string EventId => "keydown";
        public EventCallback<KeyDownEvent>? Handler { get; init; }
    }

    public sealed class KeyUpBinding : FiberEventBinding
    {
        public override string EventId => "keyup";
        public EventCallback<KeyUpEvent>? Handler { get; init; }
    }

    public sealed class FocusInBinding : FiberEventBinding
    {
        public override string EventId => "focusin";
        public EventCallback<FocusInEvent>? Handler { get; init; }
    }

    public sealed class FocusOutBinding : FiberEventBinding
    {
        public override string EventId => "focusout";
        public EventCallback<FocusOutEvent>? Handler { get; init; }
    }

    public sealed class FocusBinding : FiberEventBinding
    {
        public override string EventId => "focus";
        public EventCallback<FocusEvent>? Handler { get; init; }
    }

    public sealed class BlurBinding : FiberEventBinding
    {
        public override string EventId => "blur";
        public EventCallback<BlurEvent>? Handler { get; init; }
    }

    public sealed class GeometryChangedBinding : FiberEventBinding
    {
        public override string EventId => "geometrychanged";
        public EventCallback<GeometryChangedEvent>? Handler { get; init; }
    }
}
