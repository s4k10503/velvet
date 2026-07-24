using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Locates Error and Suspense boundaries by walking the Fiber tree, and propagates exceptions
    // to the nearest Error Boundary.
    internal static class ComponentBoundarySearch
    {
        // Inclusive of fiber itself.
        internal static ComponentFiber? FindNearestSuspenseBoundary(ComponentFiber fiber)
        {
            for (var current = fiber; current != null; current = current.Parent)
            {
                if (current.IsSuspenseBoundary) return current;
            }
            return null;
        }


        // True when fiber's own async slots hold a pending resource (does not walk
        // descendants). Used by GeneralPathReconciler.ExpandSuspenseInline to scan a Suspense's
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

        // Exclusive of fiber itself (starts at fiber.Parent).
        // fiber is nullable: a caller with no owning component fiber (e.g. AnimatePresence reconciled onto
        // a bare element) has nothing to walk from and falls straight through to the log.
        internal static void PropagateException(ComponentFiber? fiber, Exception exception)
        {
            var current = fiber?.Parent;
            while (current != null)
            {
                // Captured before TryCatch runs: a boundary whose own fallback content fails can cascade
                // to a farther ancestor mid-attempt, whose successful fallback then disposes (and detaches)
                // current along with everything between it and that farther ancestor. Reading current.Parent
                // AFTER TryCatch returns would follow that now-severed link and stop short of any candidate
                // beyond current, even though this walk's own job is to keep searching past current.
                var next = current.Parent;
                if (current.IsErrorBoundary && FiberErrorBoundary.TryCatch(current, fiber, exception))
                {
                    return;
                }
                // current itself (not just its content) can be disposed as a side effect of the attempt
                // just declined above: its own fallback content failing cascaded to and was caught by a
                // farther ancestor, whose fallback replaced everything back down to and including current.
                // That farther ancestor already resolved this exception too by replacing current's whole
                // subtree, so continuing to next (that same farther ancestor, or beyond it) would just
                // re-invoke its TryCatch a second time on top of the one that already ran — a redundant
                // fallback factory call and Reconcile pass for one visible outcome. This is NOT the same as
                // current merely declining because its OWN fallback content failed with nowhere to escalate
                // to (current survives that case — only the failed content's own subtree is gone — so the
                // walk must still continue normally to next).
                if (current.IsDisposed) return;
                current = next;
            }

            Debug.LogException(exception);
        }
    }
}
