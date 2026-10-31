using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Calls back the elements that snap a length to the panel's device pixels (a border width written in pixels)
    // once the panel's scale is not what they snapped to: when the element attaches to a panel, including another
    // one, and when the panel it is on changes scale. UI Toolkit raises no event for a scale change, so one scheduled
    // item per panel compares the scale, and only a change walks the elements registered on that panel.
    internal static class PanelScaleWatch
    {
        private const long TickMs = 16;

        private sealed class Entry
        {
            public readonly Dictionary<object, Action<VisualElement>> Resnaps = new();
            public EventCallback<AttachToPanelEvent> OnAttach = null!;
            public IPanel? Watched;
            public readonly WeakReference<VisualElement> Reference;

            public Entry(VisualElement element) => Reference = new WeakReference<VisualElement>(element);
        }

        private sealed class Watch
        {
            // One reference per tracked element: Join adds it once per panel and Untrack and Tick take it out.
            public readonly List<WeakReference<VisualElement>> Elements = new();
            public IVisualElementScheduledItem Item = null!;
            public float Scale;
        }

        private static readonly ConditionalWeakTable<VisualElement, Entry> s_entries = new();
        private static readonly ConditionalWeakTable<IPanel, Watch> s_watches = new();

        // Registers owner's resnap for element, replacing the owner's earlier one; it runs on every attach and scale
        // change. An element holds one per owner, as a border width and a divide on it each snap their own.
        public static void Track(VisualElement element, object owner, Action<VisualElement> resnap)
        {
            if (!s_entries.TryGetValue(element, out var entry))
            {
                entry = new Entry(element);
                var tracked = entry;
                entry.OnAttach = _ =>
                {
                    Join(element, tracked);
                    Run(element, tracked);
                };
                s_entries.Add(element, entry);
                element.RegisterCallback(entry.OnAttach);
            }
            entry.Resnaps[owner] = resnap;
            Join(element, entry);
        }

        // Stops calling owner's resnap for element, as when its layers are released to the pool.
        public static void Untrack(VisualElement element, object owner)
        {
            if (s_entries.TryGetValue(element, out var entry) && entry.Resnaps.Remove(owner) && entry.Resnaps.Count == 0)
            {
                element.UnregisterCallback(entry.OnAttach);
                s_entries.Remove(element);
                if (entry.Watched != null && s_watches.TryGetValue(entry.Watched, out var watch))
                {
                    watch.Elements.Remove(entry.Reference);
                }
                entry.Watched = null;
            }
        }

        private static void Run(VisualElement element, Entry entry)
        {
            foreach (var resnap in new List<Action<VisualElement>>(entry.Resnaps.Values))
            {
                resnap(element);
            }
        }

        private static void Join(VisualElement element, Entry entry)
        {
            var panel = element.panel;
            if (panel == null || ReferenceEquals(entry.Watched, panel))
            {
                return;
            }
            if (entry.Watched != null && s_watches.TryGetValue(entry.Watched, out var previous))
            {
                previous.Elements.Remove(entry.Reference);
            }
            entry.Watched = panel;
            if (!s_watches.TryGetValue(panel, out var watch))
            {
                watch = new Watch { Scale = panel.scaledPixelsPerPoint };
                var created = watch;
                created.Item = panel.visualTree.schedule.Execute(() => Tick(panel, created)).Every(TickMs);
                s_watches.Add(panel, watch);
            }
            watch.Elements.Add(entry.Reference);
        }

        private static void Tick(IPanel panel, Watch watch)
        {
            var scale = panel.scaledPixelsPerPoint;
            if (scale == watch.Scale)
            {
                return;
            }
            watch.Scale = scale;
            for (var i = watch.Elements.Count - 1; i >= 0; i--)
            {
                if (!watch.Elements[i].TryGetTarget(out var element) || !s_entries.TryGetValue(element, out var entry))
                {
                    watch.Elements.RemoveAt(i);
                    continue;
                }
                if (!ReferenceEquals(element.panel, panel))
                {
                    // Off this panel: it joins again on the attach that brings it back, here or on another.
                    watch.Elements.RemoveAt(i);
                    entry.Watched = ReferenceEquals(entry.Watched, panel) ? null : entry.Watched;
                    continue;
                }
                Run(element, entry);
            }
        }
    }
}
