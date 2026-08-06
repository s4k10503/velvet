using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    internal interface IPoolableVelvetTaskSource
    {
        bool IsPooled { get; }

        void MarkPooled();

        void ClearPooled();
    }

    internal static class VelvetTaskSourcePool
    {
        const int MaxPoolSize = 64;

        static readonly Stack<VelvetTaskSource> VoidSources = new();

        internal static VelvetTaskSource Rent()
        {
            if (VoidSources.Count == 0)
            {
                return new VelvetTaskSource();
            }

            var source = VoidSources.Pop();
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
            if (VoidSources.Count < MaxPoolSize)
            {
                VoidSources.Push(source);
            }
        }
    }

    internal static class VelvetTaskSourcePool<T>
    {
        const int MaxPoolSize = 64;

        static readonly Stack<VelvetTaskSource<T>> Sources = new();

        internal static VelvetTaskSource<T> Rent()
        {
            if (Sources.Count == 0)
            {
                return new VelvetTaskSource<T>();
            }

            var source = Sources.Pop();
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
            if (Sources.Count < MaxPoolSize)
            {
                Sources.Push(source);
            }
        }
    }

    internal static class YieldVelvetTaskSourcePool
    {
        const int MaxPoolSize = 64;

        static readonly Stack<YieldVelvetTaskSource> Sources = new();

        internal static YieldVelvetTaskSource Rent()
        {
            YieldVelvetTaskSource source;
            if (Sources.Count == 0)
            {
                source = new YieldVelvetTaskSource();
            }
            else
            {
                source = Sources.Pop();
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
            if (Sources.Count < MaxPoolSize)
            {
                Sources.Push(source);
            }
        }
    }

    internal static class AwaitableVelvetTaskSourcePool
    {
        const int MaxPoolSize = 64;

        static readonly Stack<AwaitableVelvetTaskSource> Sources = new();

        internal static AwaitableVelvetTaskSource Rent(Awaitable awaitable)
        {
            AwaitableVelvetTaskSource source;
            if (Sources.Count == 0)
            {
                source = new AwaitableVelvetTaskSource();
            }
            else
            {
                source = Sources.Pop();
                source.ClearPooled();
                source.ResetForPool();
            }

            source.Initialize(awaitable);
            return source;
        }

        internal static void Return(AwaitableVelvetTaskSource source)
        {
            if (source.IsPooled)
            {
                throw new InvalidOperationException("The AwaitableVelvetTaskSource has already been returned to the pool.");
            }

            source.MarkPooled();
            if (Sources.Count < MaxPoolSize)
            {
                Sources.Push(source);
            }
        }
    }

    internal static class AwaitableVelvetTaskSourcePool<T>
    {
        const int MaxPoolSize = 64;

        static readonly Stack<AwaitableVelvetTaskSource<T>> Sources = new();

        internal static AwaitableVelvetTaskSource<T> Rent(Awaitable<T> awaitable)
        {
            AwaitableVelvetTaskSource<T> source;
            if (Sources.Count == 0)
            {
                source = new AwaitableVelvetTaskSource<T>();
            }
            else
            {
                source = Sources.Pop();
                source.ClearPooled();
                source.ResetForPool();
            }

            source.Initialize(awaitable);
            return source;
        }

        internal static void Return(AwaitableVelvetTaskSource<T> source)
        {
            if (source.IsPooled)
            {
                throw new InvalidOperationException("The AwaitableVelvetTaskSource has already been returned to the pool.");
            }

            source.MarkPooled();
            if (Sources.Count < MaxPoolSize)
            {
                Sources.Push(source);
            }
        }
    }
}
