#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The UI Toolkit host's IFrameClock: one UseFrameDispatcher per panel, subscribed while the host is attached to it.
    // A transient detach flips the subscription inactive and a re-attach to the same panel resumes the same slot, which
    // is what keeps its place among same-priority siblings across a keyed reorder.
    internal sealed class PanelFrameClock : IFrameClock
    {
        internal static readonly PanelFrameClock Instance = new();

        private PanelFrameClock()
        {
        }

        public IFrameSubscription Subscribe(VisualElement host, int priority, Action<float> onFrame, MotionClock? clock)
            => new PanelSubscription(host, priority, onFrame, clock);

        private sealed class PanelSubscription : IFrameSubscription
        {
            private readonly VisualElement _host;
            private readonly Action<float> _onFrame;
            private readonly MotionClock? _clock;
            private readonly EventCallback<AttachToPanelEvent> _onAttach;
            private readonly EventCallback<DetachFromPanelEvent> _onDetach;
            private UseFrameDispatcher? _dispatcher;
            private UseFrameDispatcher.Subscription? _subscription;
            private int _priority;

            internal PanelSubscription(VisualElement host, int priority, Action<float> onFrame, MotionClock? clock)
            {
                _host = host;
                _priority = priority;
                _onFrame = onFrame;
                _clock = clock;
                _onAttach = _ => EnsureSubscribed();
                _onDetach = _ =>
                {
                    if (_subscription != null) _subscription.Active = false;
                };
                host.RegisterCallback(_onAttach);
                host.RegisterCallback(_onDetach);
                // A subscribe made after the commit finds the host attached already and no AttachToPanelEvent coming.
                EnsureSubscribed();
            }

            public int Priority
            {
                get => _priority;
                set
                {
                    _priority = value;
                    if (_subscription != null) _subscription.Priority = value;
                }
            }

            private void EnsureSubscribed()
            {
                var panel = _host.panel;
                if (panel == null) return;
                var current = UseFrameDispatcher.GetOrCreate(panel);
                if (_subscription != null && ReferenceEquals(_dispatcher, current))
                {
                    _subscription.Active = true;
                    return;
                }
                if (_subscription != null) _dispatcher!.Unsubscribe(_subscription);
                _dispatcher = current;
                _subscription = current.Subscribe(_priority, _onFrame, _clock);
            }

            public void Dispose()
            {
                _host.UnregisterCallback(_onAttach);
                // MUTANT_SURVIVES(equivalent, line removed): the detach callback writes Active = false, and the Unsubscribe that ends Dispose leaves the subscription inactive, so a later detach writes the value it holds; only EnsureSubscribed sets Active, and its one trigger after construction is the attach callback unregistered above.
                _host.UnregisterCallback(_onDetach);
                if (_subscription != null) _dispatcher!.Unsubscribe(_subscription);
            }
        }
    }
}
