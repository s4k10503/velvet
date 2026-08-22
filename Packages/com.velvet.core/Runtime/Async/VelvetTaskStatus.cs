using System.Runtime.CompilerServices;

namespace Velvet
{
    public enum VelvetTaskStatus
    {
        Pending = 0,
        Succeeded = 1,
        Faulted = 2,
        Canceled = 3,
    }

    public static class VelvetTaskStatusExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompleted(this VelvetTaskStatus status) => status != VelvetTaskStatus.Pending;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompletedSuccessfully(this VelvetTaskStatus status) => status == VelvetTaskStatus.Succeeded;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCanceled(this VelvetTaskStatus status) => status == VelvetTaskStatus.Canceled;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFaulted(this VelvetTaskStatus status) => status == VelvetTaskStatus.Faulted;
    }
}
