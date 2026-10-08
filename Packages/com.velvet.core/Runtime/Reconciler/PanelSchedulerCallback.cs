using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Marks the Velvet panel-scheduler callback running now and the panel whose scheduler runs it, so
    // FiberBatchScheduler can tell a Transition request made inside a pass of its own panel's scheduler from one
    // made anywhere else (see FiberBatchScheduler.ScheduleDelayed). A callback Velvet registers without this
    // bracket still works; a request it makes takes the path for one made outside a pass.
    internal static class PanelSchedulerCallback
    {
        private static int s_started;

        // The bracketed callback running now, 0 outside every one.
        internal static int Current { get; private set; }

        private static IPanel? s_currentPanel;

        internal readonly struct Outer
        {
            internal readonly int Id;
            internal readonly IPanel? Panel;

            internal Outer(int id, IPanel? panel)
            {
                Id = id;
                Panel = panel;
            }
        }

        internal static Outer Enter(IPanel? panel)
        {
            var outer = new Outer(Current, s_currentPanel);
            Current = ++s_started;
            s_currentPanel = panel;
            return outer;
        }

        internal static void Exit(Outer outer)
        {
            Current = outer.Id;
            s_currentPanel = outer.Panel;
        }

        internal static void Run<T>(VisualElement? host, T state, Action<T> body)
        {
            var outer = Enter(host?.panel);
            try
            {
                body(state);
            }
            finally
            {
                Exit(outer);
            }
        }

        // A registration made now on host's scheduler lands on a later pass than the running one only while that
        // same scheduler is mid-pass: each panel's scheduler holds back only what is registered on itself.
        // Outside every bracketed callback the recorded panel is null, which no panel equals.
        internal static bool InPassOf(VisualElement? host)
            => host?.panel is { } panel && ReferenceEquals(panel, s_currentPanel);
    }
}
