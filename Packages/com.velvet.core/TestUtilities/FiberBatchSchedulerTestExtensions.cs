using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Runs a <c>FiberBatchScheduler</c> tier drain synchronously, standing in for the UIToolkit scheduler
    /// callback that never fires in EditMode. Reached by reflection because production types carry no
    /// test-only members.
    /// <para>
    /// Each method throws <see cref="MissingMethodException"/> when the method it reflects for is gone.
    /// Throwing is the point: a caller drains to observe the re-render a queued fiber produces, so a drain
    /// that quietly reached nothing would leave it asserting on the tree as it stood before the update.
    /// </para>
    /// </summary>
    internal static class FiberBatchSchedulerTestExtensions
    {
        private const string DrainImmediateMethodName = "DrainImmediate";
        private const string DrainDelayedMethodName = "DrainDelayed";
        private const string RunImmediateCallbackMethodName = "RunImmediateCallback";

        /// <summary>Drains the Normal / Urgent tier.</summary>
        // Bypasses: the panel scheduler callback: production registers RunImmediateCallback, which also retires the tier's registration and marks a scheduler pass; this drains as FlushImmediate and the Transition tier's drain do, doing neither (RunImmediateCallbackForTest runs the callback itself).
        internal static void DrainImmediateForTest(this FiberBatchScheduler scheduler)
            => Drain(scheduler, DrainImmediateMethodName);

        /// <summary>Runs the callback the Normal / Urgent tier registers with the panel scheduler.</summary>
        // Bypasses: the panel scheduler that decides when it runs.
        internal static void RunImmediateCallbackForTest(this FiberBatchScheduler scheduler)
            => Drain(scheduler, RunImmediateCallbackMethodName);

        /// <summary>Drains every Transition-tier entry, after whatever the Normal / Urgent tier still holds.</summary>
        // Bypasses: the panel callbacks and their admission: a panel callback drains only the entries waiting for its own registration; VelvetPreviewHost.Settle is the production caller that drains the whole tier this way.
        internal static void DrainDelayedForTest(this FiberBatchScheduler scheduler)
            => Drain(scheduler, DrainDelayedMethodName);

        private static void Drain(FiberBatchScheduler scheduler, string methodName)
        {
            // Type.EmptyTypes pins the no-argument overload, so a future drain that gains a budget parameter
            // is a miss rather than a silent bind to a signature the caller never meant.
            var method = typeof(FiberBatchScheduler).GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null)
            {
                throw new MissingMethodException(typeof(FiberBatchScheduler).FullName, methodName);
            }
            // The helper hands callers the drain's own exception, so no call site unwraps one.
            try
            {
                method.Invoke(scheduler, BindingFlags.DoNotWrapExceptions, null, null, null);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
            }
        }
    }
}
