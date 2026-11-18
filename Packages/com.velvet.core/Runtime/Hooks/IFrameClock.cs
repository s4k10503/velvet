#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The per-frame tick Hooks.UseFrame subscribes to, on the root context. A subscription ticks while its host is
    // attached to whatever the implementation ticks on, in (priority, subscription order), with the frame's delta in
    // seconds; a request made from inside a tick renders on the next pass (IHostScheduler). The UI Toolkit
    // implementation is PanelFrameClock.
    internal interface IFrameClock
    {
        IFrameSubscription Subscribe(VisualElement host, int priority, Action<float> onFrame, MotionClock? clock);
    }

    internal interface IFrameSubscription : IDisposable
    {
        // Applied live: the next tick orders by it without moving the subscription among its equals.
        int Priority { get; set; }
    }
}
