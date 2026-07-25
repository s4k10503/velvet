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
    /// <c>priority</c> order (ties broken by subscription order). Priority affects only this ordering:
    /// Unity's own rendering is independent of this scheduler, so a higher priority does not grant the
    /// caller any control over the render loop itself.
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
        // Reused snapshot buffer for Tick's own iteration (see Tick). Grow-only: it is resized up to
        // the panel's high-water-mark subscriber count and never shrunk, so a steady-state panel (no
        // new UseFrame hosts mounting) ticks with zero allocation. Never read outside Tick itself: it
        // is a scratch field, not state, and is left in its default (all-null, matching Length) shape
        // between ticks. Nullable so Tick can null it out for the duration of its own invocation loop
        // (see the reentrancy guard there) — a null field is how a nested Tick call on this same
        // instance is told to allocate its own buffer instead of aliasing the in-flight one.
        private Subscription?[]? _tickSnapshot = Array.Empty<Subscription>();

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
            // Setting Active=false (not just removing from the list) matters because Tick's loop below
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
            // Snapshot into a reused buffer: a callback that itself
            // mounts/unmounts a UseFrame sibling (adding or removing a Subscription mid-tick) must not
            // perturb this pass's own iteration, but the snapshot itself need not be a fresh
            // allocation every tick — nothing on this path re-enters Tick for the SAME dispatcher while
            // this pass is still running. The one confirmed mid-tick escalation (an error boundary's
            // fallback swap, triggered synchronously from a throwing callback via
            // Hooks.UseFrame's own try/catch) only ever mutates _subscriptions (Unsubscribe) through
            // this call stack; Subscribe's `_tick ??=` short-circuits while _tick is already armed, and
            // nothing else on this path drives the panel's scheduler synchronously, so a second,
            // concurrent Tick call on this same instance never happens. A single reused array is
            // therefore safe: growing it (never shrinking) covers the panel's high-water-mark
            // subscriber count, and clearing it in the finally below keeps a paused/unmounted
            // subscription from being held alive by this scratch field between ticks.
            //
            // The analysis above is not enforced by any runtime check, so the buffer is still guarded
            // against reentrancy rather than trusted unconditionally: the field is captured into a
            // local and nulled out before the invocation loop runs, so a nested Tick that DID somehow
            // reach this same instance (a future call path this analysis failed to anticipate) would
            // find the field null and allocate its own buffer instead of aliasing this pass's in-flight
            // one. This costs one extra field read/write on the already-taken hot path — no allocation,
            // so steady state stays zero-alloc — and the finally below restores the field from the
            // local only if it is still null, i.e. only if no nested Tick claimed it in the meantime.
            var count = _subscriptions.Count;
            var snapshot = _tickSnapshot;
            _tickSnapshot = null;
            // The grow/copy phase lives inside the try: if the array allocation ever fails, the
            // finally must still restore whatever buffer this pass holds, or the field would be
            // abandoned at null and the next tick forced to reallocate.
            try
            {
                if (snapshot is null || snapshot.Length < count)
                {
                    snapshot = new Subscription?[count];
                }
                for (var i = 0; i < count; i++)
                {
                    snapshot[i] = _subscriptions[i];
                }

                for (var i = 0; i < count; i++)
                {
                    var subscription = snapshot[i]!;
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
                    // Per-subscriber cadence guard: a zero delta (same-frame
                    // flush) is skipped so the callback only ever observes positive, frame-sized seconds,
                    // and a hitch spike is clamped the way Time.deltaTime clamps its own.
                    if (dt <= 0f) continue;
                    dt = Mathf.Min(dt, Time.maximumDeltaTime);
                    subscription.Callback(dt);
                }
            }
            finally
            {
                // Scrubbed rather than left dangling: a stale reference here would hold a disposed
                // subscription's Callback closure (and everything it captures) alive on this instance
                // field until the buffer slot is next overwritten, well past the subscription's own
                // unmount.
                if (snapshot != null)
                {
                    var scrub = count < snapshot.Length ? count : snapshot.Length;
                    for (var i = 0; i < scrub; i++)
                    {
                        snapshot[i] = null;
                    }
                }
                // Restore only if the field is still null: a nested Tick that ran synchronously during
                // the invocation loop above would have found the field null (per the reentrancy guard),
                // allocated its own buffer, and already restored it through this same line by the time
                // this outer call resumes here. In that case this pass's buffer is simply dropped rather
                // than overwriting the nested call's — both are equivalently-sized scratch space and
                // only one can occupy the field going forward.
                _tickSnapshot ??= snapshot;
            }
        }
    }
}
