using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // The UI Toolkit host's IHostScheduler: a registration is schedule.Execute on the element, and the pass bracket is
    // PanelSchedulerCallback's, which is process-wide, so one instance serves the process.
    internal sealed class PanelHostScheduler : IHostScheduler
    {
        internal static readonly PanelHostScheduler Instance = new();

        private PanelHostScheduler()
        {
        }

        public void Schedule(VisualElement? on, Action callback) => on?.schedule.Execute(callback);

        public int CurrentPass => PanelSchedulerCallback.Current;

        public HostPass Enter(VisualElement? on) => PanelSchedulerCallback.Enter(on?.panel);

        public void Exit(HostPass outer) => PanelSchedulerCallback.Exit(outer);

        public void Run<T>(VisualElement? on, T state, Action<T> body) => PanelSchedulerCallback.Run(on, state, body);

        public bool InPassOf(VisualElement? on) => PanelSchedulerCallback.InPassOf(on);
    }
}
