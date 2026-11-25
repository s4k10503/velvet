#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // The containers holding the rows of more than one context: a Portal's target or the element a root is mounted into,
    // where the context that created the element places rows as well, or another Portal or root does. A context's
    // structural pass reads only the rules it registered, so a pass over such a container re-derives the rows of the
    // other contexts recorded there too (FiberNodePatcher.ApplyStructuralVariants).
    //
    // Recorded where a context places rows from outside the walk that reconciles a container's own children: a Portal's
    // mount into its target and a root's mount into its element. The context that created the element is read off the
    // fiber FiberNodeFactory leaves on it (VisualElement.userData), so an element no ElementNode or MotionNode created
    // names no creator. The contexts of a container are told it is shared (ReconcilerContext.SharesRowContainers) once
    // it holds two, so a pass in a context that shares none looks nothing up here.
    internal static class SharedRowContainers
    {
        private static readonly ConditionalWeakTable<VisualElement, WeakContextList> s_placers = new();

        internal static void NotePlacer(ReconcilerContext ctx, VisualElement container)
        {
            var placers = s_placers.GetOrCreateValue(container);
            placers.Add(ctx);
            if ((container.userData as ComponentFiber)?.Reconciler?.Context is { } creator)
            {
                placers.Add(creator);
            }
            if (placers.Count < 2)
            {
                return;
            }
            for (var i = 0; i < placers.Count; i++)
            {
                if (placers.TryGet(i, out var placer))
                {
                    placer.SharesRowContainers = true;
                }
            }
        }

        // The contexts placing rows into container, false for one no Portal or root placed rows into.
        internal static bool TryGetPlacers(VisualElement container, out WeakContextList placers)
            => s_placers.TryGetValue(container, out placers!);
    }
}
