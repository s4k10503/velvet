#nullable enable
using System;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace Velvet
{
    // The render-error / Error Boundary path — the "throw" phase. When a Render() or Reconcile
    // throws, OnRenderError walks up the fiber tree (via ComponentBoundarySearch) to the nearest
    // [Component(IsErrorBoundary = true)] ancestor; TryCatch renders that boundary's UseFallback UI in
    // place of the throwing subtree. A render error below a boundary whose output the walk is expanding, or
    // whose own render is reconciling its output, is taken in there (GeneralPathReconciler.ExpandBoundaryInline,
    // Reconciler.ReconcileCatching); otherwise TryCatch replaces the boundary's rows with the fallback
    // (ShowFallbackTree) and aborts the in-flight reconcile. The fiber-stack and VNode-pool
    // plumbing it relies on stays on FiberRenderer (the render core) and is called back into here.
    internal static class FiberErrorBoundary
    {
        // Error Boundaries catch only errors in their child subtree — a fiber's own Render() exception is
        // not caught by itself, and is delegated to a parent (or higher) boundary instead. The mount root is the
        // exception: its body returns the tree it was handed, so what its render throws came from reconciling that
        // tree, which React attributes to a child the root takes.
        internal static void OnRenderError(ComponentFiber fiber, Exception exception)
        {
            if (fiber.IsMountRoot) ComponentBoundarySearch.PropagateFromOwner(fiber, exception, isRenderError: true);
            else ComponentBoundarySearch.PropagateException(fiber, exception, isRenderError: true);
        }

        // MountOptions.OnCaughtError's default. The report carries the caught exception as its inner exception
        // so that the entry reads as the caught exception itself, with the report's message in its stack
        // trace; CaughtErrorLogTests pins both.
        internal static void LogCaughtError(Exception exception, ErrorInfo info)
            => Debug.LogException(new CaughtErrorReport(
                $"Caught by the {info.ErrorBoundary} error boundary. Component stack:\n{info.ComponentStack}",
                exception));

        // MountOptions.OnUncaughtError's default: the entry is the error itself, as React's default reports the
        // error itself, and a development build adds the warning naming the component React's development build adds.
        internal static void LogUncaughtError(Exception exception, ErrorInfo info)
        {
            Debug.LogException(exception);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(UncaughtErrorWarning(info));
#endif
        }

        private static string UncaughtErrorWarning(ErrorInfo info)
        {
            var firstLine = info.ComponentStack.Split('\n')[0];
            var component = firstLine.StartsWith(StackLinePrefix, StringComparison.Ordinal)
                ? $"An error occurred in the <{firstLine.Substring(StackLinePrefix.Length)}> component."
                : "An error occurred in one of your Velvet components.";
            return component + "\n\nConsider adding an error boundary to your tree to customize error handling behavior.";
        }

        // Fallback path for a function-style Error Boundary: invokes the factory registered via
        // Hooks.UseFallback within the Render of a [Component(IsErrorBoundary = true)] component.
        // Returns null when no factory is registered, so the caller keeps propagating to a higher boundary.
        private static VNode? RenderFallback(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception, out ErrorInfo? info)
        {
            info = null;
            if (fiber?.FallbackFactory == null) return null;
            info = InfoFor(fiber, throwingFiber);
            return TryInvokeFactory(fiber, exception, info);
        }

        internal static ErrorInfo InfoFor(ComponentFiber boundary, ComponentFiber? throwingFiber)
            => new(BuildComponentStack(throwingFiber))
            {
                ErrorBoundary = boundary.IsMountRoot ? string.Empty : Hooks.ComponentName(boundary),
            };

        // Walks the throwing fiber's Parent chain to produce a component stack
        // (one line per fiber, deepest first), honoring [Component(DisplayName)] overrides
        // via Hooks.ComponentName. It stops below the fiber V.Mount made, as React's stack leaves out the HostRoot,
        // so an error the root itself raises has an empty stack.
        private const string StackLinePrefix = "    at ";

        private static string BuildComponentStack(ComponentFiber? throwingFiber)
        {
            if (throwingFiber == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            for (var current = throwingFiber; current is { IsMountRoot: false }; current = current.Parent)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(StackLinePrefix).Append(Hooks.ComponentName(current));
            }
            return sb.ToString();
        }

        private static bool TryShowFallback(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception originalException, out ErrorInfo? info)
        {
            info = null;
            if (fiber.Reconciler == null) return false;
            var cursor = fiber.Reconciler.Context.ComponentContextStack;
            var fallback = RenderFallback(fiber, throwingFiber, originalException, out info);
            if (fallback == null) return false;
            // The fallback renders where the boundary is, so it reads the Providers above the boundary rather than
            // what the cursor holds where the error was caught, inside its subtree. The spine is dropped with the
            // cursor it was pushed onto rather than unwound.
            var caughtUnder = cursor.SetAside();
            _ = FiberContextSpine.Push(fiber);
            try
            {
                return ShowFallbackTree(fiber, new[] { fallback });
            }
            finally
            {
                cursor.Restore(caughtUnder);
            }
        }

        // Unmounts the boundary's own rows, mounts fallbackTree in their place and commits it as the boundary's tree;
        // false where the fallback's own content failed or an ancestor's fallback replaced the boundary meanwhile.
        private static bool ShowFallbackTree(ComponentFiber fiber, VNode?[] fallbackTree)
        {
            try
            {
                UnmountShownRows(fiber);
                FiberCommitWork.ReconcileOwnRows(fiber, Array.Empty<VNode>(), fallbackTree, frameBudgetMs: 0);
            }
            catch (FiberSuspendSignal)
            {
                // Same as above: a suspend raised while rendering the fallback's own content must keep
                // unwinding toward a real Suspense boundary, not be treated as a fallback failure.
                throw;
            }
            catch (BoundaryCaughtSignal)
            {
                // Nor is a catch by a boundary above whose walk this reconcile runs inside.
                throw;
            }
            catch (Exception reconcileEx)
            {
                FiberLogger.LogError("ErrorBoundary",
                    "FiberRenderer: reconciling the fallback UI threw an exception. The original exception is preserved.");
                FiberLogger.LogException("ErrorBoundary", reconcileEx);
                // The error of an element's constructor arrives wrapped in a TargetInvocationException.
                fiber.FallbackReconcileFailure = reconcileEx is System.Reflection.TargetInvocationException { InnerException: { } inner }
                    ? inner
                    : reconcileEx;
                return false;
            }
            if (fiber.IsDisposed)
            {
                // An ancestor boundary's fallback (triggered by this fallback's own content failing, see
                // FallbackContentFailed) already replaced this fiber's whole subtree while the Reconcile
                // call above was in progress — nothing here to track bookkeeping for anymore.
                return false;
            }
            fiber.PreviousTree = fallbackTree;
            fiber.FallbackReplacedPreviousTree = true;
            // FallbackContentFailed is set when a re-entrant TryCatch above declined because THIS
            // fiber's own fallback content threw (logged, or shown by a farther ancestor instead) — the
            // Reconcile call still returns normally either way, so this is the only place that can tell
            // "rendered cleanly" apart from "the content itself failed." When it failed, the ORIGINAL
            // exception this attempt was responding to was never actually shown anything — report failure
            // so the caller keeps propagating instead of falsely claiming success.
            return !fiber.FallbackContentFailed;
        }

        // React reconciles a boundary's fallback against none of the children it replaces
        // (forceUnmountCurrentAndReconcile), so a component of the fallback with the type and key of one the boundary
        // showed mounts afresh rather than keeping that one's state. PreviousTree is emptied with the rows, so that it
        // describes what the boundary holds while the fallback mounts.
        private static void UnmountShownRows(ComponentFiber fiber)
        {
            var shown = fiber.PreviousTree ?? Array.Empty<VNode>();
            FiberCommitWork.ReconcileOwnRows(fiber, shown, Array.Empty<VNode>(), frameBudgetMs: 0);
            fiber.PreviousTree = Array.Empty<VNode>();
            // What the shown rows recorded against the boundary itself, for a Suspense or an AnimatePresence it
            // rendered directly, and the primaries the reconcile above reproduced for a new side to read. Inside an
            // enclosing pass, the retirement at that pass's end (ReconcilerContext.RetireBoundaryStatesNotReRendered)
            // would come after the fallback had read them.
            fiber.Reconciler!.Context.ForgetRecordsOf(fiber);
        }

        // A render error below a boundary whose own render is the pass, caught inside that render's reconcile
        // (Reconciler.ReconcileCatching), as GeneralPathReconciler.ExpandBoundaryInline catches one in a walk: the fallback then replaces the rows the boundary's committed tree holds, and the catch is
        // reported once it has rendered. Where the fallback's own content failed, the original error goes on.
        internal static void ShowCaughtFallback(ComponentFiber fiber, BoundaryCaughtSignal caught)
        {
            fiber.IsShowingFallback = true;
            fiber.FallbackContentFailed = false;
            bool shown;
            try
            {
                shown = ShowFallbackTree(fiber, caught.FallbackTree);
            }
            finally
            {
                fiber.IsShowingFallback = false;
            }
            if (fiber.IsDisposed) return;
            if (shown) RecordCatch(fiber.Reconciler!.Context, fiber, caught.Error, caught.Info);
            else PassOnTheCaughtError(fiber, caught);
        }

        internal static void PassOnTheCaughtError(ComponentFiber boundary, BoundaryCaughtSignal caught)
        {
            DropTheSwapsErrors(boundary);
            // A fallback that could not be created is what the boundary above is handed, as React hands it the error of
            // the fallback's render rather than the one the fallback was for.
            var error = TakeFallbackReconcileFailure(boundary) ?? caught.Error;
            ComponentBoundarySearch.PropagateException(boundary, caught.Thrower, error, isRenderError: true);
        }

        private static VNode? TryInvokeFactory(ComponentFiber fiber, Exception originalException, ErrorInfo info)
        {
            try
            {
                return fiber.FallbackFactory?.Invoke(originalException, info);
            }
            catch (FiberSuspendSignal)
            {
                // Not a fallback failure: the factory suspended on a pending async resource and must reach
                // a real Suspense boundary via the ordinary ambient path, the same as any other render.
                throw;
            }
            catch (Exception fallbackEx)
            {
                FiberLogger.LogError("ErrorBoundary",
                    "FiberRenderer: RenderFallback factory threw an exception. The original exception is preserved.");
                FiberLogger.LogException("ErrorBoundary", fallbackEx);
                return null;
            }
        }

        // Attempts to display a fallback UI via this fiber's own RenderFallback; returns true on success.
        // Delegation to a parent boundary is performed by the caller (ComponentBoundarySearch.PropagateException)
        // by walking the Fiber tree. Opt-in contract: only a fiber with IsErrorBoundary=true is eligible
        // to catch — every other candidate returns false immediately, even if reached via some other path.
        // fiber: Candidate Error Boundary fiber.
        // exception: Exception thrown by a descendant render that should be caught.
        // True when the fallback was rendered successfully and the exception is consumed; false to continue propagation.
        public static bool TryCatch(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception, bool isRenderError)
        {
            if (!fiber.CatchesErrors || fiber.Reconciler == null) return false;
            if (fiber.IsMountRoot && fiber.IsDisposed)
            {
                // MountedTree.Dispose is unmounting the tree, and no catch follows to deliver a report. It reports
                // what was thrown once every cleanup of the unmount has run (ReportErrorsDuringDisposal).
                fiber.Reconciler.Context.ErrorsDuringRootDisposal.Add((exception, InfoFor(fiber, throwingFiber)));
                return true;
            }
            if (fiber.IsShowingFallback)
            {
                // An error a render of the fallback's own content raises declines here, as React's boundary passes
                // over an error its own retry raises. Recorded so the call showing the fallback reports failure
                // for the error it was handling, though the reconcile it runs inside returns normally.
                if (isRenderError && !fiber.IsMountRoot)
                {
                    fiber.FallbackContentFailed = true;
                    return false;
                }
                TakeDuringTheSwap(fiber, throwingFiber, exception);
                return true;
            }
            if (isRenderError && fiber.CatchesInTheWalk) return CatchInTheWalk(fiber, throwingFiber, exception);
            if (FiberExecutionContext.IsSet) return SwapInTheFallback(fiber, throwingFiber, exception, isRenderError);
            // A failure delivered with no entry on the stack (a UseFrame callback's, say) opens one, since the swap
            // renders and commits.
            return FiberExecutionContext.Enter(fiber.Reconciler.Context.BatchScheduler,
                (Fiber: fiber, Thrower: throwingFiber, Error: exception, IsRenderError: isRenderError),
                static failure => SwapInTheFallback(failure.Fiber, failure.Thrower, failure.Error, failure.IsRenderError));
        }

        private static bool SwapInTheFallback(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception, bool isRenderError)
        {
            var ctx = fiber.Reconciler!.Context;
            // Taken before the fallback is shown, which detaches what the failed output created, so that the
            // completions the fallback's own render queues stay out of it as well.
            var failedCompletions = ctx.CompletionsBelow(fiber);
            var pushedOnto = FiberRenderer.PushFiber(fiber);
            fiber.IsShowingFallback = true;
            fiber.FallbackContentFailed = false;
            bool result;
            var reportOwed = false;
            ErrorInfo? info;
            try
            {
                result = TryShowFallback(fiber, throwingFiber, exception, out info);
            }
            catch (FiberSuspendSignal) when (ctx.SuspensePrimaryDepth == 0)
            {
                // The swap has taken the failed rows down, and the throwing fiber with them, so the pass it started
                // has no fiber left to retry. The boundary holds the error and renders its fallback again when the
                // read resolves (OutputOf), and reports the catch from the render whose fallback then reconciles
                // (ReportCarriedCatch), as React reports it from the commit that shows it.
                fiber.CaughtError = (exception, InfoFor(fiber, throwingFiber));
                fiber.CatchUnreported = true;
                ctx.DropCompletions(failedCompletions);
                DropTheSwapsErrors(fiber);
                FiberRenderer.SuspendPassOwner(fiber, FiberWorkLoop.IsRenderingTransitionLane);
                if (ctx.SharedReconcileDepth > ctx.RangeBracketDepth) fiber.Reconciler?.SetAborted();
                info = null;
                result = true;
                reportOwed = true;
            }
            finally
            {
                fiber.IsShowingFallback = false;
                FiberRenderer.PopFiber(pushedOnto);
            }
            if (result)
            {
                // React runs nothing for work that never committed.
                ctx.DropCompletions(failedCompletions);
                // Only a pass on the stack has sibling work left for the abort to stop. Set outside one, the flag
                // outlives the catch, and a pass starting while it is still set discards its whole reconcile —
                // CommitPhaseCatchAbortLeakTests holds that. A VirtualList range's bracket adds a depth that is no pass
                // here (ReconcilerContext.RangeBracketDepth).
                if (ctx.SharedReconcileDepth > ctx.RangeBracketDepth) fiber.Reconciler!.SetAborted();
                if (reportOwed) return true;
                RecordCatch(ctx, fiber, exception, info!);
                FiberEffects.CommitStrandedLayoutWork(ctx);
                return true;
            }
            DropTheSwapsErrors(fiber);
            var failure = TakeFallbackReconcileFailure(fiber);
            if (failure == null) return false;
            ComponentBoundarySearch.PropagateException(fiber, throwingFiber, failure, isRenderError);
            return true;
        }

        private static Exception? TakeFallbackReconcileFailure(ComponentFiber boundary)
        {
            var failure = boundary.FallbackReconcileFailure;
            boundary.FallbackReconcileFailure = null;
            return failure;
        }

        // An error that is not a render of the fallback's content — a cleanup or a ref of what the swap removes or
        // mounts — is the boundary's own to catch: it is the nearest mounted ancestor of that work, where React's
        // captureCommitPhaseError starts. It is caught after the error the swap is for (RecordCatch), as React
        // reports that one from the layout phase of the swap's commit and this one from a later commit.
        private static void TakeDuringTheSwap(ComponentFiber boundary, ComponentFiber? throwingFiber, Exception exception)
        {
            var info = InfoFor(boundary, throwingFiber);
            boundary.Reconciler!.Context.ErrorsDuringFallbackSwap.Add((boundary, exception, info));
        }

        // A swap that failed leaves nothing to catch with, so what it took goes with it. The root cannot be replaced,
        // and reports what it took at once.
        private static void DropTheSwapsErrors(ComponentFiber boundary)
        {
            var ctx = boundary.Reconciler?.Context;
            if (ctx == null) return;
            var taken = ctx.ErrorsDuringFallbackSwap;
            for (var i = 0; i < taken.Count; i++)
            {
                if (boundary.IsMountRoot && ReferenceEquals(taken[i].Boundary, boundary))
                {
                    ReportUncaughtError(ctx, taken[i].Error, taken[i].Info);
                }
            }
            taken.RemoveAll(entry => ReferenceEquals(entry.Boundary, boundary));
        }

        // Leaves the fallback to the frame expanding or reconciling the boundary's output that is on the stack,
        // which renders the fallback in place of the failed children and reports the catch once the fallback has
        // rendered; the rest of the pass goes on. Returns false only where no fallback was produced, so propagation
        // continues.
        private static bool CatchInTheWalk(ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception)
        {
            var fallback = RenderFallback(fiber, throwingFiber, exception, out var info);
            if (fallback == null) return false;
            throw new BoundaryCaughtSignal(fiber, new[] { fallback }, throwingFiber, exception, info!);
        }

        private static void QueueCatch(ReconcilerContext ctx, ComponentFiber fiber, Exception exception, ErrorInfo info)
        {
            fiber.CaughtError = (exception, info);
            fiber.CatchReportedFromCarry = false;
            ctx.PendingCaughtErrorReports.Add((fiber, exception, info, ctx.NextCaughtErrorSequence++));
        }

        internal static void RecordCatch(ReconcilerContext ctx, ComponentFiber fiber, Exception exception, ErrorInfo info)
        {
            QueueCatch(ctx, fiber, exception, info);
            var taken = ctx.ErrorsDuringFallbackSwap;
            var tookAny = false;
            for (var i = 0; i < taken.Count; i++)
            {
                if (!ReferenceEquals(taken[i].Boundary, fiber)) continue;
                QueueCatch(ctx, fiber, taken[i].Error, taken[i].Info);
                tookAny = true;
            }
            if (!tookAny) return;
            taken.RemoveAll(entry => ReferenceEquals(entry.Boundary, fiber));
            // Its fallback renders again with the newest error, as React's getDerivedStateFromError takes it.
            if (!fiber.IsMountRoot) FiberWorkLoop.RequestRenderFromHook(fiber);
        }

        // A boundary that has caught renders its fallback from then on, as React's does until it remounts: a
        // re-render does not bring its children back, and changing its key is what does. Its body still runs, so
        // its hooks keep their order and the factory it registers is the current one, which is handed the error
        // it caught. The body's output is discarded as an aborted render's is. A factory that gives no fallback,
        // by returning null or throwing, passes the error it was handed to the boundaries above, as it does at
        // the catch: that error is thrown from this boundary's own render.
        internal static VNode?[] OutputOf(ComponentFiber fiber, VNode?[] bodyOutput)
        {
            // After the body, which reads what the boundary held before this render as the library's prevState.
            TakeQueuedReset(fiber);
            TakeCaptures(fiber);
            if (fiber.CaughtError is not var (error, info)) return bodyOutput;
            var fallback = TryInvokeFactory(fiber, error, info);
            if (fallback == null) ExceptionDispatchInfo.Capture(error).Throw();
            return new[] { fallback! };
        }

        // The errors a commit captured below the boundary (CommitErrors.Capture), caught here as a catch is, each
        // reported in capture order; the newest is the one its fallback is handed, as React's getDerivedStateFromError
        // takes each and keeps the last. A render that a commit's own user code starts leaves them for the render
        // the commit's end asks for (FiberEffects.FlushCaptures). The rows the boundary showed go first, so the fallback mounts afresh as it does
        // where TryCatch swaps it in. A cleanup of those rows that throws in a removal that runs in the walk is taken as one of
        // the swap is, and reported after the captures; in a deferred removal it is a capture of its own. Inside a parent's walk the unmount would take rows that walk already holds, so
        // the fallback is reconciled against them instead.
        private static void TakeCaptures(ComponentFiber fiber)
        {
            var captures = fiber.PendingCaptures;
            if (captures == null || captures.Count == 0) return;
            var ctx = fiber.Reconciler!.Context;
            if (ctx.InCommitPhase) return;
            // A fallback that has not committed leaves its catch unreported, and an error captured meanwhile waits for the
            // commit that shows it, as React raises the capture in that commit. ReportCarriedCatch asks for the render that
            // takes it.
            if (fiber.CatchUnreported) return;
            var taken = captures.ToArray();
            ctx.DropCapturesOf(fiber);
            if (ctx.SharedReconcileDepth == 0)
            {
                fiber.IsShowingFallback = true;
                try
                {
                    UnmountShownRows(fiber);
                }
                finally
                {
                    fiber.IsShowingFallback = false;
                }
            }
            // Reported by the render that reconciles the fallback without suspending (ReportCarriedCatch), as React
            // calls the update's callback from the commit that shows the fallback, which a fallback that suspends
            // does not reach.
            for (var i = 0; i < taken.Length - 1; i++)
            {
                (fiber.CarriedEarlierCaptures ??= new()).Add((taken[i].Error, taken[i].Info));
            }
            var last = taken[taken.Length - 1];
            fiber.CaughtError = (last.Error, last.Info);
            fiber.CatchUnreported = true;
        }

        // The catch OutputOf's fallback was rendered for, where the fallback it suspended in at the catch has now been
        // reconciled without suspending again: the report belongs to the commit that shows it.
        internal static void ReportCarriedCatch(ComponentFiber fiber)
        {
            if (!fiber.CatchUnreported || fiber.CaughtError is not var (error, info)) return;
            fiber.CatchUnreported = false;
            var earlier = fiber.CarriedEarlierCaptures;
            fiber.CarriedEarlierCaptures = null;
            if (earlier != null)
            {
                foreach (var capture in earlier) QueueCatch(fiber.Reconciler!.Context, fiber, capture.Error, capture.Info);
            }
            var ctx = fiber.Reconciler!.Context;
            RecordCatch(ctx, fiber, error, info);
            fiber.CatchReportedFromCarry = true;
            // What a commit captured while the fallback was suspended was left for this commit (TakeCaptures), whose render
            // is asked for as a capture asks for its own.
            if (fiber.PendingCaptures is { Count: > 0 })
            {
                FiberWorkLoop.ScheduleRerender(fiber, FiberUpdatePriority.Urgent);
                ctx.BatchScheduler.OweSyncLane();
            }
        }

        // react-error-boundary's setState(initialState), which follows its onReset: queued as a state update is,
        // on the lane a setter called here would take, and taken by the boundary's next render on that lane
        // (TakeQueuedReset). Until then the boundary still holds what it caught, so a second reset in the same
        // handler calls onReset again, as the library's does on reading this.state.error again.
        internal static void QueueReset(ComponentFiber boundary)
        {
            if (!FiberWorkLoop.IsInTransitionScope) boundary.QueuedReset = QueuedErrorBoundaryReset.AnyLane;
            else if (boundary.QueuedReset == QueuedErrorBoundaryReset.None) boundary.QueuedReset = QueuedErrorBoundaryReset.TransitionLane;
            FiberWorkLoop.RequestRenderFromHook(boundary);
        }

        // A render off the Transition lane leaves a reset queued on it for that lane, and asks for the lane again,
        // as Hooks.ThrowPendingError leaves a transition's error.
        private static void TakeQueuedReset(ComponentFiber fiber)
        {
            switch (fiber.QueuedReset)
            {
                case QueuedErrorBoundaryReset.None:
                    return;
                case QueuedErrorBoundaryReset.TransitionLane when !FiberWorkLoop.IsRenderingTransitionLane:
                    FiberWorkLoop.RequestTransitionRerender(fiber);
                    return;
            }
            fiber.QueuedReset = QueuedErrorBoundaryReset.None;
            // What was captured while the fallback had not committed is the error of the swap's own deletion, a commit that
            // React never made, so the reset shows the children rather than a fallback for it.
            if (fiber.CatchUnreported) fiber.Reconciler?.Context.DropCapturesOf(fiber);
            fiber.CaughtError = null;
            // The children mount afresh, as React's do after a reset: what a Suspense among them kept offscreen,
            // and the fallback recorded for it against this fiber, are not carried into the reset render.
            var context = fiber.Reconciler?.Context;
            if (context == null) return;
            context.ComponentRegistry.DisposeOffscreenInlineChildren(fiber);
            context.PruneSuspenseBoundaryState(fiber);
        }

        // A throw out of the handler would escape the commit delivering the report.
        internal static void ReportCaughtError(ReconcilerContext ctx, ComponentFiber boundary, Exception exception, ErrorInfo info)
        {
            if (boundary.IsMountRoot)
            {
                ReportUncaughtError(ctx, exception, info);
                return;
            }
            try
            {
                ctx.OnCaughtError(exception, info);
            }
            catch (Exception handlerException)
            {
                Debug.LogException(handlerException);
            }
        }

        internal static void ReportErrorsDuringDisposal(ReconcilerContext ctx)
        {
            var thrown = ctx.ErrorsDuringRootDisposal;
            for (var i = 0; i < thrown.Count; i++)
            {
                ReportUncaughtError(ctx, thrown[i].Error, thrown[i].Info);
            }
            thrown.Clear();
        }

        internal static void ReportUncaughtError(ReconcilerContext ctx, Exception exception, ErrorInfo info)
        {
            try
            {
                ctx.OnUncaughtError(exception, info);
            }
            catch (Exception handlerException)
            {
                Debug.LogException(handlerException);
            }
        }
    }

    internal enum QueuedErrorBoundaryReset
    {
        None,
        AnyLane,
        TransitionLane,
    }

    // Unwinds from FiberErrorBoundary.TryCatch to the frame of the boundary it names that set CatchesInTheWalk.
    internal sealed class BoundaryCaughtSignal : Exception
    {
        internal BoundaryCaughtSignal(
            ComponentFiber boundary, VNode?[] fallbackTree, ComponentFiber? thrower, Exception error, ErrorInfo info)
        {
            Boundary = boundary;
            FallbackTree = fallbackTree;
            Thrower = thrower;
            Error = error;
            Info = info;
        }

        internal ComponentFiber Boundary { get; }

        internal VNode?[] FallbackTree { get; }

        internal ComponentFiber? Thrower { get; }

        internal Exception Error { get; }

        internal ErrorInfo Info { get; }
    }

    internal sealed class CaughtErrorReport : Exception
    {
        internal CaughtErrorReport(string message, Exception caught) : base(message, caught)
        {
        }
    }
}
