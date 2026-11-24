using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a container's deletion decided when its walk reached it, kept for the removals that follow: the take-downs in
    // the order the walk reached them (DeletionCommit.Execute runs them), and for each root the fibers mounted under it
    // and the occupant its removal hands to the pool, where its type is one the pool takes (FiberElementCleaner.ReturnToPool).
    // A removal takes its root's entry, which is how it knows the walk has been over the row already. A Portal the
    // deletion detached from the model where it decided (DeletionCommit.DetachPortals) is kept the same way, until the
    // cleanup of its placeholder takes it.
    internal sealed class DeletionPlan
    {
        // A plan per deletion on the stack, so a cleanup that commits another deletion takes a plan of its own.
        private static readonly Stack<DeletionPlan> s_pool = new();

        internal readonly List<PreOrderTeardown.Step> Steps = new();
        internal readonly Dictionary<VisualElement, PlannedRoot> Roots = new();
        internal readonly Dictionary<VisualElement, DetachedPortal> Portals = new();

        // The Portal's target and the rows it held there, from the first to the last.
        internal readonly struct DetachedPortal
        {
            internal readonly VisualElement Target;
            internal readonly List<VisualElement> Rows;

            internal DetachedPortal(VisualElement target, List<VisualElement> rows)
            {
                Target = target;
                Rows = rows;
            }
        }

        internal readonly struct PlannedRoot
        {
            internal readonly List<ComponentFiber>? Mounted;
            internal readonly VisualElement Poolable;

            internal PlannedRoot(List<ComponentFiber>? mounted, VisualElement poolable)
            {
                Mounted = mounted;
                Poolable = poolable;
            }
        }

        internal VisualElement? PoolableOf(VisualElement root) => Roots.TryGetValue(root, out var planned) ? planned.Poolable : null;

        // A removal takes its root's entry: the walk is not repeated for a root a plan holds, and a root taken is gone.
        internal PlannedRoot? Take(VisualElement root) => Roots.Remove(root, out var planned) ? planned : null;

        internal DetachedPortal? TakePortal(VisualElement placeholder)
            => Portals.Remove(placeholder, out var detached) ? detached : null;

        internal static DeletionPlan Rent() => s_pool.Count > 0 ? s_pool.Pop() : new DeletionPlan();

        internal void Release()
        {
            PreOrderTeardown.ClearAndTrim(Steps);
            PreOrderTeardown.ClearAndTrim(Roots);
            PreOrderTeardown.ClearAndTrim(Portals);
            s_pool.Push(this);
        }
    }
}
