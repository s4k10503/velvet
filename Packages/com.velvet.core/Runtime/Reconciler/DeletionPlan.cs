using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a container's deletion decided when its walk reached it, kept for the removals that follow: the take-downs in
    // the order the walk reached them (DeletionCommit.Execute runs them), and for each root the fibers mounted under it
    // and the occupant that returns to its pool. A removal takes its root's entry, which is how it knows the walk has
    // been over the row already.
    internal sealed class DeletionPlan
    {
        // A plan per deletion on the stack, so a cleanup that commits another deletion takes a plan of its own.
        private static readonly Stack<DeletionPlan> s_pool = new();

        internal readonly List<PreOrderTeardown.Step> Steps = new();
        internal readonly Dictionary<VisualElement, PlannedRoot> Roots = new();

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

        // A removal takes its root's entry, so nothing reads the root twice.
        internal PlannedRoot? Take(VisualElement root) => Roots.Remove(root, out var planned) ? planned : null;

        internal static DeletionPlan Rent() => s_pool.Count > 0 ? s_pool.Pop() : new DeletionPlan();

        internal void Release()
        {
            PreOrderTeardown.ClearAndTrim(Steps);
            PreOrderTeardown.ClearAndTrim(Roots);
            s_pool.Push(this);
        }
    }
}
