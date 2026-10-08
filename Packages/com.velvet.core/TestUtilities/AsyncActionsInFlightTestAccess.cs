using System.Linq;
using System.Reflection;
using UnityEngine;

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

        /// <summary>
        /// The reset production runs at subsystem registration, found by that attribute so a rename of the
        /// method cannot leave this resetting nothing.
        /// </summary>
        internal static MethodInfo FindSubsystemReset()
            => typeof(FiberWorkLoop)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SingleOrDefault(method =>
                    method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType
                    == RuntimeInitializeLoadType.SubsystemRegistration);

        /// <summary>
        /// Runs the production reset. A tree without it has nothing to reset, so this returns there rather than
        /// throwing: it runs from a fixture's set-up, where a throw would take down every case beside the one
        /// UseOptimisticTests keeps for the method going missing.
        /// </summary>
        // Bypasses: the completion that counts each action out in production.
        internal static void ResetForTest() => FindSubsystemReset()?.Invoke(null, null);

        internal static int CountForTest()
            => (int)typeof(FiberWorkLoop).GetField(CountFieldName, BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null);
    }
}
