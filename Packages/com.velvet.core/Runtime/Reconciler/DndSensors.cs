#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// An input source that can start a drag, dnd-kit's sensor. A scope offers each pointer-down and key-down that
    /// reaches one of its enabled draggables to its sensors in order (<see cref="DndContextSettings.Sensors"/>), and
    /// the first whose <see cref="TryActivate"/> returns true takes the event. A press or key that lands inside a
    /// nested draggable, or inside a <c>NoDrag</c> element, is offered to the innermost draggable only.
    /// </summary>
    public abstract class DndSensor
    {
        /// <summary>
        /// Decides whether <paramref name="evt"/> starts this sensor's gesture on the draggable
        /// <paramref name="activation"/> names. Return true to claim it; the drag itself starts when the sensor calls
        /// <see cref="DndSensorActivation.Start"/>, now or after a constraint of its own.
        /// </summary>
        /// <param name="evt">A <c>PointerDownEvent</c> or <c>KeyDownEvent</c>, in its trickle phase at the draggable.</param>
        /// <param name="activation">The draggable and the means to start its drag.</param>
        /// <returns>Whether this sensor claimed the event.</returns>
        protected internal abstract bool TryActivate(EventBase evt, DndSensorActivation activation);
    }

    /// <summary>The sensors a scope uses when it names none.</summary>
    public static class DndSensors
    {
        /// <summary>A <see cref="DndPointerSensor"/> followed by a <see cref="DndKeyboardSensor"/>, dnd-kit's
        /// default pair.</summary>
        public static readonly IReadOnlyList<DndSensor> Default = new DndSensor[]
        {
            new DndPointerSensor(),
            new DndKeyboardSensor(),
        };
    }

    /// <summary>What a sensor is handed with the event it may claim.</summary>
    public sealed class DndSensorActivation
    {
        internal readonly VisualElement Element;
        internal readonly DndDraggableBinding Binding;
        internal readonly ReconcilerContext Context;
        internal readonly VisualElement ScopeElement;
        internal readonly DndScopeBinding Scope;
        internal readonly EventBase ActivatorEvent;
        // The pointer position the activating event carried, panel space; null for a key.
        internal readonly Vector2? ActivatorCoordinates;

        internal DndSensorActivation(
            VisualElement element, DndDraggableBinding binding, ReconcilerContext context,
            VisualElement scopeElement, DndScopeBinding scope, EventBase activatorEvent)
        {
            Element = element;
            Binding = binding;
            Context = context;
            ScopeElement = scopeElement;
            Scope = scope;
            ActivatorEvent = activatorEvent;
            ActivatorCoordinates = activatorEvent is IPointerEvent pointer ? pointer.position : null;
        }

        /// <summary>The draggable the event reached.</summary>
        public DraggableInfo Draggable => new(Binding.Settings.Id, Binding.Settings.Data, Element);

        /// <summary>
        /// Starts the drag at <paramref name="coordinates"/>, panel space: every later
        /// <see cref="DndSensorSession.Move"/> translates the drag by its distance from them. Returns null, starting
        /// nothing, while another drag of the same tree is active or once the draggable has left its scope.
        /// </summary>
        /// <param name="coordinates">The reference the session's moves are measured from.</param>
        /// <param name="onClose">Called once when the drag ends for any reason, so the sensor can stop listening.</param>
        /// <returns>The started session, or null.</returns>
        public DndSensorSession? Start(Vector2 coordinates, System.Action? onClose = null)
            => DndActiveDrag.StartFromSensor(this, coordinates, autoScroll: true, onClose);

        internal DndSensorSession? Start(Vector2 coordinates, bool autoScroll, System.Action? onClose)
            => DndActiveDrag.StartFromSensor(this, coordinates, autoScroll, onClose);
    }

    /// <summary>The drag a sensor started, which the sensor drives until it ends.</summary>
    public sealed class DndSensorSession
    {
        private readonly DndActiveDrag _drag;

        internal DndSensorSession(DndActiveDrag drag)
        {
            _drag = drag;
        }

        /// <summary>False once the drag has ended, been cancelled or been torn down.</summary>
        public bool IsActive => !_drag.IsClosed;

        /// <summary>The active rect collision detection reads, panel space: the source's rect at the start moved by
        /// the drag's translate.</summary>
        public Rect CollisionRect => _drag.CollisionRect;

        /// <summary>Moves the drag to <paramref name="coordinates"/>, panel space.</summary>
        /// <param name="coordinates">The new position, measured against the start's.</param>
        public void Move(Vector2 coordinates) => _drag.SensorMove(coordinates);

        /// <summary>Drops the drag where it is, firing <c>onDragEnd</c>.</summary>
        public void End() => _drag.SensorEnd();

        /// <summary>Cancels the drag, firing <c>onDragCancel</c>.</summary>
        public void Cancel() => _drag.SensorCancel();

        internal DndKeyboardMove KeyboardMove(KeyCode key) => _drag.KeyboardMove(key);
    }

    /// <summary>
    /// dnd-kit's <c>PointerSensor</c>: a primary-button press starts the drag under <see cref="Activation"/>, or
    /// under the scope's <c>activation:</c> when this is null. A draggable's own <c>activation:</c> wins over both.
    /// </summary>
    public sealed class DndPointerSensor : DndSensor
    {
        public DndPointerSensor(DragActivation? activation = null)
        {
            Activation = activation;
        }

        public DragActivation? Activation { get; }

        protected internal override bool TryActivate(EventBase evt, DndSensorActivation activation)
            => evt is PointerDownEvent down && DndActiveDrag.ArmPointer(activation, down, Activation);
    }

    /// <summary>The keys <see cref="DndKeyboardSensor"/> starts, cancels and ends a drag with.</summary>
    public sealed record DndKeyboardCodes(
        IReadOnlyList<KeyCode> Start, IReadOnlyList<KeyCode> Cancel, IReadOnlyList<KeyCode> End)
    {
        /// <summary>dnd-kit's defaults: Space or Enter starts, Escape cancels, Space, Enter or Tab ends.</summary>
        public static readonly DndKeyboardCodes Default = new(
            new[] { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter },
            new[] { KeyCode.Escape },
            new[] { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Tab });
    }

    /// <summary>What a <see cref="DndKeyboardCoordinateGetter"/> reads.</summary>
    public readonly struct DndKeyboardMove
    {
        public KeyCode Key { get; }
        /// <summary>The collision rect's top-left, panel space: where the drag is now.</summary>
        public Vector2 CurrentCoordinates { get; }
        public Rect CollisionRect { get; }
        public DraggableInfo Active { get; }
        /// <summary>The droppable the drag is over, if any.</summary>
        public string? OverId { get; }
        /// <summary>The scope's enabled droppables other than the active one, with their live rects.</summary>
        public IReadOnlyList<DndDroppableRect> Droppables { get; }

        internal DndKeyboardMove(
            KeyCode key, Rect collisionRect, DraggableInfo active, string? overId,
            IReadOnlyList<DndDroppableRect> droppables)
        {
            Key = key;
            CurrentCoordinates = collisionRect.position;
            CollisionRect = collisionRect;
            Active = active;
            OverId = overId;
            Droppables = droppables;
        }
    }

    /// <summary>Where a key moves a keyboard drag to, as a new <see cref="DndKeyboardMove.CurrentCoordinates"/>;
    /// null leaves it where it is.</summary>
    public delegate Vector2? DndKeyboardCoordinateGetter(in DndKeyboardMove move);

    /// <summary>
    /// dnd-kit's <c>KeyboardSensor</c>: a start key on a draggable that holds keyboard focus, or contains the element
    /// that does, starts a drag; until an end or cancel key, each other key the <see cref="CoordinateGetter"/>
    /// answers moves it. The default getter moves 25 px per arrow key.
    /// </summary>
    public sealed class DndKeyboardSensor : DndSensor
    {
        private const float ArrowStep = 25f;

        public DndKeyboardSensor(DndKeyboardCodes? codes = null, DndKeyboardCoordinateGetter? coordinateGetter = null)
        {
            Codes = codes ?? DndKeyboardCodes.Default;
            CoordinateGetter = coordinateGetter ?? DefaultCoordinates;
        }

        public DndKeyboardCodes Codes { get; }

        public DndKeyboardCoordinateGetter CoordinateGetter { get; }

        /// <summary>dnd-kit's <c>defaultKeyboardCoordinateGetter</c>: 25 px per arrow key.</summary>
        public static Vector2? DefaultCoordinates(in DndKeyboardMove move)
        {
            var current = move.CurrentCoordinates;
            return move.Key switch
            {
                KeyCode.RightArrow => new Vector2(current.x + ArrowStep, current.y),
                KeyCode.LeftArrow => new Vector2(current.x - ArrowStep, current.y),
                KeyCode.DownArrow => new Vector2(current.x, current.y + ArrowStep),
                KeyCode.UpArrow => new Vector2(current.x, current.y - ArrowStep),
                _ => null,
            };
        }

        protected internal override bool TryActivate(EventBase evt, DndSensorActivation activation)
        {
            if (evt is not KeyDownEvent key || !Contains(Codes.Start, key.keyCode))
            {
                return false;
            }
            var root = activation.Element.panel?.visualTree;
            if (root == null)
            {
                return false;
            }
            var listener = new Listener(this, root);
            // The drag translates from the source's own top-left, so its first move is measured from there.
            var session = activation.Start(activation.Element.worldBound.position, autoScroll: false, listener.Detach);
            if (session == null)
            {
                return false;
            }
            key.StopPropagation();
            // The start callback's own commit may already have torn the drag down, and closed it.
            if (session.IsActive)
            {
                listener.Attach(session);
            }
            return true;
        }

        private static bool Contains(IReadOnlyList<KeyCode> codes, KeyCode code)
        {
            for (var i = 0; i < codes.Count; i++)
            {
                if (codes[i] == code)
                {
                    return true;
                }
            }
            return false;
        }

        // The keys of one keyboard drag, heard on the panel root ahead of every element below it. The key that
        // started the drag has already passed the root's trickle phase, so it is not heard again as an end key.
        // While the drag lasts, the root also holds back the navigation events the same keys raise, as dnd-kit
        // prevents the keys' defaults: a move would take focus off the source and a submit would click it.
        private sealed class Listener
        {
            private readonly DndKeyboardSensor _sensor;
            private readonly VisualElement _root;
            private readonly EventCallback<KeyDownEvent> _onKeyDown;
            private readonly EventCallback<NavigationMoveEvent> _onNavigationMove;
            private readonly EventCallback<NavigationSubmitEvent> _onNavigationSubmit;
            private DndSensorSession? _session;

            internal Listener(DndKeyboardSensor sensor, VisualElement root)
            {
                _sensor = sensor;
                _root = root;
                _onKeyDown = OnKeyDown;
                _onNavigationMove = HoldBack;
                _onNavigationSubmit = HoldBack;
            }

            internal void Attach(DndSensorSession session)
            {
                _session = session;
                _root.RegisterCallback(_onKeyDown, TrickleDown.TrickleDown);
                _root.RegisterCallback(_onNavigationMove, TrickleDown.TrickleDown);
                _root.RegisterCallback(_onNavigationSubmit, TrickleDown.TrickleDown);
            }

            internal void Detach()
            {
                _root.UnregisterCallback(_onKeyDown, TrickleDown.TrickleDown);
                _root.UnregisterCallback(_onNavigationMove, TrickleDown.TrickleDown);
                _root.UnregisterCallback(_onNavigationSubmit, TrickleDown.TrickleDown);
            }

            private void HoldBack(EventBase evt)
            {
                _root.panel?.focusController?.IgnoreEvent(evt);
                evt.StopPropagation();
            }

            private void OnKeyDown(KeyDownEvent evt)
            {
                var session = _session;
                if (session == null || !session.IsActive)
                {
                    return;
                }
                var codes = _sensor.Codes;
                if (Contains(codes.End, evt.keyCode))
                {
                    evt.StopPropagation();
                    session.End();
                    return;
                }
                if (Contains(codes.Cancel, evt.keyCode))
                {
                    evt.StopPropagation();
                    session.Cancel();
                    return;
                }
                var move = session.KeyboardMove(evt.keyCode);
                var next = _sensor.CoordinateGetter(in move);
                if (next is { } coordinates)
                {
                    evt.StopPropagation();
                    session.Move(coordinates);
                }
            }
        }
    }
}
