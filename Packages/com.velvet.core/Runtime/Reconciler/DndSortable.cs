#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>What a <see cref="DndSortingStrategy"/> reads, dnd-kit's sorting strategy arguments.</summary>
    public readonly struct DndSortingArgs
    {
        /// <summary>The dragged item's index in the context's items.</summary>
        public int ActiveIndex { get; }
        /// <summary>The index of the item the drag is over.</summary>
        public int OverIndex { get; }
        /// <summary>The index of the item the transform is for.</summary>
        public int Index { get; }
        /// <summary>Each item's rect without the transform a drag gave it, in items order, panel space; an item
        /// that is not mounted has an empty rect.</summary>
        public IReadOnlyList<Rect> Rects { get; }
        /// <summary>The dragged item's rect when the drag started.</summary>
        public Rect ActiveNodeRect { get; }

        internal DndSortingArgs(int activeIndex, int overIndex, int index, IReadOnlyList<Rect> rects, Rect activeNodeRect)
        {
            ActiveIndex = activeIndex;
            OverIndex = overIndex;
            Index = index;
            Rects = rects;
            ActiveNodeRect = activeNodeRect;
        }
    }

    /// <summary>The translate and scale a sorting strategy gives one item.</summary>
    public readonly struct DndSortingTransform
    {
        public Vector2 Translate { get; }
        public Vector2 Scale { get; }

        public DndSortingTransform(Vector2 translate, Vector2? scale = null)
        {
            Translate = translate;
            Scale = scale ?? Vector2.one;
        }
    }

    /// <summary>Where an item of a <c>V.SortableContext</c> moves while a drag sorts it, dnd-kit's
    /// <c>SortingStrategy</c>; null leaves it in place.</summary>
    public delegate DndSortingTransform? DndSortingStrategy(in DndSortingArgs args);

    /// <summary>The sorting strategies dnd-kit's <c>@dnd-kit/sortable</c> ships.</summary>
    public static class DndSortingStrategies
    {
        /// <summary>dnd-kit's <c>rectSortingStrategy</c>, the default: every item takes the rect of the slot the
        /// moved order puts it in, scaled to that slot's size.</summary>
        public static readonly DndSortingStrategy Rect = (in DndSortingArgs args) =>
        {
            var rects = args.Rects;
            if (!Valid(args.Index, rects) || !Valid(args.ActiveIndex, rects) || !Valid(args.OverIndex, rects))
            {
                return null;
            }
            var moved = DndSortable.ArrayMove(rects, args.OverIndex, args.ActiveIndex);
            var oldRect = rects[args.Index];
            var newRect = moved[args.Index];
            return new DndSortingTransform(
                newRect.position - oldRect.position,
                new Vector2(newRect.width / oldRect.width, newRect.height / oldRect.height));
        };

        /// <summary>dnd-kit's <c>rectSwappingStrategy</c>: the dragged item and the one it is over trade places.</summary>
        public static readonly DndSortingStrategy RectSwapping = (in DndSortingArgs args) =>
        {
            var rects = args.Rects;
            int from;
            int to;
            if (args.Index == args.ActiveIndex)
            {
                from = args.Index;
                to = args.OverIndex;
            }
            else if (args.Index == args.OverIndex)
            {
                from = args.Index;
                to = args.ActiveIndex;
            }
            else
            {
                return null;
            }
            if (!Valid(from, rects) || !Valid(to, rects))
            {
                return null;
            }
            var oldRect = rects[from];
            var newRect = rects[to];
            return new DndSortingTransform(
                newRect.position - oldRect.position,
                new Vector2(newRect.width / oldRect.width, newRect.height / oldRect.height));
        };

        /// <summary>dnd-kit's <c>verticalListSortingStrategy</c>: the items between the dragged one and the one it
        /// is over shift by its height and the gap beside them.</summary>
        public static readonly DndSortingStrategy VerticalList = (in DndSortingArgs args) =>
            ListStrategy(in args, vertical: true);

        /// <summary>dnd-kit's <c>horizontalListSortingStrategy</c>, along x.</summary>
        public static readonly DndSortingStrategy HorizontalList = (in DndSortingArgs args) =>
            ListStrategy(in args, vertical: false);

        private static DndSortingTransform? ListStrategy(in DndSortingArgs args, bool vertical)
        {
            var rects = args.Rects;
            var activeRect = Valid(args.ActiveIndex, rects) ? rects[args.ActiveIndex] : args.ActiveNodeRect;
            if (args.Index == args.ActiveIndex)
            {
                if (!Valid(args.OverIndex, rects))
                {
                    return null;
                }
                var overRect = rects[args.OverIndex];
                var shift = args.ActiveIndex < args.OverIndex
                    ? End(overRect, vertical) - End(activeRect, vertical)
                    : Start(overRect, vertical) - Start(activeRect, vertical);
                return Along(shift, vertical);
            }
            var gap = ItemGap(rects, args.Index, args.ActiveIndex, vertical);
            var size = vertical ? activeRect.height : activeRect.width;
            if (args.Index > args.ActiveIndex && args.Index <= args.OverIndex)
            {
                return Along(-size - gap, vertical);
            }
            if (args.Index < args.ActiveIndex && args.Index >= args.OverIndex)
            {
                return Along(size + gap, vertical);
            }
            return Along(0f, vertical);
        }

        private static float ItemGap(IReadOnlyList<Rect> rects, int index, int activeIndex, bool vertical)
        {
            if (!Valid(index, rects))
            {
                return 0f;
            }
            var current = rects[index];
            var hasPrevious = Valid(index - 1, rects);
            var hasNext = Valid(index + 1, rects);
            if (activeIndex < index)
            {
                return hasPrevious ? Start(current, vertical) - End(rects[index - 1], vertical)
                    : hasNext ? Start(rects[index + 1], vertical) - End(current, vertical)
                    : 0f;
            }
            return hasNext ? Start(rects[index + 1], vertical) - End(current, vertical)
                : hasPrevious ? Start(current, vertical) - End(rects[index - 1], vertical)
                : 0f;
        }

        private static float Start(Rect rect, bool vertical) => vertical ? rect.yMin : rect.xMin;

        private static float End(Rect rect, bool vertical) => vertical ? rect.yMax : rect.xMax;

        private static DndSortingTransform Along(float shift, bool vertical)
            => new(vertical ? new Vector2(0f, shift) : new Vector2(shift, 0f));

        private static bool Valid(int index, IReadOnlyList<Rect> rects) => index >= 0 && index < rects.Count;
    }

    /// <summary>A sortable item's place in its <c>V.SortableContext</c>, dnd-kit's <c>data.sortable</c>.</summary>
    public sealed record SortableInfo(string ContainerId, int Index, IReadOnlyList<string> Items);

    /// <summary>What <c>Hooks.UseSortable</c> returns, dnd-kit's <c>useSortable</c> state. An index is -1 where
    /// there is none: <see cref="ActiveIndex"/> and <see cref="OverIndex"/> while no item of this context is
    /// dragged or under the drag.</summary>
    public readonly record struct SortableState
    {
        public int Index { get; init; }
        public int ActiveIndex { get; init; }
        public int OverIndex { get; init; }
        /// <summary>Where the dragged order puts this item.</summary>
        public int NewIndex { get; init; }
        /// <summary>Whether any drag is active in the scope.</summary>
        public bool IsSorting { get; init; }
        public bool IsDragging { get; init; }
        public bool IsOver { get; init; }
        public IReadOnlyList<string> Items { get; init; }
        public string? ContainerId { get; init; }
    }

    /// <summary>Helpers of dnd-kit's <c>@dnd-kit/sortable</c>.</summary>
    public static class DndSortable
    {
        /// <summary>Returns a copy of <paramref name="items"/> with the item at <paramref name="from"/> moved to
        /// <paramref name="to"/>, dnd-kit's <c>arrayMove</c>. A negative <paramref name="to"/> counts from the end.</summary>
        public static T[] ArrayMove<T>(IReadOnlyList<T> items, int from, int to)
        {
            var list = new List<T>(items);
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to < 0 ? list.Count + 1 + to : to, item);
            return list.ToArray();
        }

        /// <summary>Returns a copy of <paramref name="items"/> with the items at <paramref name="from"/> and
        /// <paramref name="to"/> swapped, dnd-kit's <c>arraySwap</c>.</summary>
        public static T[] ArraySwap<T>(IReadOnlyList<T> items, int from, int to)
        {
            var array = new T[items.Count];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = items[i];
            }
            (array[from], array[to]) = (array[to], array[from]);
            return array;
        }

        /// <summary>
        /// dnd-kit's <c>sortableKeyboardCoordinates</c>, for <see cref="DndKeyboardSensor"/>: an arrow key moves the
        /// drag onto the nearest droppable in its direction, by the corners of the rects, passing over the one the
        /// drag is already over. Items of one context after the dragged one are met at their far edge.
        /// </summary>
        public static readonly DndKeyboardCoordinateGetter KeyboardCoordinates = (in DndKeyboardMove move) =>
        {
            if (move.Key is not (KeyCode.DownArrow or KeyCode.RightArrow or KeyCode.UpArrow or KeyCode.LeftArrow))
            {
                return null;
            }
            var ahead = Ahead(in move);
            if (Nearest(in move, ahead) is not { } chosen)
            {
                return null;
            }
            var collisionRect = move.CollisionRect;
            var newRect = chosen.Rect;
            var active = move.Active.Sortable;
            var other = chosen.Sortable;
            var after = active != null && other != null && active.ContainerId == other.ContainerId
                && active.Index < other.Index;
            var offset = after
                ? new Vector2(collisionRect.width - newRect.width, collisionRect.height - newRect.height)
                : Vector2.zero;
            return offset.x != 0f && offset.y != 0f ? newRect.position : newRect.position - offset;
        };

        // The droppables whose top-left lies past the collision rect's in the arrow's direction.
        private static List<DndDroppableRect> Ahead(in DndKeyboardMove move)
        {
            var origin = move.CollisionRect.position;
            var ahead = new List<DndDroppableRect>();
            foreach (var candidate in move.Droppables)
            {
                var corner = candidate.Rect.position;
                var isAhead = move.Key switch
                {
                    KeyCode.DownArrow => origin.y < corner.y,
                    KeyCode.UpArrow => origin.y > corner.y,
                    KeyCode.LeftArrow => origin.x > corner.x,
                    _ => origin.x < corner.x,
                };
                if (isAhead)
                {
                    ahead.Add(candidate);
                }
            }
            return ahead;
        }

        // The nearest by corners, or the next one where the nearest is the target the drag is already over.
        private static DndDroppableRect? Nearest(in DndKeyboardMove move, List<DndDroppableRect> ahead)
        {
            var collisionRect = move.CollisionRect;
            var query = new DndCollisionQuery(collisionRect, collisionRect.center, ahead, hasPointer: false);
            var collisions = DndCollisions.ClosestCorners(in query);
            if (collisions.Length == 0)
            {
                return null;
            }
            var closest = collisions[0].Id == move.OverId && collisions.Length > 1 ? collisions[1].Id : collisions[0].Id;
            foreach (var candidate in ahead)
            {
                if (candidate.Id == closest)
                {
                    return candidate;
                }
            }
            return null;
        }

        internal static int IndexOf(IReadOnlyList<string> items, string? id)
        {
            if (id == null)
            {
                return -1;
            }
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] == id)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    // One V.SortableContext instance's live items and strategy. Stable for the instance's life, so the
    // membership a sortable item's settings carry compares equal across the context's renders.
    internal sealed class DndSortableContextStore
    {
        internal IReadOnlyList<string> Items = System.Array.Empty<string>();
        internal DndSortingStrategy Strategy = DndSortingStrategies.Rect;
        internal string Id = string.Empty;
    }

    // What V.SortableContext provides: a fresh value per render, so UseSortable re-renders when the items do.
    internal sealed record DndSortableContextValue(
        DndSortableContextStore Store, IReadOnlyList<string> Items, DndSortingStrategy Strategy, string Id)
    {
        internal static readonly ComponentContext<DndSortableContextValue?> Context =
            ComponentContext<DndSortableContextValue?>.Create();
    }

    // Carried on a sortable item's DraggableSettings: the context it sorts in and its own strategy, if any.
    internal sealed record DndSortableMembership(DndSortableContextStore Store, DndSortingStrategy? Strategy);

    // The displacement one drag gives the other items of its source's V.SortableContext. Each item is moved by
    // the strategy whenever the target changes, with an inline transition so the move animates, and every
    // value it wrote is put back when the drag closes or the item leaves.
    internal sealed class DndSortableSession
    {
        private const float TransitionMs = 200f;

        private readonly DndActiveDrag _drag;
        private readonly DndSortableContextStore _store;
        private readonly Dictionary<VisualElement, Saved> _saved = new();
        private readonly List<Rect> _rects = new();

        private readonly struct Saved
        {
            internal readonly StyleTranslate Translate;
            internal readonly StyleScale Scale;
            internal readonly StyleList<StylePropertyName> TransitionProperty;
            internal readonly StyleList<TimeValue> TransitionDuration;
            internal readonly StyleList<EasingFunction> TransitionTiming;

            internal Saved(VisualElement element)
            {
                Translate = SeatedTranslate.Read(element);
                Scale = element.style.scale;
                TransitionProperty = element.style.transitionProperty;
                TransitionDuration = element.style.transitionDuration;
                TransitionTiming = element.style.transitionTimingFunction;
            }
        }

        private DndSortableSession(DndActiveDrag drag, DndSortableContextStore store)
        {
            _drag = drag;
            _store = store;
        }

        internal static DndSortableSession? TryBegin(DndActiveDrag drag)
        {
            if (!drag.Context.DraggableBindings.TryGetValue(drag.Source, out var binding))
            {
                return null;
            }
            var membership = binding.Settings.Sortable;
            return membership == null ? null : new DndSortableSession(drag, membership.Store);
        }

        internal static SortableInfo? InfoOf(DraggableSettings settings)
            => settings.Sortable is { } membership
                ? new SortableInfo(membership.Store.Id,
                    DndSortable.IndexOf(membership.Store.Items, settings.Id), membership.Store.Items)
                : null;

        // The rect collision detection reads for a member: its layout box placed by its ancestors, so the
        // displacement this session wrote does not move it.
        internal static Rect TransformAgnosticRect(VisualElement element)
            => element.hierarchy.parent is { } parent ? parent.LocalToWorld(element.layout) : element.worldBound;

        internal bool IsMember(VisualElement element)
            => _drag.Context.DraggableBindings.TryGetValue(element, out var binding)
               && ReferenceEquals(binding.Settings.Sortable?.Store, _store);

        internal void Update(string? overId)
        {
            var items = _store.Items;
            var activeIndex = DndSortable.IndexOf(items, _drag.ActiveId);
            var overIndex = DndSortable.IndexOf(items, overId);
            var members = Members();
            _rects.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                _rects.Add(default);
            }
            foreach (var (element, binding) in members)
            {
                var index = DndSortable.IndexOf(items, binding.Settings.Id);
                if (index >= 0)
                {
                    _rects[index] = TransformAgnosticRect(element);
                }
            }
            foreach (var (element, binding) in members)
            {
                var index = DndSortable.IndexOf(items, binding.Settings.Id);
                // The dragged item moves itself with the drag unless an overlay shows it, as dnd-kit's
                // useSortable gives the drag source its own transform when no DragOverlay is used.
                if (index < 0 || (ReferenceEquals(element, _drag.Source) && _drag.ActiveMovement == DragMovement.Translate))
                {
                    continue;
                }
                DndSortingTransform? transform = null;
                if (activeIndex >= 0 && overIndex >= 0)
                {
                    var strategy = binding.Settings.Sortable?.Strategy ?? _store.Strategy;
                    transform = strategy(new DndSortingArgs(activeIndex, overIndex, index, _rects, _drag.OriginRect));
                }
                Write(element, transform);
            }
        }

        internal void Forget(VisualElement element)
        {
            if (_saved.TryGetValue(element, out var saved))
            {
                Put(element, saved);
                _saved.Remove(element);
            }
        }

        internal void Restore()
        {
            foreach (var (element, saved) in _saved)
            {
                Put(element, saved);
            }
            _saved.Clear();
        }

        private List<(VisualElement Element, DndDraggableBinding Binding)> Members()
        {
            var members = new List<(VisualElement, DndDraggableBinding)>();
            foreach (var (element, binding) in _drag.Context.DraggableBindings)
            {
                if (ReferenceEquals(binding.Settings.Sortable?.Store, _store)
                    && ReferenceEquals(DndActiveDrag.FindEnclosingScope(element, _drag.Context, out _), _drag.ScopeElement))
                {
                    members.Add((element, binding));
                }
            }
            return members;
        }

        private void Write(VisualElement element, DndSortingTransform? transform)
        {
            if (!_saved.ContainsKey(element))
            {
                if (transform == null)
                {
                    return;
                }
                _saved[element] = new Saved(element);
                element.style.transitionProperty = new List<StylePropertyName> { "translate", "scale" };
                element.style.transitionDuration = new List<TimeValue> { new(TransitionMs, TimeUnit.Millisecond) };
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Ease) };
            }
            var value = transform ?? new DndSortingTransform(Vector2.zero);
            SeatedTranslate.Write(element, new Translate(value.Translate.x, value.Translate.y));
            element.style.scale = value.Scale == Vector2.one
                ? _saved[element].Scale
                : new StyleScale(new Scale(new Vector3(value.Scale.x, value.Scale.y, 1f)));
        }

        // The session's own transition comes off first, so putting the translate back is not animated by it.
        private static void Put(VisualElement element, in Saved saved)
        {
            element.style.transitionProperty = saved.TransitionProperty;
            element.style.transitionDuration = saved.TransitionDuration;
            element.style.transitionTimingFunction = saved.TransitionTiming;
            SeatedTranslate.Write(element, saved.Translate);
            element.style.scale = saved.Scale;
        }
    }
}
