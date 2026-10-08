using System;

namespace Velvet
{
    // Marks the Velvet panel-scheduler callback running now, so FiberBatchScheduler can tell a Transition
    // request made inside a scheduler pass from one made anywhere else (see FiberBatchScheduler.ScheduleDelayed).
    // A callback Velvet registers without this bracket still works; a request it makes takes the path for one
    // made outside a pass.
    internal static class PanelSchedulerCallback
    {
        private static int s_started;

        // The bracketed callback running now, 0 outside every one.
        internal static int Current { get; private set; }

        internal static int Enter()
        {
            var outer = Current;
            Current = ++s_started;
            return outer;
        }

        internal static void Exit(int outer) => Current = outer;

        internal static void Run<T>(T state, Action<T> body)
        {
            var outer = Enter();
            try
            {
                body(state);
            }
            finally
            {
                Exit(outer);
            }
        }
    }
}
