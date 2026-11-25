#nullable enable
using System.Collections.Generic;
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

        // The contexts whose walk may reconcile content's rows. Content's owner, content itself or the element whose
        // content container it is (ChildListWrites.ContentOf), names its creator by the fiber FiberNodeFactory leaves on
        // what it creates (VisualElement.userData), the first one at or above content. Where the owner holds none, as
        // where the application wrote a value of its own over it, the contexts are the ones whose fibers content's rows
        // hold; the contexts placing rows into the elements above content are not taken.
        private static void AddCreators(WeakContextList placers, VisualElement content)
        {
            var stamped = FirstStamped(content);
            if (stamped?.userData is ComponentFiber { Reconciler: { } creator }
                && ReferenceEquals(ChildListWrites.ContentOf(stamped), content))
            {
                placers.Add(creator.Context);
                return;
            }
            var rows = new List<VisualElement?>();
            ChildListWrites.Rows(content, 0, int.MaxValue, rows);
            foreach (var row in rows)
            {
                if (row?.userData is ComponentFiber { Reconciler: { } reconciler })
                {
                    placers.Add(reconciler.Context);
                }
            }
        }

        private static VisualElement? FirstStamped(VisualElement? element)
        {
            for (; element != null; element = ChildListWrites.ParentOf(element))
            {
                if (element.userData is ComponentFiber) return element;
            }
            return null;
        }
    }
}
