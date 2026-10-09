using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Writes the filter utilities' composed list and the tween's frames. Where the engine's animation of a write
    // takes filter's own timing (StyleFilterTransitionDriver.EngineTimesFilterWrites) on a mount whose MotionClock
    // is the panel's, the write is left to animate,
    // but UI Toolkit's inline-filter setter pads a function one side lacks from that function's declared default,
    // which for contrast is 0 where CSS's identity is 1 (FilterTransitionPanelTests' straight-write contrast case
    // pins the engine's 0). So the side lacking functions at the end of the list is handed them at the neutral
    // Velvet's tween fades from: functions gained are written into the painted list with transitions suspended, the
    // value the engine's transition then starts from, and a contrast lost is written beside the target and dropped,
    // suspended again, when the engine's transition of the filter ends or the element leaves the panel.
    internal static class StyleFilterEngineWrite
    {
        private static readonly List<StylePropertyName> s_noTransition = new() { new StylePropertyName("none") };
        private static readonly StylePropertyName s_filter = new("filter");
        private static readonly ConditionalWeakTable<VisualElement, PendingDrop> s_pendingDrops = new();
        private static readonly EventCallback<TransitionEndEvent> s_onTransitionEnd = OnTransitionEnd;
        private static readonly EventCallback<DetachFromPanelEvent> s_onDetach = OnDetach;

        private sealed class PendingDrop
        {
            public readonly List<FilterFunction> Padded;
            public readonly List<FilterFunction>? Target;

            public PendingDrop(List<FilterFunction> padded, List<FilterFunction>? target)
            {
                Padded = padded;
                Target = target;
            }
        }

        private static int s_withheld;

        internal static bool TransitionsWithheld => s_withheld > 0;

        // Within the scope a filter change is written at once. A CSS animation that ends uncovers the value under it
        // without a transition, since the after-change style is computed with the animations of the before-change
        // style.
        internal static WithheldTransitions WithoutTransition()
        {
            s_withheld++;
            return default;
        }

        internal readonly struct WithheldTransitions : IDisposable
        {
            public void Dispose() => s_withheld--;
        }

        // A null list clears the inline filter.
        public static void Write(VisualElement element, List<FilterFunction>? to)
        {
            // The animation's value shows while it runs.
            if (StyleAnimateDriver.DrivesFilter(element))
            {
                return;
            }
            if (element.panel == null)
            {
                WritePlain(element, to);
                return;
            }
            if (TransitionsWithheld)
            {
                WriteSuspended(element, to);
                return;
            }
            // The engine animates a write on the panel's time, which a mount on another clock does not follow, so
            // there the write lands at once like one no transition runs for.
            if (StyleFilterTransitionDriver.EngineTimesFilterWrites(element) && MotionClock.Of(element).StepsOnPanelTime)
            {
                var painted = element.resolvedStyle.filter?.ToList() ?? new List<FilterFunction>();
                var toCount = to?.Count ?? 0;
                if (toCount > painted.Count)
                {
                    AppendNeutrals(painted, to!, painted.Count);
                    WriteSuspended(element, painted);
                }
                // MUTANT_SURVIVES(equivalent, boundary): lists of one length leave an empty tail, which holds no contrast.
                else if (painted.Count > toCount && TryPadRemoval(element, painted, to))
                {
                    return;
                }
            }
            // The tween has settled or declined this change, or no transition runs for filter, so an animation the
            // setter would start here is one CSS does not run.
            else if (SetterAnimates(element, to))
            {
                WriteSuspended(element, to);
                return;
            }
            WritePlain(element, to);
        }

        // A frame of Velvet's tween, written past the animation the setter would start for it.
        public static void WriteFrame(VisualElement element, List<FilterFunction> frame)
        {
            if (StyleFilterTransitionDriver.EngineAnimatesFilterWrites(element))
            {
                WriteSuspended(element, frame);
            }
            else
            {
                element.style.filter = frame;
            }
        }

        // Clearing the inline filter is animated by the entry for filter rather than the one for background-size a
        // list write is animated by (FilterTransitionPanelTests' background-size clear case).
        private static bool SetterAnimates(VisualElement element, List<FilterFunction>? to)
            => to == null
                ? StyleFilterTransitionDriver.FilterTransitionRuns(element)
                : StyleFilterTransitionDriver.EngineAnimatesFilterWrites(element);

        private static void WritePlain(VisualElement element, List<FilterFunction>? to)
        {
            if (to != null)
            {
                element.style.filter = to;
            }
            else
            {
                element.style.filter = StyleKeyword.Null;
            }
        }

        private static bool TryPadRemoval(VisualElement element, List<FilterFunction> painted, List<FilterFunction>? to)
        {
            var toCount = to?.Count ?? 0;
            // A lost tail without a contrast is left to the engine, since padding has to be dropped again when the
            // transition ends (FilterTransitionPanelTests' removed blur case).
            if (!HasContrast(painted, toCount))
            {
                return false;
            }
            var padded = to != null ? new List<FilterFunction>(to) : new List<FilterFunction>();
            AppendNeutrals(padded, painted, toCount);
            element.style.filter = padded;
            // Painted at once rather than animated, so no transition will end to drop the padding at.
            if (ReferenceEquals(element.resolvedStyle.filter, padded))
            {
                WriteSuspended(element, to);
            }
            else
            {
                s_pendingDrops.AddOrUpdate(element, new PendingDrop(padded, to));
                element.RegisterCallback(s_onTransitionEnd);
                element.RegisterCallback(s_onDetach);
            }
            return true;
        }

        private static bool HasContrast(List<FilterFunction> list, int start)
        {
            for (var k = start; k < list.Count; k++)
            {
                if (list[k].type == FilterFunctionType.Contrast)
                {
                    return true;
                }
            }
            return false;
        }

        // Stops at a Custom bound to a destroyed definition, which the engine pairs with nothing
        // (FilterTransitionPanelTests' destroyed custom case).
        private static void AppendNeutrals(List<FilterFunction> destination, List<FilterFunction> source, int start)
        {
            for (var k = start; k < source.Count; k++)
            {
                if (source[k].type == FilterFunctionType.Custom && source[k].customDefinition == null)
                {
                    return;
                }
                destination.Add(StyleFilterTransitionDriver.NeutralOf(source[k]));
            }
        }

        // Clears before writing: with no transition, clearing the inline filter cancels a filter transition still
        // running, which writing a list does not, and the next animated write would start from that transition's
        // value instead (FilterTransitionPanelTests' mid-transition contrast case).
        private static void WriteSuspended(VisualElement element, List<FilterFunction>? list)
        {
            var restore = CornerRadiusFit.DetachedCopy(element.style.transitionProperty);
            element.style.transitionProperty = s_noTransition;
            try
            {
                element.style.filter = StyleKeyword.Null;
                if (list != null)
                {
                    element.style.filter = list;
                }
            }
            finally
            {
                element.style.transitionProperty = restore;
            }
        }

        private static void OnTransitionEnd(TransitionEndEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || evt.target != element
                || !evt.stylePropertyNames.Contains(s_filter))
            {
                return;
            }
            Drop(element);
        }

        // Leaving the panel ends the engine's transition without a TransitionEndEvent (FilterTransitionPanelTests'
        // detached contrast case).
        private static void OnDetach(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is VisualElement element)
            {
                Drop(element);
            }
        }

        private static void Drop(VisualElement element)
        {
            if (!s_pendingDrops.TryGetValue(element, out var drop))
            {
                return;
            }
            // MUTANT_SURVIVES(equivalent, line removed): the reference check below refuses a leftover entry.
            // Keeping one changes only how long its lists live.
            s_pendingDrops.Remove(element);
            // Another writer may have replaced the list since.
            if (ReferenceEquals(element.style.filter.value, drop.Padded))
            {
                WriteSuspended(element, drop.Target);
            }
        }
    }
}
