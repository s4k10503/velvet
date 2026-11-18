using System;
using System.Collections.Generic;

namespace Velvet
{
    internal interface IPoolableVelvetTaskSource
    {
        bool IsPooled { get; }

        void MarkPooled();

        void ClearPooled();
    }

    // The stack is the main thread's alone: another thread's rent allocates and its return drops the
    // item. A lock would make that thread safe to pool on too, at a cost every main-thread rent pays.
    internal sealed class MainThreadPool<T>
        where T : class
    {
        const int MaxPoolSize = 64;

        readonly Stack<T> _items = new();

        internal T? Rent() => VelvetMainThread.IsCurrent && _items.Count > 0 ? _items.Pop() : null;

        internal void Return(T item)
        {
            if (VelvetMainThread.IsCurrent && _items.Count < MaxPoolSize)
            {
                _items.Push(item);
            }
        }
    }

    internal static class VelvetTaskSourcePool
    {
        static readonly MainThreadPool<VelvetTaskSource> VoidSources = new();

        internal static VelvetTaskSource Rent()
        {
            var source = VoidSources.Rent();
            if (source == null)
            {
                return new VelvetTaskSource();
            }

            source.ClearPooled();
            source.ResetForPool();
            return source;
        }

        internal static void Return(VelvetTaskSource source)
        {
            if (source.IsPooled)
            {
                throw new InvalidOperationException("The VelvetTaskSource has already been returned to the pool.");
            }

            source.MarkPooled();
            VoidSources.Return(source);
        }
    }

    internal static class VelvetTaskSourcePool<T>
    {
        static readonly MainThreadPool<VelvetTaskSource<T>> Sources = new();

        internal static VelvetTaskSource<T> Rent()
        {
            var source = Sources.Rent();
            if (source == null)
            {
                return new VelvetTaskSource<T>();
            }

            source.ClearPooled();
            source.ResetForPool();
            return source;
        }

        internal static void Return(VelvetTaskSource<T> source)
        {
            if (source.IsPooled)
            {
                throw new InvalidOperationException("The VelvetTaskSource has already been returned to the pool.");
            }

            source.MarkPooled();
            Sources.Return(source);
        }
    }

    internal static class YieldVelvetTaskSourcePool
    {
        static readonly MainThreadPool<YieldVelvetTaskSource> Sources = new();

        internal static YieldVelvetTaskSource Rent()
        {
            var source = Sources.Rent();
            if (source == null)
            {
                source = new YieldVelvetTaskSource();
            }
            else
            {
                source.ClearPooled();
                source.ResetForPool();
            }

            source.Activate();
            return source;
        }

        internal static void Return(YieldVelvetTaskSource source)
        {
            if (source.IsPooled)
            {
                throw new InvalidOperationException("The YieldVelvetTaskSource has already been returned to the pool.");
            }

            source.MarkPooled();
            Sources.Return(source);
        }
    }
}
