#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One dispatcher per panel: a single stable scheduled tick, hosted on the panel's own root element
    /// (never itself subject to a keyed reorder), fanning out every frame to every live
    /// <see cref="Hooks.UseFrame(System.Action{float}, int)"/> subscriber in that panel, in
    /// <c>priority</c> order (ties broken by subscription order) — r3f's
    /// <c>useFrame(callback, renderPriority)</c> ordering parity. A positive priority carries no other
    /// side effect here (unlike r3f, where it also hands the caller manual control of the render loop):
    /// Unity's own rendering is independent of this scheduler, so there is no internal render call to
    /// take over.
    /// </summary>
    /// <remarks>
    /// Subscribing per-PANEL rather than per-component-HOST (the one scheduled item per component this
    /// replaced) is what makes order both deterministic and stable across a keyed reorder: a transient
    /// detach only flips <see cref="Subscription.Active"/> off and back on — the slot in the ordered list
    /// is never vacated — where a plain per-element <c>IVisualElementScheduledItem</c> is re-appended to
    /// the end of UI Toolkit's own internal scheduler list on every re-attach (verified by decompiling
    /// <c>TimerEventScheduler</c>/<c>BaseVisualElementScheduledItem</c>), silently reshuffling order.
    /// </remarks>
    internal sealed class UseFrameDispatcher
    {
        internal sealed class Subscription
        {
            public int Priority;
            public bool Active;
            internal readonly long Sequence;
            internal readonly Action<float> Callback;
            // Null until this subscription has been through one Tick pass. A per-subscription baseline —
            // not the shared tick item's own start/now — is what a late joiner needs: without it, a
            // subscriber that joins a panel where an EARLIER subscriber has been ticking for a while
            // would inherit that earlier subscriber's elapsed-since-last-fire on its own first tick
            // (measured and confirmed: a panel stalled 500ms before a second host mounts hands that host a
            // dt of Time.maximumDeltaTime on its very first-ever callback, not a small "just mounted" one).
            internal long? LastTimeMs;

            internal Subscription(long sequence, int priority, Action<float> callback)
            {
                Sequence = sequence;
                Priority = priority;
                Callback = callback;
                Active = true;
            }
        }

        // Keyed by the panel itself (not the caller) and weak on that key so a destroyed panel's
        // dispatcher — and the scheduled tick it owns — becomes collectible without any explicit
        // teardown hook; UI Toolkit's own panel disposal already stops delivering scheduler updates to
        // an item whose host panel is gone.
        private static readonly ConditionalWeakTable<IPanel, UseFrameDispatcher> s_perPanel = new();

        private readonly IPanel _panel;
        private readonly List<Subscription> _subscriptions = new();
        private IVisualElementScheduledItem? _tick;
        private long _nextSequence;

        private UseFrameDispatcher(IPanel panel)
        {
            _panel = panel;
        }

        internal static UseFrameDispatcher GetOrCreate(IPanel panel)
        {
            return s_perPanel.GetValue(panel, static p => new UseFrameDispatcher(p));
        }

        internal Subscription Subscribe(int priority, Action<float> callback)
        {
            var subscription = new Subscription(_nextSequence++, priority, callback);
            _subscriptions.Add(subscription);
            // Every(0): fires once per panel scheduler update (not a wall-clock interval) — the
            // per-update cadence Hooks.UseFrame documents. One shared item drives every subscriber's own
            // Tick pass, but each still measures its OWN elapsed time via Subscription.LastTimeMs, not
            // this item's.
            _tick ??= _panel.visualTree.schedule.Execute((TimerState ts) => Tick(ts)).Every(0);
            return subscription;
        }

        internal void Unsubscribe(Subscription subscription)
        {
            // Setting Active=false (not just removing from the list) matters because Tick's foreach below
            // runs over a SNAPSHOT taken at the start of that tick: a callback that synchronously disposes
            // a later-sorted sibling this same tick (an error boundary's fallback swap unmounting it
            // mid-iteration) can reach this Unsubscribe call before the snapshot's own walk gets to that
            // sibling's entry — the snapshot still holds the reference, and Active is what stops it firing
            // posthumously on an already-disposed subscriber.
            subscription.Active = false;
            _subscriptions.Remove(subscription);
            if (_subscriptions.Count == 0)
            {
                _tick?.Pause();
                _tick = null;
            }
        }

        private void Tick(TimerState ts)
        {
            // domIndices-free re-sort every tick: subscriber counts here are dozens at most, and a
            // sort over that is not worth tracking a dirty flag to skip. (Priority, Sequence) is a
            // strictly unique compound key (Sequence never repeats), so an unstable Array.Sort under
            // List.Sort still yields one deterministic order — no tie-break subtlety to worry about.
            _subscriptions.Sort(static (a, b) =>
            {
                var byPriority = a.Priority.CompareTo(b.Priority);
                return byPriority != 0 ? byPriority : a.Sequence.CompareTo(b.Sequence);
            });
            // Snapshot via ToArray: a callback that itself mounts/unmounts a UseFrame sibling (adding
            // or removing a Subscription mid-tick) must not perturb this pass's own iteration.
            foreach (var subscription in _subscriptions.ToArray())
            {
                // LastTimeMs is not refreshed while inactive, so a pause accumulates real elapsed time
                // rather than freezing it — reactivating hands the next callback a dt spanning the WHOLE
                // paused interval (still clamped below like any other). The only currently-reachable pause
                // is a same-frame keyed-reorder detach+reattach (zero elapsed time either way), so this
                // choice is unobserved today; it is deliberate, not an oversight, should a future pause
                // ever span real time.
                if (!subscription.Active) continue;

                // Per-subscription delta (see Subscription.LastTimeMs) rather than one dt shared by the
                // whole panel: a subscription with no baseline yet only records one here and skips
                // invoking this pass, matching a freshly-armed engine scheduled item's own zero-delta
                // first fire — the callback only ever observes a real elapsed span it was actually
                // present for.
                if (subscription.LastTimeMs is not { } lastMs)
                {
                    subscription.LastTimeMs = ts.now;
                    continue;
                }
                var dt = (ts.now - lastMs) / 1000f;
                subscription.LastTimeMs = ts.now;
                // Mirrors the per-subscriber cadence guard this replaced: a zero delta (same-frame
                // flush) is skipped so the callback only ever observes positive, frame-sized seconds,
                // and a hitch spike is clamped the way Time.deltaTime clamps its own.
                if (dt <= 0f) continue;
                dt = Mathf.Min(dt, Time.maximumDeltaTime);
                subscription.Callback(dt);
            }
        }
    }
}
