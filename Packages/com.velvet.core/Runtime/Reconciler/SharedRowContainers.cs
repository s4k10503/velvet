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
    // mount into its target and a root's mount into its element. Both sides key on the element whose rows a structural
    // pass reads (ChildListWrites.ContentOf), a ScrollView's content rather than the ScrollView a pass is handed. The
    // contexts of a container are told it is shared (ReconcilerContext.SharesRowContainers) once it holds two, so a pass
    // in a context that shares none looks nothing up here.
    internal static class SharedRowContainers
    {
        private static readonly ConditionalWeakTable<VisualElement, WeakContextList> s_placers = new();

        internal static void NotePlacer(ReconcilerContext ctx, VisualElement container)
        {
            var content = ChildListWrites.ContentOf(container);
            var placers = s_placers.GetOrCreateValue(content);
            placers.Add(ctx);
            AddCreators(placers, content);
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
            => s_placers.TryGetValue(ChildListWrites.ContentOf(container), out placers!);

        // The contexts whose walk may reconcile content's rows, going up from content as far as the first element holding
        // the fiber FiberNodeFactory leaves on what it creates (VisualElement.userData): that fiber's context, and at each
        // element on the way the contexts a Portal or a root places rows into it for. An element whose userData the
        // application wrote a value of its own over holds no fiber, and the walk goes past it. A context taken that
        // reconciles no row of content costs its passes a lookup and holds no rule for those rows.
        private static void AddCreators(WeakContextList placers, VisualElement content)
        {
            for (var element = content; element != null; element = ChildListWrites.ParentOf(element))
            {
                if (s_placers.TryGetValue(element, out var placing))
                {
                    for (var i = 0; i < placing.Count; i++)
                    {
                        if (placing.TryGet(i, out var placer))
                        {
                            placers.Add(placer);
                        }
                    }
                }
                if (element.userData is ComponentFiber { Reconciler: { } reconciler })
                {
                    placers.Add(reconciler.Context);
                    return;
                }
            }
        }
    }
}
