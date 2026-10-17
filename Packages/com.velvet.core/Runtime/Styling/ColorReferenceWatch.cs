using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Re-writes an element's colours read off a live source (ArbitraryStyle.Reference) once a colour they read has
    // changed, on the style-resolved events of the two elements the sources live on: the element, whose text colour
    // currentcolor reads and which inherits it from its own parent, and the element it is laid out in, whose
    // text, background and border colours inherit reads. Nothing runs between those events, so an element
    // elsewhere in the panel costs this one nothing. Like LeadingLengthProbe, the events come only where the
    // bundled sheet is on the panel.
    internal sealed class ColorReferenceWatch
    {
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
            _watchedParent?.UnregisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved);
            _watchedParent = parent;
            _watchedParent?.RegisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved, this);
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
            _watchedParent?.UnregisterCallback<CustomStyleResolvedEvent, ColorReferenceWatch>(OnResolved);
            _watchedParent = null;
        }
    }
}
