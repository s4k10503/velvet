using System;
using System.Collections.Generic;

namespace Velvet
{
    // Traversal utility over a ComponentFiber tree.
    // Used on context value changes to schedule dependent consumer fibers for re-render, reaching them
    // reliably even across memoized subtrees.
    internal static class FiberTreeTraversal
    {
        // Uses an explicit Stack rather than recursion to avoid StackOverflowException on deeply nested
        // component trees. root itself is also visited.
        // Sibling nodes are processed in reverse order due to Stack LIFO, but all current callers
        // (context distribution, state flush, etc.) are order-independent so this is fine.
        internal static void Visit(ComponentFiber root, Action<ComponentFiber> visitor)
        {
            if (root == null || visitor == null) return;
            var stack = new Stack<ComponentFiber>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var fiber = stack.Pop();
                visitor(fiber);
                var child = fiber.Child;
                while (child != null)
                {
                    stack.Push(child);
                    child = child.Sibling;
                }
            }
        }

        // Schedules for re-render every fiber that registered a dependency on contextKey (via UseContext).
        // Each scheduled consumer re-renders later (setState lane) and reads the new value LIVE
        // from the context cursor — the spine of enclosing Providers is reconstructed onto the cursor by
        // FiberContextSpine before its body runs — so no per-fiber snapshot is distributed
        // here. The reconstruction always reflects the nearest Provider, so an inner Provider that masks
        // the same key yields the (unchanged) masked value on the masked consumer's re-render.
        // Masking is NOT detected here: a consumer whose value is masked by an inner Provider is still
        // scheduled and re-renders, producing identical output (the reconciler then diffs to a no-op).
        // This trades a bounded extra re-render in the masking case for correctness-by-construction and
        // the removal of the cross-render snapshot machinery (the divergence root cause). Skipping
        // masked consumers would be a pure optimization; dropping it changes performance, not behavior.
        // Within one reconcile pass, the first PatchContextProvider / new-side Provider expansion
        // that observes a value change bumps ReconcilerContext.ContextPropagationGeneration,
        // and Provider chains nested inside inherit the same generation. A consumer that depends on
        // multiple keys changed in the same pass is force-rendered once, deduped by comparing
        // propagationGeneration with ComponentFiber.LastForceRenderGeneration.
        // The default int.MinValue sentinel means "do not dedup" (used only by test fixtures
        // that fire a single notify); the production path always passes a positive generation.
        public static void NotifyContextChanged(
            ComponentFiber root,
            object contextKey,
            int propagationGeneration = int.MinValue)
        {
            if (contextKey == null) return;
            Visit(root, fiber =>
            {
                if (!fiber.HasDependencyOn(contextKey)) return;

                var dedupActive = propagationGeneration != int.MinValue;
                if (dedupActive && fiber.LastForceRenderGeneration == propagationGeneration) return;

                if (dedupActive)
                {
                    // Update the sentinel before invoke: even if the handler triggers a secondary notify
                    // of the same generation during render, recursive dispatch is prevented. If the handler
                    // throws, subsequent notifies in the same generation are intentionally skipped
                    // (reliably prevents double firing).
                    fiber.LastForceRenderGeneration = propagationGeneration;
                }
                fiber.RequestRenderForContextHandler?.Invoke(fiber);
            });
        }
    }
}
