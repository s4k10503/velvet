#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // One drag's auto-scroll (DndAutoScroll). Each move picks, outermost first, the first ScrollView around the
    // over element (else the source) whose edge zone holds the pointer on an axis it can still scroll along and
    // the pointer has moved toward since the drag started; the panel scheduler then scrolls it until a move
    // picks another or none.
    internal sealed class DndAutoScroller
    {
        private const long TickIntervalMs = 16;

        private readonly DndActiveDrag _drag;
        private readonly DndAutoScroll _settings;
        private readonly VisualElement _schedulerHost;
        private readonly List<ScrollView> _views = new();
        private IVisualElementScheduledItem? _tick;
        private ScrollView? _target;
        // Signed px per interval on each axis.
        private Vector2 _velocity;
        private Vector2 _lastDelta;
        // Whether the pointer has moved toward each edge since the drag started: x-, x+, y-, y+.
        private bool _towardLeft;
        private bool _towardRight;
        private bool _towardTop;
        private bool _towardBottom;

        internal DndAutoScroller(DndActiveDrag drag, DndAutoScroll settings, VisualElement schedulerHost)
        {
            _drag = drag;
            _settings = settings;
            _schedulerHost = schedulerHost;
        }

        internal void Update(Vector2? pointer, Vector2 delta, VisualElement searchStart)
        {
            TrackIntent(delta);
            if (pointer is not { } position)
            {
                Halt();
                return;
            }
            _views.Clear();
            for (var current = searchStart.parent; current != null; current = current.parent)
            {
                if (current is ScrollView view)
                {
                    _views.Add(view);
                }
            }
            for (var i = _views.Count - 1; i >= 0; i--)
            {
                var view = _views[i];
                var velocity = VelocityFor(view, position);
                if (velocity != Vector2.zero)
                {
                    _target = view;
                    _velocity = velocity;
                    _tick ??= _schedulerHost.schedule.Execute(OnTick).Every(TickIntervalMs);
                    _tick.Resume();
                    return;
                }
            }
            Halt();
        }

        internal void Stop()
        {
            _tick?.Pause();
            _tick = null;
            _target = null;
        }

        private void Halt()
        {
            _tick?.Pause();
            _target = null;
            _velocity = Vector2.zero;
        }

        private void TrackIntent(Vector2 delta)
        {
            var step = delta - _lastDelta;
            _towardLeft |= step.x < 0f;
            _towardRight |= step.x > 0f;
            _towardTop |= step.y < 0f;
            _towardBottom |= step.y > 0f;
            _lastDelta = delta;
        }

        private Vector2 VelocityFor(ScrollView view, Vector2 pointer)
        {
            var rect = view.worldBound;
            var zoneHeight = rect.height * _settings.ThresholdY;
            var zoneWidth = rect.width * _settings.ThresholdX;
            var vertical = view.verticalScroller;
            var horizontal = view.horizontalScroller;
            var velocity = Vector2.zero;
            if (zoneHeight > 0f)
            {
                if (vertical.value > vertical.lowValue && pointer.y <= rect.yMin + zoneHeight && _towardTop)
                {
                    velocity.y = -_settings.Acceleration * Mathf.Abs((rect.yMin + zoneHeight - pointer.y) / zoneHeight);
                }
                else if (vertical.value < vertical.highValue && pointer.y >= rect.yMax - zoneHeight && _towardBottom)
                {
                    velocity.y = _settings.Acceleration * Mathf.Abs((rect.yMax - zoneHeight - pointer.y) / zoneHeight);
                }
            }
            if (zoneWidth > 0f)
            {
                if (horizontal.value < horizontal.highValue && pointer.x >= rect.xMax - zoneWidth && _towardRight)
                {
                    velocity.x = _settings.Acceleration * Mathf.Abs((rect.xMax - zoneWidth - pointer.x) / zoneWidth);
                }
                else if (horizontal.value > horizontal.lowValue && pointer.x <= rect.xMin + zoneWidth && _towardLeft)
                {
                    velocity.x = -_settings.Acceleration * Mathf.Abs((rect.xMin + zoneWidth - pointer.x) / zoneWidth);
                }
            }
            return velocity;
        }

        private void OnTick(TimerState timer)
        {
            var view = _target;
            if (view == null || view.panel == null || _settings.IntervalMs <= 0f)
            {
                return;
            }
            var steps = timer.deltaTime / _settings.IntervalMs;
            var before = view.scrollOffset;
            view.scrollOffset = before + _velocity * steps;
            if (view.scrollOffset != before)
            {
                _drag.OnAutoScrolled();
            }
        }
    }
}
