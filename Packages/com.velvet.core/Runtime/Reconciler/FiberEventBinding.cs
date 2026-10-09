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

    /// <summary>
    /// Binding for a click on a <c>Button</c> whose handler receives the click, React's <c>onClick</c>
    /// with its event. The bindings of this kind on one button share one <see cref="ClickedEvent"/> per
    /// click and run in the order they were bound, so a handler sees what an earlier one did to it.
    /// </summary>
    public sealed class ClickedEventBinding : FiberEventBinding
    {
        public override string EventId => "clicked";
        public Action<ClickedEvent>? Handler { get; init; }
    }

    /// <summary>
    /// The click a <see cref="ClickedEventBinding"/> handler receives, carrying React's
    /// <c>preventDefault()</c>: <c>V.Link</c> and <c>V.NavLink</c> skip their navigation for a click a
    /// handler bound ahead of it prevented, as React Router's <c>Link</c> does.
    /// </summary>
    public sealed class ClickedEvent
    {
        /// <summary>Whether a handler called <see cref="PreventDefault"/> on this click.</summary>
        public bool DefaultPrevented { get; private set; }

        /// <summary>Cancels the default action this click would take.</summary>
        public void PreventDefault() => DefaultPrevented = true;
    }

    /// <summary>
    /// Base for the bindings that take React's <c>on…Capture</c> form: pointer down, up and move, wheel, key,
    /// focus in and out, focus and blur. The enter and leave bindings derive from
    /// <see cref="FiberEventBinding"/> instead, as React's <c>onPointerEnter</c> and <c>onPointerLeave</c> have
    /// no capture form, and so does the geometry binding, which has no React counterpart at all.
    /// </summary>
    public abstract class FiberDispatchedEventBinding : FiberEventBinding
    {
        /// <summary>
        /// When true the handler runs as the event travels down toward its target rather than as it
        /// travels back up: an ancestor's capture handler runs before a descendant's, and every capture
        /// handler before any bubble handler.
        /// </summary>
        public bool Capture { get; init; }
    }

    public sealed class PointerDownBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "pointerdown";
        public EventCallback<PointerDownEvent>? Handler { get; init; }
    }

    public sealed class PointerUpBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "pointerup";
        public EventCallback<PointerUpEvent>? Handler { get; init; }
    }

    public sealed class PointerMoveBinding : FiberDispatchedEventBinding
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

    public sealed class WheelBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "wheel";
        public EventCallback<WheelEvent>? Handler { get; init; }
    }

    public sealed class KeyDownBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "keydown";
        public EventCallback<KeyDownEvent>? Handler { get; init; }
    }

    public sealed class KeyUpBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "keyup";
        public EventCallback<KeyUpEvent>? Handler { get; init; }
    }

    public sealed class FocusInBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "focusin";
        public EventCallback<FocusInEvent>? Handler { get; init; }
    }

    public sealed class FocusOutBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "focusout";
        public EventCallback<FocusOutEvent>? Handler { get; init; }
    }

    public sealed class FocusBinding : FiberDispatchedEventBinding
    {
        public override string EventId => "focus";
        public EventCallback<FocusEvent>? Handler { get; init; }
    }

    public sealed class BlurBinding : FiberDispatchedEventBinding
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
