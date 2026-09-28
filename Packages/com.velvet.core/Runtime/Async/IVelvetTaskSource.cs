using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

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

    // Implemented only by a source whose fault can hold more than one exception. Reading must not consume.
    internal interface IVelvetTaskFaults
    {
        IReadOnlyList<ExceptionDispatchInfo>? GetFaults(short version);
    }
}
