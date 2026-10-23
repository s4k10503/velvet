#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// How an active drag moves its source element. <see cref="Translate"/> (the default) writes the
    /// pointer delta as an inline <c>translate</c> on the source every move; <see cref="None"/> leaves the
    /// source in place — the <c>V.DragOverlay</c> ghost pattern, where only the portal-rendered preview
    /// follows the pointer.
    /// </summary>
    public enum DragMovement
    {
        Translate,
        None,
    }

    /// <summary>
    /// Constraint before a press becomes a drag. <see cref="Distance"/> is panel px of travel before
    /// activation; zero (the default, and <see cref="None"/>) makes the press itself the drag, as dnd-kit's
    /// <c>PointerSensor</c> does with no constraint — so a press on a draggable control is a drag and not
    /// a click. A <see cref="DelaySec"/> &gt; 0 switches from distance-based to hold-to-drag activation:
    /// activation happens after the hold, aborted if the observed travel exceeds <see cref="Tolerance"/>
    /// first.
    /// </summary>
    public sealed record DragActivation(float Distance = 0f, float DelaySec = 0f, float Tolerance = 5f)
    {
        public static readonly DragActivation Default = new();
        public static readonly DragActivation None = new(Distance: 0f);
    }

    /// <summary>The active drag source, as seen by every context callback.</summary>
    public sealed record DraggableInfo(string Id, object? Data, VisualElement Element)
    {
        /// <summary>Its place in its <c>V.SortableContext</c>; null for a draggable <c>V.Sortable</c> did not make.</summary>
        public SortableInfo? Sortable { get; init; }
    }

    /// <summary>A drop target, as seen by the over/end callbacks.</summary>
    public sealed record DroppableInfo(string Id, object? Data, VisualElement Element)
    {
        /// <summary>Its place in its <c>V.SortableContext</c>; null for a droppable <c>V.Sortable</c> did not make.</summary>
        public SortableInfo? Sortable { get; init; }
    }

    /// <summary>Fired once when a press crosses its activation constraint and becomes a drag.
    /// <see cref="Origin"/> is the pointer's panel-space position at activation.</summary>
    public sealed record DragStartArgs(DraggableInfo Active, Vector2 Origin);

    /// <summary>Fired when the winning drop target CHANGES (including to null — leaving all targets).
    /// <see cref="Delta"/> is the total panel-space translation since activation. <see cref="Collisions"/>
    /// is the strategy's full ranking at that move, as dnd-kit's event carries <c>collisions</c>.</summary>
    public sealed record DragOverArgs(
        DraggableInfo Active, DroppableInfo? Over, Vector2 Delta, IReadOnlyList<DndCollision> Collisions);

    /// <summary>Fired on release. <see cref="Over"/> is null when dropped on nothing; state written here
    /// flushes synchronously, like any discrete input handler. <see cref="Collisions"/> is the strategy's
    /// full ranking at the last pointer move, empty when the drag ended before one.</summary>
    public sealed record DragEndArgs(
        DraggableInfo Active, DroppableInfo? Over, Vector2 Delta, Vector2 Position,
        IReadOnlyList<DndCollision> Collisions);

    /// <summary>Fired when a drag aborts without a drop: Escape, a pointer cancel, a lost pointer
    /// capture, or the source/scope unmounting mid-drag.</summary>
    public sealed record DragCancelArgs(DraggableInfo Active);

    /// <summary>Fired on every move of an active drag, before an <c>onDragOver</c> the same move causes, as
    /// dnd-kit's <c>onDragMove</c> is. <see cref="Over"/> is the target the drag was over before this move and
    /// <see cref="Collisions"/> the strategy's ranking at this move.</summary>
    public sealed record DragMoveArgs(
        DraggableInfo Active, DroppableInfo? Over, Vector2 Delta, IReadOnlyList<DndCollision> Collisions);

    /// <summary>
    /// What <c>Hooks.UseDndContext</c> returns: the drag the nearest <c>V.DndContext</c> holds, dnd-kit's public
    /// context. <see cref="Active"/> is null while no drag is active; <see cref="ActiveNodeRect"/> is the source's
    /// rect at activation, panel space.
    /// </summary>
    public sealed record DndContextState(
        DraggableInfo? Active, DroppableInfo? Over, IReadOnlyList<DndCollision> Collisions, Rect? ActiveNodeRect)
    {
        /// <summary>No drag active.</summary>
        public static readonly DndContextState Idle =
            new(null, null, System.Array.Empty<DndCollision>(), null);
    }

    /// <summary>What <c>Hooks.UseDraggable</c> returns: dnd-kit's <c>useDraggable</c> state. <see cref="Transform"/>
    /// is the translate the drag applies to this draggable, null unless it is the one being dragged.</summary>
    public readonly record struct DraggableState(
        bool IsDragging, DraggableInfo? Active, DroppableInfo? Over, Vector2? Transform);

    /// <summary>What <c>Hooks.UseDroppable</c> returns: dnd-kit's <c>useDroppable</c> state.</summary>
    public readonly record struct DroppableState(bool IsOver, DraggableInfo? Active, DroppableInfo? Over);

    /// <summary>The callbacks <c>Hooks.UseDndMonitor</c> registers with the nearest <c>V.DndContext</c>, each
    /// called after the context's own callback of the same name, as dnd-kit's <c>useDndMonitor</c> is.</summary>
    public sealed record DndMonitorListener(
        System.Action<DragStartArgs>? OnDragStart = null,
        System.Action<DragMoveArgs>? OnDragMove = null,
        System.Action<DragOverArgs>? OnDragOver = null,
        System.Action<DragEndArgs>? OnDragEnd = null,
        System.Action<DragCancelArgs>? OnDragCancel = null);

    /// <summary>One drop candidate's live rect, as handed to a collision strategy.</summary>
    public readonly struct DndDroppableRect
    {
        public string Id { get; }
        /// <summary>Live <c>worldBound</c>, panel space — re-read every move, so mid-drag layout shifts
        /// and scrolls are picked up automatically.</summary>
        public Rect Rect { get; }
        public object? Data { get; }

        // Position in attach order; zero for a rect a caller constructs.
        internal long Order { get; }

        // The droppable's place in a V.SortableContext, which DndSortable.KeyboardCoordinates reads.
        internal SortableInfo? Sortable { get; }

        public DndDroppableRect(string id, Rect rect, object? data)
            : this(id, rect, data, 0, null)
        {
        }

        internal DndDroppableRect(string id, Rect rect, object? data, long order, SortableInfo? sortable)
        {
            Id = id;
            Rect = rect;
            Data = data;
            Order = order;
            Sortable = sortable;
        }
    }

    /// <summary>
    /// Everything a collision strategy may consider. Candidates are pre-filtered: in-scope, enabled,
    /// attached, same panel as the source, and the active draggable's own id excluded (an element that is
    /// both draggable and droppable under one id never collides with itself).
    /// </summary>
    public readonly struct DndCollisionQuery
    {
        /// <summary>The source's activation-time <c>worldBound</c> translated by the current pointer
        /// delta — correct even under <see cref="DragMovement.None"/>, where the source element itself
        /// never moves.</summary>
        public Rect ActiveRect { get; }
        /// <summary>Current pointer position, panel space.</summary>
        public Vector2 PointerPosition { get; }
        /// <summary>In registration order: a droppable that unmounts and mounts again goes to the end, as in
        /// dnd-kit's <c>DroppableContainersMap</c>.</summary>
        public IReadOnlyList<DndDroppableRect> Droppables { get; }

        /// <summary>False for a drag no pointer drives, such as one <see cref="DndKeyboardSensor"/> started: dnd-kit
        /// hands its strategies no pointer coordinates there, and <see cref="DndCollisions.PointerWithin"/> collides
        /// with nothing. <see cref="PointerPosition"/> is then the active rect's center.</summary>
        public bool HasPointer { get; }

        public DndCollisionQuery(Rect activeRect, Vector2 pointerPosition, IReadOnlyList<DndDroppableRect> droppables)
            : this(activeRect, pointerPosition, droppables, hasPointer: true)
        {
        }

        internal DndCollisionQuery(
            Rect activeRect, Vector2 pointerPosition, IReadOnlyList<DndDroppableRect> droppables, bool hasPointer)
        {
            ActiveRect = activeRect;
            PointerPosition = pointerPosition;
            Droppables = droppables;
            HasPointer = hasPointer;
        }
    }

    /// <summary>One colliding droppable: its id, its <see cref="DndDroppableRect.Data"/>, and the value the
    /// strategy ranked it by (<see cref="DndCollisions"/> says which per strategy).</summary>
    public readonly struct DndCollision
    {
        public string Id { get; }
        public object? Data { get; }
        public double Value { get; }

        public DndCollision(string id, object? data, double value)
        {
            Id = id;
            Data = data;
            Value = value;
        }
    }

    /// <summary>Returns the colliding droppables ranked best first, as dnd-kit's <c>CollisionDetection</c>
    /// does; the first entry becomes the over target and an empty array means no collision. Must be pure
    /// over the query — it runs on every pointer move of an active drag.</summary>
    public delegate DndCollision[] DndCollisionDetection(in DndCollisionQuery query);

    /// <summary>
    /// The text <see cref="DndAccessibility"/> hands the screen reader for each drag event, dnd-kit's
    /// <c>announcements</c>. A null function, or one returning null, announces nothing for that event.
    /// <see cref="Default"/> carries dnd-kit's default English sentences.
    /// </summary>
    public sealed record DndAnnouncements(
        System.Func<DragStartArgs, string?>? OnDragStart = null,
        System.Func<DragMoveArgs, string?>? OnDragMove = null,
        System.Func<DragOverArgs, string?>? OnDragOver = null,
        System.Func<DragEndArgs, string?>? OnDragEnd = null,
        System.Func<DragCancelArgs, string?>? OnDragCancel = null)
    {
        public static readonly DndAnnouncements Default = new(
            OnDragStart: args => $"Picked up draggable item {args.Active.Id}.",
            OnDragOver: args => args.Over != null
                ? $"Draggable item {args.Active.Id} was moved over droppable area {args.Over.Id}."
                : $"Draggable item {args.Active.Id} is no longer over a droppable area.",
            OnDragEnd: args => args.Over != null
                ? $"Draggable item {args.Active.Id} was dropped over droppable area {args.Over.Id}"
                : $"Draggable item {args.Active.Id} was dropped.",
            OnDragCancel: args => $"Dragging was cancelled. Draggable item {args.Active.Id} was dropped.");
    }

    /// <summary>
    /// A scope's screen-reader settings, dnd-kit's <c>accessibility</c> prop. <see cref="Announcements"/> null
    /// means <see cref="DndAnnouncements.Default"/>. <see cref="Announce"/> receives each announcement; null sends
    /// it to <c>UnityEngine.Accessibility.AssistiveSupport.notificationDispatcher</c> while a screen reader is
    /// enabled, where dnd-kit writes it into an ARIA live region.
    /// </summary>
    public sealed record DndAccessibility(DndAnnouncements? Announcements = null, System.Action<string>? Announce = null);

    /// <summary>
    /// Scrolling the <c>ScrollView</c>s around the drag while the pointer nears their edges, dnd-kit's
    /// <c>autoScroll</c>. Within <see cref="Threshold"/> (a fraction of the view's size per axis) of an edge the
    /// view scrolls toward it, faster the closer the pointer is, at up to <see cref="Acceleration"/> px per
    /// <see cref="IntervalMs"/>, and only in a direction the pointer has moved in since the drag started. A drag
    /// <see cref="DndKeyboardSensor"/> drives never auto-scrolls.
    /// </summary>
    public sealed record DndAutoScroll(
        bool Enabled = true, float Acceleration = 10f, float IntervalMs = 5f, float ThresholdX = 0.2f,
        float ThresholdY = 0.2f)
    {
        public static readonly DndAutoScroll Default = new();
        public static readonly DndAutoScroll Disabled = new(Enabled: false);
    }

    /// <summary>
    /// How a <c>V.DragOverlay</c> returns to its source when the drag ends or is cancelled, dnd-kit's
    /// <c>dropAnimation</c>: the overlay moves from where it was to the source's settled position over
    /// <see cref="DurationMs"/> with CSS <c>ease</c> timing while the source is held at opacity 0, then hides. A
    /// zero duration, or an overlay already over the source, hides at once.
    /// </summary>
    public sealed record DndDropAnimation(float DurationMs = 250f)
    {
        public static readonly DndDropAnimation Default = new();

        /// <summary>No animation: the overlay hides when the drag ends, dnd-kit's <c>dropAnimation={null}</c>.</summary>
        public static readonly DndDropAnimation None = new(DurationMs: 0f);
    }

    /// <summary>
    /// Drag-and-drop scope configuration. All callbacks are optional;
    /// <see cref="CollisionDetection"/> null means <see cref="DndCollisions.RectIntersection"/>;
    /// <see cref="Activation"/> is the scope-wide default a per-draggable override wins over.
    /// </summary>
    public sealed record DndContextSettings(
        System.Action<DragStartArgs>? OnDragStart = null,
        System.Action<DragOverArgs>? OnDragOver = null,
        System.Action<DragEndArgs>? OnDragEnd = null,
        System.Action<DragCancelArgs>? OnDragCancel = null,
        DndCollisionDetection? CollisionDetection = null,
        DragActivation? Activation = null,
        System.Action<DragMoveArgs>? OnDragMove = null)
    {
        /// <summary>The sensors a press or key on a draggable is offered to, in order, dnd-kit's
        /// <c>sensors</c>; null means <see cref="DndSensors.Default"/>.</summary>
        public IReadOnlyList<DndSensor>? Sensors { get; init; }

        /// <summary>Applied in order to every drag's translate, dnd-kit's <c>modifiers</c>.</summary>
        public IReadOnlyList<DndModifier>? Modifiers { get; init; }

        /// <summary>Null means <see cref="DndAutoScroll.Default"/>.</summary>
        public DndAutoScroll? AutoScroll { get; init; }

        /// <summary>Null means the default announcements, sent while a screen reader is enabled.</summary>
        public DndAccessibility? Accessibility { get; init; }

        // The store V.DndContext provides to its subtree's hooks; null for a scope declared through the raw props
        // slot, whose binding keeps one of its own.
        internal DndContextStore? Store { get; init; }
    }

    /// <summary>Drag-source configuration. The element carrying
    /// the setting is the drag node itself.</summary>
    public sealed record DraggableSettings(
        string Id,
        object? Data = null,
        bool Disabled = false,
        DragMovement Movement = DragMovement.Translate,
        DragActivation? Activation = null,
        string? WhileDraggingClass = null)
    {
        // The V.SortableContext a V.Sortable item sorts in; null for every other draggable.
        internal DndSortableMembership? Sortable { get; init; }
    }

    /// <summary>Drop-target configuration.
    /// <see cref="WhileOverClass"/> applies while this target is the winning collision;
    /// <see cref="WhileDragActiveClass"/> applies to every enabled candidate while any drag is live in
    /// scope.</summary>
    public sealed record DroppableSettings(
        string Id,
        object? Data = null,
        bool Disabled = false,
        string? WhileOverClass = null,
        string? WhileDragActiveClass = null);

    /// <summary>Marker settings for the <c>V.DragOverlay</c> positioner element (framework-positioned,
    /// picking-ignored, hidden while no drag is active). Carried on props so the overlay rides the same
    /// binding lifecycle as every other element binding.</summary>
    public sealed record DragOverlaySettings
    {
        /// <summary>Null disables it; dnd-kit's default is <see cref="DndDropAnimation.Default"/>.</summary>
        public DndDropAnimation? DropAnimation { get; init; } = DndDropAnimation.Default;

        /// <summary>Applied in order to this overlay's position after the scope's own modifiers, dnd-kit's
        /// <c>DragOverlay modifiers</c>.</summary>
        public IReadOnlyList<DndModifier>? Modifiers { get; init; }

        // What V.DragOverlay's component keeps the drop animation's children alive through; null for a
        // positioner the raw props slot declares, whose children the drop animation cannot hold.
        internal DragOverlayHandle? Handle { get; init; }
    }
}
