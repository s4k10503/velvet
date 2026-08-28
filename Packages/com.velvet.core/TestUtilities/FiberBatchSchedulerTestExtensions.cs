using System;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Runs a <c>FiberBatchScheduler</c> tier drain synchronously, standing in for the UIToolkit scheduler
    /// callback that never fires in EditMode. Reached by reflection because production types carry no
    /// test-only members.
    /// <para>
    /// Both methods throw <see cref="MissingMethodException"/> when the drain they reflect for is gone.
    /// Throwing is the point: a caller drains to observe the re-render a queued fiber produces, so a drain
    /// that quietly reached nothing would leave it asserting on the tree as it stood before the update.
    /// </para>
    /// <para>
    /// An exception escaping a drain arrives wrapped in <see cref="System.Reflection.TargetInvocationException"/>,
    /// which no caller observes today because none wraps a drain in <c>Assert.Throws</c>. The first one that
    /// does wants <c>BindingFlags.DoNotWrapExceptions</c> added below rather than an unwrap at the call site.
    /// </para>
    /// </summary>
    internal static class FiberBatchSchedulerTestExtensions
    {
        private const string DrainImmediateMethodName = "DrainImmediate";
        private const string DrainDelayedMethodName = "DrainDelayed";

        /// <summary>Drains the Normal / Urgent tier.</summary>
        // Bypasses: the panel scheduler callback: production registers DrainImmediate with _anchor.schedule.Execute and never calls it.
        internal static void DrainImmediateForTest(this FiberBatchScheduler scheduler)
            => Drain(scheduler, DrainImmediateMethodName);

        /// <summary>Drains the Transition tier.</summary>
        // Bypasses: the panel scheduler callback and its delay: production registers DrainDelayed with schedule.Execute(...).ExecuteLater(delayMs).
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
            method.Invoke(scheduler, null);
        }
    }
}
