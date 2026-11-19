using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Velvet
{
    // UI Toolkit's inline setters for a filter list and for background-size, on a write no transition entry animates,
    // cancel the element's transform-origin transition rather than their own property's, landing the origin on its end
    // (FilterTransitionPanelTests' Group G holds both). While one runs, such a write is made inside this scope instead:
    // under an added background-size entry, which the setter animates both writes by, with the element's own entries
    // kept. The writer makes its write land at once; Dispose puts the element's own lists back.
    internal readonly struct TransformOriginSparing : IDisposable
    {
        private static readonly StylePropertyName s_backgroundSize = new("background-size");
        private static readonly StyleBackgroundSize s_autoSize = new BackgroundSize(Length.Auto(), Length.Auto());

        private readonly IStyle? _style;
        private readonly (StyleList<StylePropertyName>, StyleList<TimeValue>, StyleList<TimeValue>, StyleList<EasingFunction>) _saved;

        private TransformOriginSparing(IStyle style,
            (StyleList<StylePropertyName>, StyleList<TimeValue>, StyleList<TimeValue>, StyleList<EasingFunction>) saved)
        {
            _style = style;
            _saved = saved;
        }

        // False, leaving the write to the caller, where no transform-origin transition runs or the engine's member that
        // reads one does not resolve.
        public static bool TryBegin(VisualElement element, out TransformOriginSparing scope)
        {
            scope = default;
            if (!MotionSlotContext.HasRunningNativeTransition(element, StyleLonghand.TransformOrigin))
            {
                return false;
            }
            var style = element.style;
            var resolved = element.resolvedStyle;
            var saved = (CornerRadiusFit.DetachedCopy(style.transitionProperty), MotionTweenTiming.Copy(style.transitionDuration),
                MotionTweenTiming.Copy(style.transitionDelay), MotionTweenTiming.Copy(style.transitionTimingFunction));
            var properties = resolved.transitionProperty.ToList();
            var durations = Spread(resolved.transitionDuration.ToList(), properties.Count, new TimeValue(1f, TimeUnit.Second));
            var delays = Spread(resolved.transitionDelay.ToList(), properties.Count, new TimeValue(0f));
            var curves = Spread(resolved.transitionTimingFunction.ToList(), properties.Count, new EasingFunction(EasingMode.Ease));
            properties.Add(s_backgroundSize);
            style.transitionProperty = properties;
            style.transitionDuration = durations;
            style.transitionDelay = delays;
            style.transitionTimingFunction = curves;
            scope = new TransformOriginSparing(style, saved);
            return true;
        }

        public void Dispose()
        {
            if (_style != null)
            {
                (_style.transitionProperty, _style.transitionDuration, _style.transitionDelay, _style.transitionTimingFunction) = _saved;
            }
        }

        // An inline background-size write. Where no entry runs for background-size and a transform-origin transition
        // does, the size is written by way of an auto size, which converts to and from no other, so each write lands at
        // once and starts no transition. A clear is written as it comes.
        public static void SetBackgroundSize(VisualElement element, StyleBackgroundSize size)
        {
            if (size.keyword != StyleKeyword.Undefined || StyleFilterTransitionDriver.EngineAnimatesFilterWrites(element)
                || !TryBegin(element, out var scope))
            {
                element.style.backgroundSize = size;
                return;
            }
            using (scope)
            {
                element.style.backgroundSize = s_autoSize;
                element.style.backgroundSize = size;
            }
        }

        // A resolved timing list, wrapped as UI Toolkit wraps a list shorter than the properties, over the entries
        // before the one added, followed by the added entry's own.
        private static List<T> Spread<T>(List<T> list, int entries, T added)
        {
            var spread = new List<T>(entries + 1);
            for (var i = 0; i < entries; i++)
            {
                spread.Add(list.Count == 0 ? default! : list[i % list.Count]);
            }
            spread.Add(added);
            return spread;
        }
    }
}
