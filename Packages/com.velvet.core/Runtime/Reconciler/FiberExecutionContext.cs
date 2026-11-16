#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // React's executionContext (RenderContext | CommitContext): one flag for every root, set while an entry renders,
    // commits or flushes passive effects. A door is the method something outside Velvet's render and commit calls —
    // the panel scheduler, a UI Toolkit callback, the main-thread pump, a public or test entry. Reached while the flag
    // is clear, a door runs its body as the outermost entry; reached while it is set, it is held, and what is held runs
    // once the outermost entry has ended, in the order it was asked for. Velvet's render and commit code calls the
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

        private const int MaxLoopingRunsPerOwner = 50;

        private static bool s_set;
        private static bool s_releasing;
        private static readonly List<Held> s_held = new();
        private static readonly List<FiberBatchScheduler> s_owingSyncLane = new();
        private static readonly Dictionary<object, int> s_runs = new();
        private static readonly HashSet<object> s_capped = new();
        private static readonly object s_noOwner = new();

        internal static bool IsSet => s_set;

        internal static void Enter<TState>(FiberBatchScheduler? owner, TState state, Action<TState> body)
        {
            Begin();
            try
            {
                body(state);
            }
            finally
            {
                End(owner);
            }
        }

        internal static TResult Enter<TState, TResult>(FiberBatchScheduler? owner, TState state, Func<TState, TResult> body)
        {
            Begin();
            try
            {
                return body(state);
            }
            finally
            {
                End(owner);
            }
        }

        private static void Begin()
        {
            UnityEngine.Debug.Assert(!s_set, "An entry began while another renders or commits.");
            UnityEngine.Debug.Assert(s_releasing || s_held.Count == 0, "Work is held with no entry left to release it.");
            s_set = true;
        }

        private static void End(FiberBatchScheduler? owner)
        {
            s_set = false;
            Release(owner);
        }

        // run reaches the door again, which the release enters with the flag clear. It is built by the caller only
        // where it holds, so that a door allocates nothing while it enters.
        internal static void Hold(Request kind, object? key, FiberBatchScheduler? owner, Action run)
        {
            UnityEngine.Debug.Assert(s_set, "Work was held with no entry on the stack to release it.");
            if (key != null && IsHeld(kind, key)) return;
            s_held.Add(new Held(kind, key, owner, run));
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

        private static bool Loops(Request kind) => kind != Request.Mount && kind != Request.Unmount;

        // Non-reentrant, as React's flushSyncWorkAcrossRoots is: an item's own entry ends inside this loop without
        // releasing, and what it holds is appended behind the items already waiting. Each item runs in its own try, and
        // what one throws is logged, so it neither stops the items behind it nor becomes the failure of the entry that
        // released it.
        private static void Release(FiberBatchScheduler? owner)
        {
            if (s_releasing) return;
            if (s_held.Count == 0 && !OwesSyncLane()) return;
            s_releasing = true;
            var last = owner;
            try
            {
                while (true)
                {
                    Request kind;
                    FiberBatchScheduler? target;
                    Action? run = null;
                    if (s_held.Count > 0)
                    {
                        var item = s_held[0];
                        s_held.RemoveAt(0);
                        kind = item.Kind;
                        target = item.Owner;
                        run = item.Run;
                    }
                    else
                    {
                        target = TakeOwingSyncLane();
                        if (target == null) break;
                        kind = Request.Flush;
                    }
                    if (target is { IsDisposed: true }) continue;
                    if (Loops(kind) && !Counted(target)) continue;
                    // React's performSyncWorkOnRoot starts by flushing the passive effects of the commit before it.
                    if (last is { IsDisposed: false }) RunIsolated(last, static scheduler => scheduler.FlushPendingPassiveEffects());
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
            }
        }

        // Counts a run toward the owner's bound. Past it, the owner's looping work is dropped for this release and the
        // nested-update error logged once; a mount and an unmount are not counted, since nothing asks for either again.
        private static bool Counted(FiberBatchScheduler? owner)
        {
            var key = (object?)owner ?? s_noOwner;
            if (s_capped.Contains(key)) return false;
            s_runs.TryGetValue(key, out var runs);
            s_runs[key] = ++runs;
            if (runs <= MaxLoopingRunsPerOwner) return true;
            s_capped.Add(key);
            for (var i = s_held.Count - 1; i >= 0; i--)
            {
                if (Loops(s_held[i].Kind) && ReferenceEquals((object?)s_held[i].Owner ?? s_noOwner, key)) s_held.RemoveAt(i);
            }
            if (owner != null) s_owingSyncLane.Remove(owner);
            UnityEngine.Debug.LogException(new InvalidOperationException(
                "Maximum update depth exceeded. A root kept asking for work while another rendered or committed, "
                + $"{MaxLoopingRunsPerOwner} times in a row. Velvet limits it, as React limits nested updates, to "
                + "prevent infinite loops."));
            return false;
        }

        private static bool OwesSyncLane()
        {
            for (var i = 0; i < s_owingSyncLane.Count; i++)
            {
                if (s_owingSyncLane[i].OwesSyncLane) return true;
            }
            s_owingSyncLane.Clear();
            return false;
        }

        private static FiberBatchScheduler? TakeOwingSyncLane()
        {
            while (s_owingSyncLane.Count > 0)
            {
                var scheduler = s_owingSyncLane[0];
                s_owingSyncLane.RemoveAt(0);
                if (scheduler.OwesSyncLane && !s_capped.Contains(scheduler)) return scheduler;
            }
            return null;
        }

        private static void RunIsolated<TState>(TState state, Action<TState> run)
        {
            try
            {
                run(state);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}
