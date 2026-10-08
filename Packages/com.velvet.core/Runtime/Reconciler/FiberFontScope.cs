#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Lets a weight or italic class with no family class of its own resolve against the family it inherits,
    // the way CSS keeps an inherited font-family under a weight change. The family an element inherits is the
    // one named by the nearest ancestor carrying a font-<name> class, and only that class names it: an
    // ancestor that merely has a weight class contributes nothing, so a lookup skips it.
    //
    // Resolution order is the constraint. A parent's font layer runs after its children are created, and a
    // created element is parented only afterwards, so a lookup made while an element resolves can come back
    // empty or stale. Each such element is therefore queued, and Drain re-resolves it at the top-level pass
    // boundary, where an element created in the pass has its final parent (the same point RingOverlay.DrainPendingPlacements
    // runs). A change to an ancestor's family outside creation queues the inheritors under it the same way.
    //
    // Both tables are keyed by element and torn down with it, so they are enrolled in
    // ReconcilerContext's pure side-tables.
    internal sealed class FiberFontScope
    {
        // Elements whose class list names a family, with that family.
        internal Dictionary<VisualElement, string> Families { get; } = new();

        // Elements with a font class and no family, with the request and the inherited family it was last
        // resolved against (null when no ancestor names one, which leaves the request on DefaultFamily).
        internal Dictionary<VisualElement, (FontIntent Intent, string? Family)> Inheritors { get; } = new();

        private readonly HashSet<VisualElement> _pending = new();
        private readonly System.Predicate<VisualElement> _settle;

        public FiberFontScope() => _settle = Settle;

        internal string? InheritedFamily(VisualElement element)
        {
            if (Families.Count == 0)
            {
                return null;
            }

            for (var ancestor = element.parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (Families.TryGetValue(ancestor, out var family))
                {
                    return family;
                }
            }

            return null;
        }

        // Records what the element's font layer just resolved. created is true on the create path, where
        // everything under the element is queued already, so the subtree walk is skipped.
        internal void Track(VisualElement element, in FontIntent intent, string? inherited, bool created)
        {
            if (intent.HasFamily)
            {
                Inheritors.Remove(element);
                var changed = !Families.TryGetValue(element, out var previous) || previous != intent.Family;
                Families[element] = intent.Family!;
                if (changed && !created)
                {
                    QueueInheritorsUnder(element);
                }

                return;
            }

            var hadFamily = Families.Remove(element);
            Inheritors[element] = (intent, inherited);
            _pending.Add(element);
            if (hadFamily && !created)
            {
                QueueInheritorsUnder(element);
            }
        }

        // The element no longer carries a font class.
        internal void Untrack(VisualElement element)
        {
            Inheritors.Remove(element);
            if (Families.Remove(element))
            {
                QueueInheritorsUnder(element);
            }
        }

        private void QueueInheritorsUnder(VisualElement ancestor)
        {
            foreach (var inheritor in Inheritors.Keys)
            {
                if (ancestor.Contains(inheritor))
                {
                    _pending.Add(inheritor);
                }
            }
        }

        // Re-resolves each queued inheritor against the family it now inherits. An inheritor with no parent
        // yet stays queued: a parked pass reaches this boundary with elements it has not placed yet.
        internal void Drain()
        {
            if (_pending.Count != 0)
            {
                _pending.RemoveWhere(_settle);
            }
        }

        private bool Settle(VisualElement element)
        {
            if (!Inheritors.TryGetValue(element, out var entry))
            {
                return true;
            }

            if (element.parent == null)
            {
                return false;
            }

            var family = InheritedFamily(element);
            if (family != entry.Family)
            {
                StyleFontResolver.ApplyResolved(element, entry.Intent, family);
                Inheritors[element] = (entry.Intent, family);
            }

            return true;
        }
    }
}
