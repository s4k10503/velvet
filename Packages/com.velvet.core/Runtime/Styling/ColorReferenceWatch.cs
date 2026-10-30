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
    // elsewhere in the panel costs this one nothing.
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

        private readonly VisualElement _element;
        private readonly Action<VisualElement> _reresolve;
        private readonly Color[] _last = new Color[7];
        private VisualElement? _watchedParent;
        private bool _sampled;
        private bool _stopped;

        public ColorReferenceWatch(VisualElement element, Action<VisualElement> reresolve)
        {
            _element = element;
            _reresolve = reresolve;
            Mark(element);
            element.RegisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved, this);
            element.RegisterCallback<AttachToPanelEvent, ColorReferenceWatch>(OnAttached, this);
            WatchParent();
        }

        private static void OnResolved(CustomStyleResolvedEvent evt, ColorReferenceWatch self) => self.Tick();

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
                parent.RegisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved, this);
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
            _element.UnregisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved);
            _element.UnregisterCallback<AttachToPanelEvent, ColorReferenceWatch>(OnAttached);
            Unmark(_element);
            ReleaseParent();
        }

        private void ReleaseParent()
        {
            if (_watchedParent == null)
            {
                return;
            }
            _watchedParent.UnregisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved);
            Unmark(_watchedParent);
            _watchedParent = null;
        }
    }
}
