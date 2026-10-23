#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A drag overlay's return to its source after a drop or a cancel (DndDropAnimation). The target is measured
    // again on every tick rather than once at the close: the drop's own commit, a sortable reorder say, moves
    // the source, and only a later layout places it. The source is found again by its id each time, since the
    // commit may have handed the id to another element.
    internal static class DndDropAnimator
    {
        private const long TickIntervalMs = 16;

        // CSS `ease`.
        private const float EaseX1 = 0.25f;
        private const float EaseY1 = 0.1f;
        private const float EaseX2 = 0.25f;
        private const float EaseY2 = 1f;

        internal sealed class Run
        {
            internal VisualElement Positioner { get; init; } = null!;
            internal DndOverlayBinding Binding { get; init; } = null!;
            internal VisualElement ScopeElement { get; init; } = null!;
            internal string ActiveId { get; init; } = string.Empty;
            internal ReconcilerContext Context { get; init; } = null!;
            internal float DurationMs { get; init; }
            internal IVisualElementScheduledItem? Tick;
            internal bool Measured;
            internal long StartMs;
            internal Vector2 From;
            internal Vector2 To;
            // The source the animation holds at opacity 0, and the inline opacity it had.
            internal VisualElement? HiddenSource;
            internal StyleFloat SavedOpacity;
        }

        // An overlay already on its source when the drag closes hides at once, as an animation from a place to
        // the same place is none.
        internal static void Begin(
            VisualElement positioner, DndOverlayBinding binding, VisualElement source, VisualElement scopeElement,
            string activeId, ReconcilerContext ctx)
        {
            Stop(binding);
            var animation = binding.Settings?.DropAnimation;
            if (animation == null || animation.DurationMs <= 0f || positioner.panel == null
                || IsOnSource(positioner, source, ctx))
            {
                DndOverlayDriver.EndSession(positioner, binding);
                return;
            }
            var run = new Run
            {
                Positioner = positioner,
                Binding = binding,
                ScopeElement = scopeElement,
                ActiveId = activeId,
                Context = ctx,
                DurationMs = animation.DurationMs,
            };
            binding.Drop = run;
            binding.Settings?.Handle?.BeginDrop();
            run.Tick = positioner.schedule.Execute(timer => Step(run, timer)).Every(TickIntervalMs);
        }

        // Ends a run at once, restoring the source and hiding the overlay: a new drag on the overlay, its
        // positioner leaving, or the run's own last frame.
        internal static void Stop(DndOverlayBinding binding)
        {
            var run = binding.Drop;
            if (run == null)
            {
                return;
            }
            binding.Drop = null;
            run.Tick?.Pause();
            run.Tick = null;
            RestoreSource(run);
            DndOverlayDriver.EndSession(run.Positioner, binding);
            binding.Settings?.Handle?.EndDrop();
        }

        // A source leaving the tree mid-animation gets its opacity back before the pool can hand it on.
        internal static void ForgetSource(VisualElement element, ReconcilerContext ctx)
        {
            foreach (var binding in ctx.DragOverlayBindings.Values)
            {
                var run = binding.Drop;
                if (run != null && ReferenceEquals(run.HiddenSource, element))
                {
                    RestoreSource(run);
                }
            }
        }

        private static void Step(Run run, TimerState timer)
        {
            if (!ReferenceEquals(run.Binding.Drop, run))
            {
                return;
            }
            if (!run.Measured)
            {
                Measure(run, timer.now);
                return;
            }
            if (TargetOf(run) is { } target)
            {
                run.To = target;
            }
            var progress = Mathf.Clamp01((timer.now - run.StartMs) / run.DurationMs);
            var eased = CubicBezierEvaluator.Evaluate(EaseX1, EaseY1, EaseX2, EaseY2, progress);
            var position = Vector2.LerpUnclamped(run.From, run.To, eased);
            run.Positioner.style.left = position.x;
            run.Positioner.style.top = position.y;
            if (progress >= 1f)
            {
                Stop(run.Binding);
            }
        }

        private static Vector2? TargetOf(Run run)
        {
            var source = FindSource(run);
            if (source?.panel == null)
            {
                return null;
            }
            var found = DndOverlayDriver.TryLocalPosition(
                run.Positioner, source.panel, source.worldBound.position, run.Context, out var at);
            return found ? at : null;
        }

        private static void Measure(Run run, long now)
        {
            var source = FindSource(run);
            var style = run.Positioner.style;
            var target = source != null ? TargetOf(run) : null;
            if (source == null || target == null)
            {
                Stop(run.Binding);
                return;
            }
            var to = target.Value;
            var from = new Vector2(style.left.value.value, style.top.value.value);
            if ((to - from).sqrMagnitude < 1e-6f)
            {
                Stop(run.Binding);
                return;
            }
            run.From = from;
            run.To = to;
            run.StartMs = now;
            run.Measured = true;
            run.HiddenSource = source;
            run.SavedOpacity = source.style.opacity;
            source.style.opacity = 0f;
        }

        private static bool IsOnSource(VisualElement positioner, VisualElement source, ReconcilerContext ctx)
        {
            if (source.panel == null)
            {
                return false;
            }
            var found = DndOverlayDriver.TryLocalPosition(
                positioner, source.panel, source.worldBound.position, ctx, out var at);
            if (!found)
            {
                return false;
            }
            var style = positioner.style;
            return (at - new Vector2(style.left.value.value, style.top.value.value)).sqrMagnitude < 1e-6f;
        }

        private static VisualElement? FindSource(Run run)
        {
            foreach (var (element, binding) in run.Context.DraggableBindings)
            {
                if (binding.Settings.Id == run.ActiveId
                    && ReferenceEquals(DndActiveDrag.FindEnclosingScope(element, run.Context, out _), run.ScopeElement))
                {
                    return element;
                }
            }
            return null;
        }

        private static void RestoreSource(Run run)
        {
            if (run.HiddenSource == null)
            {
                return;
            }
            run.HiddenSource.style.opacity = run.SavedOpacity;
            run.HiddenSource = null;
        }
    }
}
