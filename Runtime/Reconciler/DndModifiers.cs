#nullable enable
using UnityEngine;

namespace Velvet
{
    /// <summary>What a <see cref="DndModifier"/> reads. Every rect is panel space, the source's panel.</summary>
    public readonly struct DndModifierArgs
    {
        /// <summary>The translate so far: the sensor's, or the previous modifier's result.</summary>
        public Vector2 Transform { get; internal init; }
        public DraggableInfo Active { get; internal init; }
        /// <summary>The source's rect when the drag started.</summary>
        public Rect ActiveNodeRect { get; internal init; }
        /// <summary>The rect that moves: the source's at the start, which a <c>V.DragOverlay</c> is placed and
        /// sized to.</summary>
        public Rect DraggingNodeRect { get; internal init; }
        /// <summary>The source's parent's rect when the drag started; null for a source with no parent.</summary>
        public Rect? ContainerNodeRect { get; internal init; }
        /// <summary>The source panel's root rect.</summary>
        public Rect WindowRect { get; internal init; }
        /// <summary>The nearest <c>ScrollView</c> around the source, its viewport rect; null when there is none.</summary>
        public Rect? FirstScrollableAncestorRect { get; internal init; }
        /// <summary>Where the pointer pressed; null for a drag no pointer started.</summary>
        public Vector2? ActivatorCoordinates { get; internal init; }
        public DroppableInfo? Over { get; internal init; }
    }

    /// <summary>Returns the translate a drag applies, given the one before it, dnd-kit's <c>Modifier</c>.</summary>
    public delegate Vector2 DndModifier(in DndModifierArgs args);

    /// <summary>The modifiers dnd-kit's <c>@dnd-kit/modifiers</c> ships.</summary>
    public static class DndModifiers
    {
        public static readonly DndModifier RestrictToVerticalAxis = (in DndModifierArgs args) =>
            new Vector2(0f, args.Transform.y);

        public static readonly DndModifier RestrictToHorizontalAxis = (in DndModifierArgs args) =>
            new Vector2(args.Transform.x, 0f);

        public static readonly DndModifier RestrictToParentElement = (in DndModifierArgs args) =>
            args.ContainerNodeRect is { } container
                ? RestrictToBoundingRect(args.Transform, args.DraggingNodeRect, container)
                : args.Transform;

        public static readonly DndModifier RestrictToWindowEdges = (in DndModifierArgs args) =>
            RestrictToBoundingRect(args.Transform, args.DraggingNodeRect, args.WindowRect);

        public static readonly DndModifier RestrictToFirstScrollableAncestor = (in DndModifierArgs args) =>
            args.FirstScrollableAncestorRect is { } scrollable
                ? RestrictToBoundingRect(args.Transform, args.DraggingNodeRect, scrollable)
                : args.Transform;

        /// <summary>Moves the dragged rect so its center sits under the pointer that pressed it.</summary>
        public static readonly DndModifier SnapCenterToCursor = (in DndModifierArgs args) =>
        {
            if (args.ActivatorCoordinates is not { } pressed)
            {
                return args.Transform;
            }
            var rect = args.DraggingNodeRect;
            return args.Transform + (pressed - rect.position) - rect.size / 2f;
        };

        /// <summary>Rounds each axis of the translate up to a multiple of <paramref name="gridSize"/>.</summary>
        /// <param name="gridSize">The grid step, px.</param>
        /// <returns>The modifier.</returns>
        public static DndModifier CreateSnapModifier(float gridSize) => (in DndModifierArgs args) =>
            new Vector2(
                Mathf.Ceil(args.Transform.x / gridSize) * gridSize,
                Mathf.Ceil(args.Transform.y / gridSize) * gridSize);

        // Keeps rect, moved by transform, inside bounds: an edge that would reach or pass the bounds' edge is
        // pinned to it, the top and left winning where the rect is larger than the bounds.
        private static Vector2 RestrictToBoundingRect(Vector2 transform, Rect rect, Rect bounds)
        {
            var value = transform;
            if (rect.yMin + transform.y <= bounds.yMin)
            {
                value.y = bounds.yMin - rect.yMin;
            }
            else if (rect.yMax + transform.y >= bounds.yMax)
            {
                value.y = bounds.yMax - rect.yMax;
            }
            if (rect.xMin + transform.x <= bounds.xMin)
            {
                value.x = bounds.xMin - rect.xMin;
            }
            else if (rect.xMax + transform.x >= bounds.xMax)
            {
                value.x = bounds.xMax - rect.xMax;
            }
            return value;
        }
    }
}
