using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Puts <c>FiberWorkLoop</c>'s count of async actions in flight back to none. The count is process-wide, so
    /// an action another fixture leaves awaiting on a component it never unmounts would otherwise decide who
    /// owns an optimistic entry a later case adds outside every transition. Reached by reflection because
    /// production types carry no test-only members.
    /// </summary>
    internal static class AsyncActionsInFlightTestAccess
    {
        internal const string CountFieldName = "s_asyncActionsInFlight";
        internal const string OwnerFieldName = "AsyncActionsInFlight";

        /// <summary>
        /// Zeroes the count, and through the owner slot's cleared <c>isPending</c> retires whatever entries it
        /// held. A tree without the count has nothing to reset, so this returns there rather than throwing:
        /// it runs from a fixture's set-up, where a throw would take down every case beside the one
        /// UseOptimisticTests keeps for the field going missing.
        /// </summary>
        // Bypasses: the completion, or the unmount giving the action up, that counts each action out in production.
        internal static void ResetForTest()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var count = typeof(FiberWorkLoop).GetField(CountFieldName, flags);
            var owner = typeof(FiberWorkLoop).GetField(OwnerFieldName, flags)?.GetValue(null) as HookTransitionSlot;
            if (count == null || owner == null)
            {
                return;
            }
            count.SetValue(null, 0);
            owner.IsPending = false;
        }
    }
}
