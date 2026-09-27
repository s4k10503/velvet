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
        static int _threadId;
        static SynchronizationContext? _context;

        // Before Capture runs, a caller is taken for the main thread only when it holds Unity's context,
        // so that code running ahead of Capture on the main thread is not refused as off it.
        internal static bool IsCurrent
        {
            get
            {
                if (_threadId == 0 && SynchronizationContext.Current?.GetType().FullName == UnityContextTypeName)
                {
                    CaptureThread();
                }

                return Thread.CurrentThread.ManagedThreadId == _threadId;
            }
        }

        const string UnityContextTypeName = "UnityEngine.UnitySynchronizationContext";

        internal static void Post(Action<object?> continuation, object? state)
        {
            lock (Gate)
            {
                _pending.Add(new Handoff(continuation, state));
            }

            _context!.Post(RunHandoffsCallback, null);
        }

        // Main thread only. Each batch leaves in a list of its own, so a handoff that pumps Unity's
        // context and reaches this again drains only what was posted since, and no list being walked is
        // one Post can reach. A continuation that throws must not end the walk, or the rest of its batch
        // would never run.
        internal static void RunHandoffs()
        {
            List<Handoff> batch;
            lock (Gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = _pending;
                _pending = new List<Handoff>();
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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [InitializeOnLoadMethod]
#endif
        static void Capture()
        {
            CaptureThread();
            lock (Gate)
            {
                _pending.Clear();
            }
        }

        static void CaptureThread()
        {
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _context = SynchronizationContext.Current;
        }
    }
}
