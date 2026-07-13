using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Walks up the Fiber tree to find an Error Boundary and propagates exceptions to it.
    internal static class ComponentBoundarySearch
    {
        // Walks ancestors of the given fiber and returns the nearest Suspense Boundary.
        // Includes the fiber itself if it is a boundary. Returns null if not found.
        internal static ComponentFiber? FindNearestSuspenseBoundary(ComponentFiber fiber)
        {
            for (var current = fiber; current != null; current = current.Parent)
            {
                if (current.IsSuspenseBoundary) return current;
            }
            return null;
        }


        // True when fiber's own async slots hold a pending resource (does not walk
        // descendants). Used by ChildReconciler.ExpandSuspenseInline to scan a Suspense's
        // primary subtree per-fiber while skipping nested boundaries' subtrees.
        internal static bool HasPendingAsyncSlot(ComponentFiber fiber)
        {
            var slots = fiber.AsyncSlots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].Status == FiberAsyncResourceStatus.Pending) return true;
            }
            return false;
        }

        // Propagates an exception to the fiber's ancestor Error Boundary.
        // If a boundary that can catch the exception is found, processing completes there.
        // Otherwise, falls back to logging via Debug.LogException.
        // fiber is nullable: a caller with no owning component fiber (e.g. AnimatePresence reconciled onto
        // a bare element) has nothing to walk from and falls straight through to the log.
        internal static void PropagateException(ComponentFiber? fiber, Exception exception)
        {
            for (var current = fiber?.Parent; current != null; current = current.Parent)
            {
                if (current.IsErrorBoundary && FiberErrorBoundary.TryCatch(current, fiber, exception))
                {
                    return;
                }
            }

            Debug.LogException(exception);
        }
    }
}
