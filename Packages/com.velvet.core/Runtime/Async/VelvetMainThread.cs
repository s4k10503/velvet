using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Velvet
{
    internal static class VelvetMainThread
    {
        readonly struct Handoff
        {
            internal readonly Action<object?> Continuation;
            internal readonly object? State;

            internal Handoff(Action<object?> continuation, object? state)
            {
                Continuation = continuation;
                State = state;
            }
        }

        static readonly SendOrPostCallback RunHandoffsCallback = static _ => RunHandoffs();
        static readonly object Gate = new();
        static List<Handoff> _pending = new();
        static List<Handoff> _running = new();
        static int _threadId;
        static SynchronizationContext? _context;

        internal static bool IsCurrent => Thread.CurrentThread.ManagedThreadId == _threadId;

        internal static void Post(Action<object?> continuation, object? state)
        {
            lock (Gate)
            {
                _pending.Add(new Handoff(continuation, state));
            }

            _context!.Post(RunHandoffsCallback, null);
        }

        // Main thread only. A continuation that throws must not end the loop: the batch would stay in
        // the list the next swap hands back to Post, and what had already run would run again.
        internal static void RunHandoffs()
        {
            List<Handoff> batch;
            lock (Gate)
            {
                batch = _pending;
                _pending = _running;
                _running = batch;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                try
                {
                    batch[i].Continuation(batch[i].State);
                }
                catch (Exception exception)
                {
                    VelvetTaskScheduler.PublishUnobservedException(exception);
                }
            }

            batch.Clear();
        }

        // Captured here rather than on first use, where a thread-pool thread could be the first caller.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        static void Capture()
        {
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _context = SynchronizationContext.Current;
            lock (Gate)
            {
                _pending.Clear();
            }
        }
    }
}
