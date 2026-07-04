#nullable enable
using System;

namespace Velvet
{
    // The render-error / Error Boundary path — the "throw" phase. When a Render() or Reconcile
    // throws, OnRenderError walks up the fiber tree (via ComponentBoundarySearch) to the nearest
    // [Component(IsErrorBoundary = true)] ancestor; TryCatch renders that boundary's UseFallback UI in
    // place of the throwing subtree and aborts the in-flight reconcile. The fiber-stack and VNode-pool
    // plumbing it relies on stays on FiberRenderer (the render core) and is called back into here.
    internal static class FiberErrorBoundary
    {
        // Hook for when an exception occurs during Render() or Reconcile.
        // Walks up the Fiber tree to find the nearest Error Boundary and attempts to catch.
        // If no boundary catches it, logs via Debug.LogException.
        // Error Boundaries catch only errors in their child subtree.
        // A fiber's own Render() exception is not caught by itself, and is delegated to a parent (or higher) boundary.
        internal static void OnRenderError(ComponentFiber fiber, Exception exception)
            => ComponentBoundarySearch.PropagateException(fiber, exception);

        // Fallback path for a function-style Error Boundary. Invokes the factory registered via
        // Hooks.UseFallback within the Render of a [Component(IsErrorBoundary = true)]
        // component and returns the fallback VNode. Builds an ErrorInfo with the
        // throwing fiber's ComponentStack. Returns null if no factory is registered,
        // propagating to a higher boundary.
        private static VNode? RenderFallback(ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception)
        {
            if (fiber?.FallbackFactory == null) return null;
            var info = new ErrorInfo(BuildComponentStack(throwingFiber));
            return fiber.FallbackFactory.Invoke(exception, info);
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

        private static bool TryShowFallback(ComponentFiber fiber, ComponentFiber? throwingFiber, Exception originalException)
        {
            if (fiber.Reconciler == null) return false;
            VNode? fallback;
            try
            {
                fallback = RenderFallback(fiber, throwingFiber, originalException);
            }
            catch (Exception fallbackEx)
            {
                FiberLogger.LogError("ErrorBoundary",
                    "FiberRenderer: RenderFallback factory threw an exception. The original exception is preserved.");
                FiberLogger.LogException("ErrorBoundary", fallbackEx);
                return false;
            }
            if (fallback == null) return false;
            var fallbackTree = new[] { fallback };
            fiber.Reconciler.Reconcile(fiber.MountPoint, fiber.PreviousTree ?? Array.Empty<VNode>(), fallbackTree);
            FiberTreeReturn.ReturnPooledObjects(fiber.PreviousTree);
            fiber.PreviousTree = fallbackTree;
            return true;
        }

        // Attempts to display a fallback UI via this fiber's own RenderFallback; returns true on success.
        // Delegation to a parent boundary is performed by the caller (ComponentBoundarySearch.PropagateException)
        // by walking the Fiber tree.
        // Opt-in contract: only components with IsErrorBoundary=true are eligible to catch.
        // Returns false unless the candidate fiber has IsErrorBoundary=true.
        // fiber: Candidate Error Boundary fiber.
        // exception: Exception thrown by a descendant render that should be caught.
        // True when the fallback was rendered successfully and the exception is consumed; false to continue propagation.
        public static bool TryCatch(ComponentFiber fiber, ComponentFiber? throwingFiber, Exception exception)
        {
            // Opt-in contract: even if a Fiber returning IsErrorBoundary=false has TryCatch invoked through some
            // path, it does not catch.
            if (!fiber.IsErrorBoundary || fiber.Reconciler == null) return false;
            var fiberPushed = FiberRenderer.PushFiber(fiber);
            bool result;
            try
            {
                result = TryShowFallback(fiber, throwingFiber, exception);
            }
            finally
            {
                FiberRenderer.PopFiber(fiber, fiberPushed);
            }
            if (result)
            {
                fiber.Reconciler.SetAborted();
                return true;
            }
            return false;
        }
    }
}
