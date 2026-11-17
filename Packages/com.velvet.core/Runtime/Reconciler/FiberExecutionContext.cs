#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // React's executionContext (RenderContext | CommitContext): one flag for every root, set while an entry renders,
    // commits or flushes passive effects. A door is the method something outside Velvet's render and commit calls —
    // the panel scheduler, a UI Toolkit callback, the main-thread pump, a public or test entry. Reached while the flag
    // is clear, a door runs its body as the outermost entry. Reached while it is set, it is held, and what is held runs
    // once the outermost entry has ended, in the order it was asked for; a flush of a scheduler whose own drain is on the
    // stack is left to that drain instead (FiberBatchScheduler.HoldFlush). Velvet's render and commit code calls the
    // body, never the door, which is what makes a call sub-work of the entry on the stack;
    // scripts/test_quality/entry_sites_check.py holds the methods that may call a door.
    internal static class FiberExecutionContext
    {
        // What a held request asks for: a request of one kind for one key is held once while it waits.
        internal enum Request
        {
            Flush,
            DelayedFlush,
            FiberFlush,
            PassiveFlush,
            Resume,
            Resolve,
            Range,
            Reconcile,
            Mount,
            Unmount,
        }

        private readonly struct Held
        {
            internal readonly Request Kind;
            internal readonly object? Key;
            internal readonly FiberBatchScheduler? Owner;
            internal readonly Action Run;

            internal Held(Request kind, object? key, FiberBatchScheduler? owner, Action run)
            {
                Kind = kind;
                Key = key;
                Owner = owner;
                Run = run;
            }
        }

        private const int MaxRunsPerRequest = 50;

        private static bool s_set;
        private static bool s_releasing;
        private static readonly List<Held> s_held = new(8);
        private static readonly List<FiberBatchScheduler> s_owingSyncLane = new(4);
        private static readonly Dictionary<(Request Kind, object Key), int> s_runs = new();
        private static readonly HashSet<(Request Kind, object Key)> s_capped = new();
        private static readonly HashSet<FiberBatchScheduler> s_threw = new();

        internal static bool IsSet => s_set;

        internal static bool Threw(FiberBatchScheduler scheduler) => s_threw.Contains(scheduler);

        internal static void Enter<TState>(FiberBatchScheduler? owner, TState state, Action<TState> entry)
        {
            Begin();
            var completed = false;
            try
            {
                entry(state);
                completed = true;
            }
            finally
            {
                End(owner, completed);
            }
        }

        internal static TResult Enter<TState, TResult>(FiberBatchScheduler? owner, TState state, Func<TState, TResult> entry)
        {
            Begin();
            var completed = false;
            try
            {
                var result = entry(state);
                completed = true;
                return result;
            }
            finally
            {
                End(owner, completed);
            }
        }

        private static void Begin()
        {
            UnityEngine.Debug.Assert(!s_set, "An entry began while another renders or commits.");
            UnityEngine.Debug.Assert(s_releasing || s_held.Count == 0, "Work is held with no entry left to release it.");
            s_set = true;
        }

        private static void End(FiberBatchScheduler? owner, bool completed)
        {
            s_set = false;
            // Ahead of the release, which returns at once where this entry is an item of one already running.
            if (!completed && owner != null) s_threw.Add(owner);
            Release(owner);
        }

        // run reaches the door again, which the release enters with the flag clear.
        internal static void Hold(Request kind, object? key, FiberBatchScheduler? owner, Action run)
        {
            UnityEngine.Debug.Assert(s_set, "Work was held with no entry on the stack to release it.");
            if (key != null && IsHeld(kind, key)) return;
            s_held.Add(new Held(kind, key, owner, run));
        }

        // As Hold, for a door whose request carries state: the closure is built only where it is held.
        internal static void Hold<TState>(Request kind, object? key, FiberBatchScheduler? owner, TState state, Action<TState> run)
        {
            if (key != null && IsHeld(kind, key)) return;
            Hold(kind, key, owner, new HeldRun<TState>(state, run).Run);
        }

        private sealed class HeldRun<TState>
        {
            private readonly TState _state;
            private readonly Action<TState> _run;

            internal HeldRun(TState state, Action<TState> run)
            {
                _state = state;
                _run = run;
            }

            internal void Run() => _run(_state);
        }

        internal static bool IsHeld(Request kind, object key)
        {
            for (var i = 0; i < s_held.Count; i++)
            {
                if (s_held[i].Kind == kind && ReferenceEquals(s_held[i].Key, key)) return true;
            }
            return false;
        }

        internal static void Drop(Request kind, object key)
        {
            for (var i = s_held.Count - 1; i >= 0; i--)
            {
                if (s_held[i].Kind == kind && ReferenceEquals(s_held[i].Key, key)) s_held.RemoveAt(i);
            }
        }

        // A scheduler whose immediate tier holds sync-lane work, which the end of the outermost entry renders, as
        // React's commit ends in flushSyncWorkOnAllRoots whether or not anything was asked for meanwhile.
        internal static void OweSyncLane(FiberBatchScheduler scheduler)
        {
            if (!s_owingSyncLane.Contains(scheduler)) s_owingSyncLane.Add(scheduler);
        }

        // Non-reentrant, as React's flushSyncWorkAcrossRoots is: an item's own entry ends inside this loop without
        // releasing, and what it holds is appended behind the items already waiting. Each item runs in its own try, and
        // what one throws is logged, so it neither stops the items behind it nor becomes the failure of the entry that
        // released it.
        //
        // Once an exception has left an entry of a scheduler, the outermost entry or one an item of this release opened,
        // this release drains that scheduler no further: not for the sync-lane work it owes, not for a held flush of it,
        // and not through the passive flush ahead of the next item, which ends by draining its sync lane
        // (FiberEffects.DrainPassiveEffects). Drained, it would run that root's work again inside the call the exception
        // left. The frame-boundary drain the scheduler registered commits that work instead, which is where
        // FiberBatchScheduler bounds a run of drains that throw. Held work of other kinds still runs.
        private static void Release(FiberBatchScheduler? owner)
        {
            if (s_releasing) return;
            if (s_held.Count == 0 && IndexOfOwing() < 0)
            {
                s_threw.Clear();
                return;
            }
            s_releasing = true;
            var last = owner;
            try
            {
                while (true)
                {
                    Request kind;
                    object? key;
                    FiberBatchScheduler? target;
                    Action? run = null;
                    if (s_held.Count > 0)
                    {
                        var item = s_held[0];
                        s_held.RemoveAt(0);
                        kind = item.Kind;
                        key = item.Key;
                        target = item.Owner;
                        run = item.Run;
                    }
                    else
                    {
                        target = TakeOwingSyncLane();
                        if (target == null) break;
                        // Keyed as FiberBatchScheduler.HoldFlush keys the same flush, so the two count as one request.
                        kind = Request.Flush;
                        key = target;
                    }
                    if (target is { IsDisposed: true }) continue;
                    if (target != null && s_threw.Contains(target) && IsDrain(kind)) continue;
                    if (!Counted(kind, key)) continue;
                    // React's performSyncWorkOnRoot starts by flushing the passive effects of the commit before it.
                    if (last is { IsDisposed: false } && !s_threw.Contains(last))
                    {
                        RunIsolated(last, static scheduler => scheduler.FlushPendingPassiveEffects());
                    }
                    if (run != null) RunIsolated(run, static held => held());
                    else RunIsolated(target!, static scheduler => scheduler.FlushImmediate());
                    last = target ?? last;
                }
            }
            finally
            {
                s_releasing = false;
                s_runs.Clear();
                s_capped.Clear();
                s_threw.Clear();
            }
        }

        // Counts a run of one request, its kind and key, toward the bound. Past it, that request is skipped wherever it is
        // reached for the rest of this release, and the nested-update error logged once; the rest of what is held, its
        // owner's included, still runs. A request with no key is not the same request as any other, so it is not counted.
        private static bool Counted(Request kind, object? key)
        {
            if (key == null) return true;
            var request = (kind, key);
            if (s_capped.Contains(request)) return false;
            s_runs.TryGetValue(request, out var runs);
            s_runs[request] = ++runs;
            if (runs <= MaxRunsPerRequest) return true;
            s_capped.Add(request);
            UnityEngine.Debug.LogException(new InvalidOperationException(
                "Maximum update depth exceeded. Work that waited for a root's render or commit to end ran "
                + $"{MaxRunsPerRequest} times before control returned, and was asked for again. Velvet does not run it "
                + "again before control returns, as React limits nested updates, and the updates still queued for it "
                + "stay queued."));
            return false;
        }

        // The first listed scheduler that owes the sync lane and has not thrown in this release, or -1. One that owes nothing
        // any more is delisted on the way; one that threw stays listed, for the release of a later entry to flush.
        private static int IndexOfOwing()
        {
            for (var i = 0; i < s_owingSyncLane.Count;)
            {
                var scheduler = s_owingSyncLane[i];
                if (!scheduler.OwesSyncLane) s_owingSyncLane.RemoveAt(i);
                else if (s_threw.Contains(scheduler)) i++;
                else return i;
            }
            return -1;
        }

        private static FiberBatchScheduler? TakeOwingSyncLane()
        {
            var index = IndexOfOwing();
            if (index < 0) return null;
            var scheduler = s_owingSyncLane[index];
            s_owingSyncLane.RemoveAt(index);
            return scheduler;
        }

        private static bool IsDrain(Request kind)
            => kind is Request.Flush or Request.FiberFlush or Request.PassiveFlush;

        private static void RunIsolated<TState>(TState state, Action<TState> run)
        {
            try
            {
                run(state);
            }
            catch (Exception thrown)
            {
                UnityEngine.Debug.LogException(thrown);
            }
        }
    }
}
