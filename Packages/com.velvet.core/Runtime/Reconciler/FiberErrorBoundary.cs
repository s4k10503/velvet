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
    // Reconciler.ReconcileCatching); otherwise TryCatch reconciles the fallback over the boundary's rows
    // and aborts the in-flight reconcile. The fiber-stack and VNode-pool
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
        // error itself.
        internal static void LogUncaughtError(Exception exception, ErrorInfo info) => Debug.LogException(exception);

        // Fallback path for a function-style Error Boundary: invokes the factory registered via
        // Hooks.UseFallback within the Render of a [Component(IsErrorBoundary = true)] component.
        // Returns null when no factory is registered, so the caller keeps propagating to a higher boundary.
        private static VNode? RenderFallback(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception, out ErrorInfo? info)
        {
            info = null;
            if (fiber?.FallbackFactory == null) return null;
            info = new ErrorInfo(BuildComponentStack(throwingFiber))
            {
                ErrorBoundary = fiber.IsMountRoot ? string.Empty : Hooks.ComponentName(fiber),
            };
            return TryInvokeFactory(fiber, exception, info);
        }

        // Walks the throwing fiber's Parent chain to produce a component stack
        // (one line per fiber, deepest first), honoring [Component(DisplayName)] overrides
        // via Hooks.ComponentName.
        private static string BuildComponentStack(ComponentFiber? throwingFiber)
        {
            if (throwingFiber == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            for (var current = throwingFiber; current != null; current = current.Parent)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("    at ").Append(Hooks.ComponentName(current));
            }
            return sb.ToString();
        }

        private static bool TryShowFallback(
            ComponentFiber fiber, ComponentFiber? throwingFiber, Exception originalException, out ErrorInfo? info)
        {
            info = null;
            if (fiber.Reconciler == null) return false;
            var fallback = RenderFallback(fiber, throwingFiber, originalException, out info);
            if (fallback == null) return false;
            return ShowFallbackTree(fiber, new[] { fallback });
        }

        // Reconciles fallbackTree over the boundary's own rows and commits it as the boundary's tree; false where
        // the fallback's own content failed or an ancestor's fallback replaced the boundary meanwhile.
        private static bool ShowFallbackTree(ComponentFiber fiber, VNode?[] fallbackTree)
        {
            try
            {
                FiberCommitWork.ReconcileOwnRows(
                    fiber, fiber.PreviousTree ?? Array.Empty<VNode>(), fallbackTree, frameBudgetMs: 0);
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
                return false;
            }
            if (fiber.IsDisposed)
            {
                // An ancestor boundary's fallback (triggered by this fallback's own content failing, see
                // FallbackContentFailed) already replaced this fiber's whole subtree while the Reconcile
                // call above was in progress — nothing here to track bookkeeping for anymore.
                return false;
            }
            // Commit the fallback as the new baseline BEFORE retiring the failed tree, so the recycle
            // sweep's owner mark protects any node the two share (a memoized subtree that survived the
            // failure and re-appears inside the fallback).
            var failedTree = fiber.PreviousTree;
            fiber.PreviousTree = fallbackTree;
            fiber.FallbackReplacedPreviousTree = true;
            FiberTreeReturn.ReturnRetiredTree(failedTree, fiber);
            // FallbackContentFailed is set when a re-entrant TryCatch above declined because THIS
            // fiber's own fallback content threw (logged, or shown by a farther ancestor instead) — the
            // Reconcile call still returns normally either way, so this is the only place that can tell
            // "rendered cleanly" apart from "the content itself failed." When it failed, the ORIGINAL
            // exception this attempt was responding to was never actually shown anything — report failure
            // so the caller keeps propagating instead of falsely claiming success.
            return !fiber.FallbackContentFailed;
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
            else
            {
                ReportDeferredUncaught(fiber);
                PassOnTheCaughtError(fiber, caught);
            }
        }

        internal static void PassOnTheCaughtError(ComponentFiber boundary, BoundaryCaughtSignal caught)
            => ComponentBoundarySearch.PropagateException(boundary, caught.Thrower, caught.Error, isRenderError: true);

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
            if (fiber.IsMountRoot && (fiber.IsShowingFallback || fiber.IsDisposed))
            {
                TakeAtTheUnmountingRoot(fiber, throwingFiber, exception);
                return true;
            }
            // A fiber whose own fallback content throws re-enters here (the content's per-fiber render
            // catch routes back to this same boundary via ComponentBoundarySearch.PropagateException).
            // Decline immediately rather than attempting to show the already-failing fallback again — the
            // caller's propagation loop then continues to the next ancestor boundary on its own. Record it
            // so the OUTER (non-reentrant) call in progress below can report failure for the exception it
            // was actually handling, instead of the Reconcile call it's nested inside returning normally
            // and looking like a clean render.
            if (fiber.IsShowingFallback)
            {
                fiber.FallbackContentFailed = true;
                return false;
            }
            if (isRenderError && fiber.CatchesInTheWalk) return CatchInTheWalk(fiber, throwingFiber, exception);
            var ctx = fiber.Reconciler.Context;
            // Taken before the fallback is shown, which detaches what the failed output created, so that the
            // completions the fallback's own render queues stay out of it as well.
            var failedCompletions = ctx.CompletionsBelow(fiber);
            var pushedOnto = FiberRenderer.PushFiber(fiber);
            fiber.IsShowingFallback = true;
            fiber.FallbackContentFailed = false;
            bool result;
            ErrorInfo? info;
            try
            {
                result = TryShowFallback(fiber, throwingFiber, exception, out info);
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
                // CommitPhaseCatchAbortLeakTests holds that.
                if (ctx.SharedReconcileDepth > 0) fiber.Reconciler.SetAborted();
                RecordCatch(ctx, fiber, exception, info!);
                FiberEffects.CommitStrandedLayoutWork(ctx);
                return true;
            }
            ReportDeferredUncaught(fiber);
            return false;
        }

        // An error out of the unmount the root is making, for an earlier uncaught error or for MountedTree.Dispose,
        // is reported as uncaught and replaces nothing. During an unmount for an earlier error it is reported after
        // that error (RecordCatch appends it), as React reports the earlier one from the layout phase of the commit
        // whose deletions raised it and this one from a later commit; a Dispose reports it at once, since no catch
        // follows to deliver it.
        private static void TakeAtTheUnmountingRoot(ComponentFiber root, ComponentFiber? throwingFiber, Exception exception)
        {
            var ctx = root.Reconciler!.Context;
            var info = new ErrorInfo(BuildComponentStack(throwingFiber));
            if (root.IsDisposed) ReportUncaughtError(ctx, exception, info);
            else ctx.UncaughtDuringRootUnmount.Add((exception, info));
        }

        // A root whose unmount failed reports at once what that unmount threw, since no catch is recorded to carry it.
        private static void ReportDeferredUncaught(ComponentFiber fiber)
        {
            if (!fiber.IsMountRoot || fiber.Reconciler == null) return;
            var ctx = fiber.Reconciler.Context;
            var deferred = ctx.UncaughtDuringRootUnmount;
            for (var i = 0; i < deferred.Count; i++) ReportUncaughtError(ctx, deferred[i].Error, deferred[i].Info);
            deferred.Clear();
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

        internal static void RecordCatch(ReconcilerContext ctx, ComponentFiber fiber, Exception exception, ErrorInfo info)
        {
            fiber.CaughtError = (exception, info);
            ctx.PendingCaughtErrorReports.Add((fiber, exception, info, ctx.NextCaughtErrorSequence++));
            if (!fiber.IsMountRoot) return;
            var deferred = ctx.UncaughtDuringRootUnmount;
            for (var i = 0; i < deferred.Count; i++)
            {
                ctx.PendingCaughtErrorReports.Add((fiber, deferred[i].Error, deferred[i].Info, ctx.NextCaughtErrorSequence++));
            }
            deferred.Clear();
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
            if (fiber.CaughtError is not var (error, info)) return bodyOutput;
            var fallback = TryInvokeFactory(fiber, error, info);
            var fallbackTree = fallback == null ? null : new[] { fallback };
            // Retired beside the fallback, which the factory may have built from nodes the body returned too.
            FiberTreeReturn.ReturnRetiredTree(bodyOutput, fiber, alsoLive: fallbackTree);
            if (fallbackTree == null) ExceptionDispatchInfo.Capture(error).Throw();
            return fallbackTree!;
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
