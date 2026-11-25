using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Locates Error and Suspense boundaries by walking the Fiber tree, and propagates exceptions
    // to the nearest Error Boundary.
    internal static class ComponentBoundarySearch
    {
        // The Suspense a suspend of fiber reaches outside a walk: the nearest boundary above it, passing over one whose
        // fallback fiber is in, as React's suspend in a fallback goes to the Suspense above the one showing it.
        internal static ComponentFiber? FindSuspenseBoundaryAbove(ComponentFiber fiber)
        {
            List<ComponentFiber>? fallbacksOf = null;
            for (var below = fiber; below.Parent != null; below = below.Parent)
            {
                if (below.SuspenseFallbackOf != null) (fallbacksOf ??= new List<ComponentFiber>()).Add(below.SuspenseFallbackOf);
                var current = below.Parent;
                if (current.IsSuspenseBoundary && fallbacksOf?.Contains(current) != true) return current;
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
            => PropagateException(fiber, exception, isRenderError: false);

        internal static void PropagateException(ComponentFiber? fiber, Exception exception, bool isRenderError)
            => PropagateException(fiber, fiber, exception, isRenderError);

        // The search starts above from; the component stack a catch reports starts at throwingFiber.
        internal static void PropagateException(
            ComponentFiber? from, ComponentFiber? throwingFiber, Exception exception, bool isRenderError)
            => PropagateFrom(from?.Parent, throwingFiber, exception, isRenderError);

        // A callback of an element, or one a component hands the reconciler, belongs to the component that rendered
        // it, and a boundary that is that component catches it, as React's capture of an element's error starts at
        // the element's parent fiber. Once that boundary has caught, its output is its fallback, so the callback is
        // the fallback's, and its error goes above it, as an error in a fallback's content does.
        internal static void PropagateFromOwner(ComponentFiber? owner, Exception exception, bool isRenderError = false)
            => PropagateFrom(owner?.CaughtError == null ? owner : owner.Parent, owner, exception, isRenderError);

        // As PropagateFromOwner, with the search starting at start rather than at the component the error belongs to, and
        // queued for the boundary's follow-up commit wherever it is thrown, as React's captureCommitPhaseError queues it;
        // with no start, the error goes to ReportUncaught.
        internal static void CaptureFrom(ComponentFiber? start, ComponentFiber owner, Exception exception)
            => CaptureAt(start == null || start.CaughtError == null ? start : start.Parent, owner, exception);

        // CaptureFrom's search, starting at start even where start has caught, as React's captureCommitPhaseError checks
        // the nearest mounted ancestor it is handed first: what a removal throws is captured this way from the component
        // whose output held the row (ChildListWrites.FileUnderTheOwner).
        internal static void CaptureAt(ComponentFiber? start, ComponentFiber owner, Exception exception)
            => PropagateFrom(start, owner, exception,
                // MUTANT_SURVIVES(equivalent, literal): isRenderError reaches only FiberErrorBoundary.TryCatch.
                // A search queued for the commit does not call it.
                isRenderError: false, inCommit: true);

        // Where the search for what placing an element threw starts, as React's commitHostPlacement starts it at the
        // return of the fiber that carries the Placement flag: the topmost fiber new in the commit, or a Portal's direct
        // child, which a Portal's mount places one by one. That is the component that rendered the element where a
        // commit has taken it (ComponentFiber.HasCommitted), and otherwise its nearest ancestor one has, a mount root
        // standing for React's host root, short of the component that rendered the Portal the element sits in, which is
        // where the search starts for anything new among a Portal's children. Null where no such fiber is above it.
        internal static ComponentFiber? PlacementSearchStart(
            ComponentFiber owner, UnityEngine.UIElements.VisualElement? portal)
        {
            ComponentFiber? fiber = owner;
            while (fiber is { IsMountRoot: false, HasCommitted: false })
            {
                if (portal != null && !ReferenceEquals(fiber.OwningPortalPlaceholder, portal)) return fiber;
                fiber = fiber.Parent;
            }
            return fiber;
        }

        // Where an error goes whose walk ends with neither a boundary nor the root V.Mount made taking it, and one Velvet
        // hands no boundary, as Framer Motion's completion callbacks run in a promise no boundary sees.
        internal static void ReportUncaught(Exception exception) => Debug.LogException(exception);

        // An error a commit's effect or ref callback throws is queued for its boundary to render in a follow-up
        // commit. A render error is caught where it is thrown, whatever commit is running around it.
        private static void PropagateFrom(
            ComponentFiber? first, ComponentFiber? throwingFiber, Exception exception, bool isRenderError)
            => PropagateFrom(first, throwingFiber, exception, isRenderError,
                inCommit: !isRenderError && first?.Reconciler?.Context.InCommitPhase == true);

        private static void PropagateFrom(ComponentFiber? first, ComponentFiber? throwingFiber, Exception exception,
            bool isRenderError, bool inCommit)
        {
            var current = first;
            while (current != null)
            {
                // Captured before TryCatch runs: a boundary whose own fallback content fails can cascade
                // to a farther ancestor mid-attempt, whose successful fallback then disposes (and detaches)
                // current along with everything between it and that farther ancestor. Reading current.Parent
                // AFTER TryCatch returns would follow that now-severed link and stop short of any candidate
                // beyond current, even though this walk's own job is to keep searching past current.
                var next = current.Parent;
                // A fiber already torn down, or one the same deletion is about to tear down, catches nothing, and the
                // walk goes on to the nearest one outside it, where React's captureCommitPhaseError starts. The mount
                // root is not passed over: being disposed is how MountedTree.Dispose reaches it
                // (FiberErrorBoundary.TryCatch).
                if ((current.IsDisposed || current.IsBeingDeleted) && !current.IsMountRoot)
                {
                    current = current.IsBeingDeleted ? current.NearestAncestorOutsideTheDeletion : next;
                    continue;
                }
                if (current.CatchesErrors && (inCommit
                        ? CommitErrors.Capture(current, throwingFiber, exception)
                        : FiberErrorBoundary.TryCatch(current, throwingFiber, exception, isRenderError)))
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
                // walk must still continue normally to next). Inside a wave that replacement's deletion can mark current
                // and leave its disposal to where the wave applies the teardown it recorded (FiberWave).
                if (current.IsDisposed || current.IsBeingDeleted) return;
                current = next;
            }

            ReportUncaught(exception);
        }
    }
}
