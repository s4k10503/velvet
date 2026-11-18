using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // What the render core needs of the host it renders into to run a callback on a later pass. The UI Toolkit
    // implementation is PanelHostScheduler; HostSchedulerBoundaryTests names the types that
    // schedule through it and fails when one reaches a panel or its scheduler itself.
    internal interface IHostScheduler
    {
        // Runs callback on a later pass of the scheduler that serves on. Does nothing for a null on.
        void Schedule(VisualElement? on, Action callback);

        // The bracketed callback running now, 0 outside every one.
        int CurrentPass { get; }

        HostPass Enter(VisualElement? on);

        void Exit(HostPass outer);

        void Run<T>(VisualElement? on, T state, Action<T> body);

        // Whether a registration made now on on's scheduler lands on a later pass than the running one.
        bool InPassOf(VisualElement? on);
    }

    // What Enter returns and Exit restores. Host is whatever the implementation keyed the pass on.
    internal readonly struct HostPass
    {
        internal readonly int Id;
        internal readonly object? Host;

        internal HostPass(int id, object? host)
        {
            Id = id;
            Host = host;
        }
    }

    internal static class HostSchedulers
    {
        // A fiber with no reconciler has no context to ask; it schedules on the UI Toolkit panel.
        internal static IHostScheduler For(ComponentFiber fiber)
            => fiber.Reconciler?.Context.HostScheduler ?? PanelHostScheduler.Instance;
    }
}
