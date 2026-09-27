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
        static bool _draining;

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

        // Main thread only. A handoff can pump Unity's context and so reach this again: the nested call
        // returns and the outer one loops until nothing is pending, so a list being walked is never
        // handed back to Post. A continuation that throws must not end the walk either: the list would go
        // back to Post uncleared, and what had already run would run again.
        internal static void RunHandoffs()
        {
            if (_draining)
            {
                return;
            }

            _draining = true;
            try
            {
                while (true)
                {
                    lock (Gate)
                    {
                        if (_pending.Count == 0)
                        {
                            return;
                        }

                        (_pending, _running) = (_running, _pending);
                    }

                    for (var i = 0; i < _running.Count; i++)
                    {
                        try
                        {
                            _running[i].Continuation(_running[i].State);
                        }
                        catch (Exception exception)
                        {
                            VelvetTaskScheduler.PublishUnobservedException(exception);
                        }
                    }

                    _running.Clear();
                }
            }
            finally
            {
                _draining = false;
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
