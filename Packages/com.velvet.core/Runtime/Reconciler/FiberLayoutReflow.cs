using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // React reads layout synchronously in its layout phase: reading a box inside useLayoutEffect forces the
    // reflow the commit's mutations owe. A panel lays itself out later in its frame, so a commit that runs layout
    // effects or builds imperative handles lays out the panels they live on first, through the layout validation
    // IPanel.Pick runs before it picks. UseLayoutEffectLayoutReadTests fails when Pick stops laying the panel out.
    internal static class FiberLayoutReflow
    {
        // The pick's result is discarded; a point this far off the panel ends it at the root.
        private static readonly Vector2 s_offPanelPoint = new(-1e9f, -1e9f);

        // Above zero while a virtual list renders a range. It renders one from the scroll view's GeometryChangedEvent,
        // which a panel dispatches inside its layout pass, and a layout validation started there re-enters that pass.
        // UseLayoutEffectLayoutReadTests holds the virtual-list case.
        private static int s_suppressedDepth;

        internal static void EnterSuppressed() => s_suppressedDepth++;

        internal static void ExitSuppressed() => s_suppressedDepth--;

        // Called between a layout commit's cleanup pass and its setup pass, so what the insertion effects and layout
        // cleanups write is in the layout the setups read.
        internal static void LayOutPanelsOf(List<(ComponentFiber Fiber, bool IsMount)> batch)
        {
            if (s_suppressedDepth > 0) return;
            for (var i = 0; i < batch.Count; i++)
            {
                var fiber = batch[i].Fiber;
                if (!ReadsLayout(fiber)) continue;
                _ = fiber.MountPoint?.panel?.Pick(s_offPanelPoint);
            }
        }

        private static bool ReadsLayout(ComponentFiber fiber)
        {
            if (fiber.PendingLayoutEffects is { Count: > 0 }) return true;
            var slots = fiber.ImperativeHandleSlots;
            if (slots == null) return false;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].NextFactory != null && slots[i].NextNeedsRecompute) return true;
            }
            return false;
        }
    }
}
