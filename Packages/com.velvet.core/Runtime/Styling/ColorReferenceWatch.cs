using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Re-writes an element's colours read off a live source (ArbitraryStyle.Reference) once a colour they read has
    // changed, on the style-resolved events of the two elements the sources live on: the element, whose text colour
    // currentcolor reads and which inherits it from its own parent, and the element it is laid out in, whose
    // text, background and border colours inherit reads. Nothing runs between those events, so an element
    // elsewhere in the panel costs this one nothing. An element's events reach every watch that reads it through one
    // Hub, because UI Toolkit keeps a single entry for a callback however many arguments it is registered with.
    //
    // A colour that transitions raises no style pass after the first, so while a transition runs on the watched parent
    // itself the watch is also read on a scheduler tick, and once more when the transition ends or is cancelled.
    //
    // UI Toolkit sends CustomStyleResolvedEvent only to an element holding a custom property, and custom properties
    // do not inherit, so each watched element carries MarkerClass, whose rule in _preflight.uss declares one, as
    // LeadingLengthProbe's does. Marking the element restyles it, which is also what reads its colours the first time.
    internal sealed class ColorReferenceWatch
    {
        internal const string MarkerClass = "velvet-color-reference";

        // How many watches rely on an element's marker: a parent is watched by each child and may be watched itself.
        private static readonly ConditionalWeakTable<VisualElement, int[]> s_marks = new();

        private static void Mark(VisualElement element)
        {
            var count = s_marks.GetValue(element, static _ => new int[1]);
            if (count[0]++ == 0)
            {
                element.AddToClassList(MarkerClass);
            }
        }

        private static void Unmark(VisualElement element)
        {
            if (s_marks.TryGetValue(element, out var count) && count[0] > 0 && --count[0] == 0)
            {
                element.RemoveFromClassList(MarkerClass);
            }
        }

        // The watches reading one element, which hears the events for all of them once.
        private sealed class Hub
        {
            public readonly VisualElement Element;
            // Every watch that reads Element, its own and its children's, for the style-resolved event.
            public readonly System.Collections.Generic.List<ColorReferenceWatch> Readers = new();
            // The child watches, which read Element's transitions.
            public readonly System.Collections.Generic.List<ColorReferenceWatch> Children = new();

            public Hub(VisualElement element) => Element = element;
        }

        private static readonly ConditionalWeakTable<VisualElement, Hub> s_hubs = new();

        private static readonly EventCallback<CustomStyleResolvedEvent, Hub> s_onResolved = (evt, hub) =>
        {
            for (var i = hub.Readers.Count - 1; i >= 0; i--)
            {
                if (i < hub.Readers.Count)
                {
                    hub.Readers[i].Tick();
                }
            }
        };

        private static readonly EventCallback<TransitionRunEvent, Hub> s_onTransitionRun = (evt, hub) =>
        {
            if (evt.target != hub.Element)
            {
                return;
            }
            for (var i = hub.Children.Count - 1; i >= 0; i--)
            {
                if (i < hub.Children.Count)
                {
                    hub.Children[i].TransitionRun();
                }
            }
        };

        private static readonly EventCallback<TransitionEndEvent, Hub> s_onTransitionEnd = (evt, hub) => EndTransition(evt, hub);

        private static readonly EventCallback<TransitionCancelEvent, Hub> s_onTransitionCancel = (evt, hub) => EndTransition(evt, hub);

        private static void EndTransition(EventBase evt, Hub hub)
        {
            if (evt.target != hub.Element)
            {
                return;
            }
            for (var i = hub.Children.Count - 1; i >= 0; i--)
            {
                if (i < hub.Children.Count)
                {
                    hub.Children[i].TransitionEnd();
                }
            }
        }

        private static void Subscribe(VisualElement element, ColorReferenceWatch watch, bool child)
        {
            var hub = s_hubs.GetValue(element, static e => new Hub(e));
            if (hub.Readers.Count == 0)
            {
                element.RegisterCallback(s_onResolved, hub);
                element.RegisterCallback(s_onTransitionRun, hub);
                element.RegisterCallback(s_onTransitionEnd, hub);
                element.RegisterCallback(s_onTransitionCancel, hub);
            }
            hub.Readers.Add(watch);
            if (child)
            {
                hub.Children.Add(watch);
            }
        }

        private static void Unsubscribe(VisualElement element, ColorReferenceWatch watch, bool child)
        {
            if (!s_hubs.TryGetValue(element, out var hub) || !hub.Readers.Remove(watch))
            {
                return;
            }
            if (child)
            {
                hub.Children.Remove(watch);
            }
            if (hub.Readers.Count == 0)
            {
                element.UnregisterCallback(s_onResolved);
                element.UnregisterCallback(s_onTransitionRun);
                element.UnregisterCallback(s_onTransitionEnd);
                element.UnregisterCallback(s_onTransitionCancel);
            }
        }

        private readonly VisualElement _element;
        private readonly Action<VisualElement> _reresolve;
        private readonly Color[] _last = new Color[7];
        private VisualElement? _watchedParent;
        private bool _sampled;
        private bool _stopped;
        private int _transitions;
        private IVisualElementScheduledItem? _sampler;

        public ColorReferenceWatch(VisualElement element, Action<VisualElement> reresolve)
        {
            _element = element;
            _reresolve = reresolve;
            Mark(element);
            Subscribe(element, this, child: false);
            element.RegisterCallback<AttachToPanelEvent, ColorReferenceWatch>(OnAttached, this);
            WatchParent();
        }

        // Whether a transition on the watched parent has the watch reading on a tick.
        internal bool Sampling => _sampler != null;

        private void TransitionRun()
        {
            _transitions++;
            _sampler ??= _element.schedule.Execute(Tick).Every(StyleAnimateDriver.TickMs);
        }

        private void TransitionEnd()
        {
            _transitions = Math.Max(0, _transitions - 1);
            Tick();
            if (_transitions == 0)
            {
                StopSampling();
            }
        }

        private void StopSampling()
        {
            _sampler?.Pause();
            _sampler = null;
        }

        private static void OnAttached(AttachToPanelEvent evt, ColorReferenceWatch self)
        {
            self.WatchParent();
            self.Tick();
        }

        private void Tick()
        {
            if (!_stopped)
            {
                _reresolve(_element);
            }
        }

        // The parent is watched wherever it is now: a keyed move or a wrapper changes it, and each of those
        // attaches the element again.
        private void WatchParent()
        {
            var parent = ClipPathLayoutBox.Of(_element).hierarchy.parent;
            if (parent == _watchedParent)
            {
                return;
            }
            ReleaseParent();
            _watchedParent = parent;
            if (parent != null)
            {
                Mark(parent);
                Subscribe(parent, this, child: true);
            }
        }

        // Whether a colour the element's references read has changed since the last call; true on the first.
        public bool Moved()
        {
            var own = _element.resolvedStyle;
            var parent = ClipPathLayoutBox.Of(_element).hierarchy.parent?.resolvedStyle;
            var moved = !_sampled;
            moved |= Take(0, own.color);
            if (parent != null)
            {
                moved |= Take(1, parent.color);
                moved |= Take(2, parent.backgroundColor);
                moved |= Take(3, parent.borderTopColor);
                moved |= Take(4, parent.borderRightColor);
                moved |= Take(5, parent.borderBottomColor);
                moved |= Take(6, parent.borderLeftColor);
            }
            _sampled = true;
            return moved;
        }

        private bool Take(int slot, Color value)
        {
            var changed = _last[slot] != value;
            _last[slot] = value;
            return changed;
        }

        public void Stop()
        {
            _stopped = true;
            Unsubscribe(_element, this, child: false);
            _element.UnregisterCallback<AttachToPanelEvent, ColorReferenceWatch>(OnAttached);
            Unmark(_element);
            ReleaseParent();
            StopSampling();
        }

        private void ReleaseParent()
        {
            if (_watchedParent == null)
            {
                return;
            }
            Unsubscribe(_watchedParent, this, child: true);
            _transitions = 0;
            StopSampling();
            Unmark(_watchedParent);
            _watchedParent = null;
        }
    }
}
