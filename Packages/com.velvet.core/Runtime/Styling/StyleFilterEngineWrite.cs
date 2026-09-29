using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The instant write of a composed filter list. Where UI Toolkit's inline-filter setter animates the write, it
    // pads a function one side lacks from that function's declared default, which for contrast is 0 where CSS's
    // identity is 1 (FilterTransitionPanelTests' straight-write contrast case pins the engine's 0). So the side
    // lacking functions at the end of the list is handed them at the neutral Velvet's tween fades from: functions
    // gained are written into the painted list with transitions suspended, the value the engine's transition then
    // starts from, and a contrast lost is written beside the target and dropped, suspended again, when the
    // engine's transition of the filter ends.
    internal static class StyleFilterEngineWrite
    {
        private static readonly List<StylePropertyName> s_noTransition = new() { new StylePropertyName("none") };
        private static readonly StylePropertyName s_filter = new("filter");
        private static readonly ConditionalWeakTable<VisualElement, PendingDrop> s_pendingDrops = new();
        private static readonly EventCallback<TransitionEndEvent> s_onTransitionEnd = OnTransitionEnd;

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

        // A null list clears the inline filter.
        public static void Write(VisualElement element, List<FilterFunction>? to)
        {
            if (element.panel != null && EngineAnimates(element, to))
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
            WritePlain(element, to);
        }

        // Clearing the inline filter is animated by the entry for filter rather than the one for background-size a
        // list write is animated by (FilterTransitionPanelTests' background-size clear case).
        private static bool EngineAnimates(VisualElement element, List<FilterFunction>? to)
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

        // Stops at the first Custom: the engine pads one from its declared defaults, which are its neutral already,
        // and pairs no function bound to a destroyed definition (FilterTransitionPanelTests' destroyed custom
        // case). Every custom but brightness composes after contrast.
        private static void AppendNeutrals(List<FilterFunction> destination, List<FilterFunction> source, int start)
        {
            for (var k = start; k < source.Count; k++)
            {
                if (source[k].type == FilterFunctionType.Custom)
                {
                    return;
                }
                destination.Add(StyleFilterTransitionDriver.NeutralOf(source[k]));
            }
        }

        // Clears before writing: with no transition, clearing the inline filter cancels a filter transition still
        // running, which writing a list does not, and the next animated write would start from that transition's
        // value instead (FilterTransitionPanelTests' mid-transition contrast case).
        internal static void WriteSuspended(VisualElement element, List<FilterFunction>? list)
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
