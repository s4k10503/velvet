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
    /// Each method throws <see cref="MissingMethodException"/> (<see cref="MissingFieldException"/> for a field) when the member it reflects for is gone.
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

        /// <summary>Runs the callback the Transition tier registered for the admission its next pass runs.</summary>
        // Bypasses: the panel scheduler that decides when it runs; the entry must already wait for that admission, which RunAdmitCallbackForTest arranges for one requested outside every pass.
        internal static void RunDelayedCallbackForTest(this FiberBatchScheduler scheduler)
        {
            if (!RunWithAdmission(scheduler, "_admittedForNextPass", "RunDelayedCallback"))
            {
                throw new InvalidOperationException("No admission is held in _admittedForNextPass");
            }
        }

        /// <summary>Runs the callback that moves entries requested outside every pass onto the next pass's admission.</summary>
        // Bypasses: the panel scheduler that decides when it runs.
        internal static void RunAdmitCallbackForTest(this FiberBatchScheduler scheduler)
        {
            if (!scheduler.TryRunAdmitCallbackForTest())
            {
                throw new InvalidOperationException("No admission is held in _unadmitted");
            }
        }

        /// <summary>As <see cref="RunAdmitCallbackForTest"/>, returning false rather than throwing when no entry waits outside every pass.</summary>
        // Bypasses: the panel scheduler that decides when it runs.
        internal static bool TryRunAdmitCallbackForTest(this FiberBatchScheduler scheduler)
            => RunWithAdmission(scheduler, "_unadmitted", "RunAdmitCallback");

        private static bool RunWithAdmission(FiberBatchScheduler scheduler, string fieldName, string methodName)
        {
            var field = typeof(FiberBatchScheduler).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(FiberBatchScheduler).FullName, fieldName);
            var admission = field.GetValue(scheduler);
            if (admission == null) return false;
            var method = typeof(FiberBatchScheduler).GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(FiberBatchScheduler).FullName, methodName);
            try
            {
                method.Invoke(scheduler, BindingFlags.DoNotWrapExceptions, null, new[] { admission }, null);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
            }
            return true;
        }

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
