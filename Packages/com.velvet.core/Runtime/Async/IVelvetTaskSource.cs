using System;

namespace Velvet
{
    internal interface IVelvetTaskSource
    {
        short Version { get; }

        VelvetTaskStatus GetStatus(short version);

        void OnCompleted(Action<object?> continuation, object? state, short version);

        void GetResult(short version);
    }

    internal interface IVelvetTaskSource<out T> : IVelvetTaskSource
    {
        new T GetResult(short version);
    }
}
