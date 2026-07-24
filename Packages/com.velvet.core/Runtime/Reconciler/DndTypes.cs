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
    /// Constraint before a press becomes a drag, so clicks keep working on draggable elements.
    /// <see cref="Distance"/> is panel px of travel before activation. A <see cref="DelaySec"/> &gt; 0
    /// switches from distance-based to hold-to-drag activation: activation happens after the hold,
    /// aborted if the observed travel exceeds <see cref="Tolerance"/> first. The default is Distance = 4,
    /// not 0, because a zero threshold would race UI Toolkit's own Clickable capture-at-down and kill
    /// clicks on draggable buttons; <see cref="None"/> restores unconstrained (zero-threshold) activation.
    /// </summary>
    public sealed record DragActivation(float Distance = 4f, float DelaySec = 0f, float Tolerance = 5f)
    {
        public static readonly DragActivation Default = new();
        public static readonly DragActivation None = new(Distance: 0f);
    }

    /// <summary>The active drag source, as seen by every context callback.</summary>
    public sealed record DraggableInfo(string Id, object? Data, VisualElement Element);

    /// <summary>A drop target, as seen by the over/end callbacks.</summary>
    public sealed record DroppableInfo(string Id, object? Data, VisualElement Element);

    /// <summary>Fired once when a press crosses its activation constraint and becomes a drag.
    /// <see cref="Origin"/> is the pointer's panel-space position at activation.</summary>
    public sealed record DragStartArgs(DraggableInfo Active, Vector2 Origin);

    /// <summary>Fired when the winning drop target CHANGES (including to null — leaving all targets).
    /// <see cref="Delta"/> is the total panel-space translation since activation.</summary>
    public sealed record DragOverArgs(DraggableInfo Active, DroppableInfo? Over, Vector2 Delta);

    /// <summary>Fired on release. <see cref="Over"/> is null when dropped on nothing; state written here
    /// flushes synchronously, like any discrete input handler.</summary>
    public sealed record DragEndArgs(DraggableInfo Active, DroppableInfo? Over, Vector2 Delta, Vector2 Position);

    /// <summary>Fired when a drag aborts without a drop: Escape, a pointer cancel, a lost pointer
    /// capture, or the source/scope unmounting mid-drag.</summary>
    public sealed record DragCancelArgs(DraggableInfo Active);

    /// <summary>One drop candidate's live rect, as handed to a collision strategy.</summary>
    public readonly struct DndDroppableRect
    {
        public string Id { get; }
        /// <summary>Live <c>worldBound</c>, panel space — re-read every move, so mid-drag layout shifts
        /// and scrolls are picked up automatically.</summary>
        public Rect Rect { get; }
        public object? Data { get; }

        public DndDroppableRect(string id, Rect rect, object? data)
        {
            Id = id;
            Rect = rect;
            Data = data;
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
        public IReadOnlyList<DndDroppableRect> Droppables { get; }

        public DndCollisionQuery(Rect activeRect, Vector2 pointerPosition, IReadOnlyList<DndDroppableRect> droppables)
        {
            ActiveRect = activeRect;
            PointerPosition = pointerPosition;
            Droppables = droppables;
        }
    }

    /// <summary>Returns the winning droppable id, or null for no collision. Must be pure over the query —
    /// it runs on every pointer move of an active drag.</summary>
    public delegate string? DndCollisionDetection(in DndCollisionQuery query);

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
        DragActivation? Activation = null);

    /// <summary>Drag-source configuration. The element carrying
    /// the setting is the drag node itself.</summary>
    public sealed record DraggableSettings(
        string Id,
        object? Data = null,
        bool Disabled = false,
        DragMovement Movement = DragMovement.Translate,
        DragActivation? Activation = null,
        string? WhileDraggingClass = null);

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
    public sealed record DragOverlaySettings;
}
