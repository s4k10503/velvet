#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // pointer-events-none / pointer-events-auto. CSS inherits the property, while an Ignore pickingMode excludes only
    // the element it is set on from hit testing, which descends through the raw hierarchy (PickingModeEngineTests
    // pins both). So an element carrying pointer-events-none takes a PickingHold on every element its subtree
    // reaches, and an element carrying either utility is where an enclosing scope's walk stops. That stop is the
    // whole of pointer-events-auto: the element keeps whatever mode it had, which is how an Ignore Velvet set on a
    // wrapper or a control set on itself survives inside an auto subtree.
    //
    // The walk follows the raw hierarchy, which is what reaches a control's internal parts. A z-* element hoisted
    // into a layer container is reached with the rest: the container is a child of the element's own parent.
    //
    // A context's scopes are walked again at the end of each of its top-level reconcile passes, or once at the end
    // of a batch drain for all of the drain's passes (OnPassEnd, OnDrainEnd), which is what enrols an element
    // mounted into the subtree since and lets go of one that has left it; an element that left by teardown is let
    // go of earlier, by FiberElementCleaner, before the pool can hand it to anyone else.
    //
    // The roots and the holders are kept across every context rather than on one, because a tree can be mounted
    // inside another tree's element: the outer scope's walk has to stop at an inner tree's root, the inner tree's
    // passes have to walk the outer scope they insert into (SyncEnclosing), and the inner tree's cleaner has to let
    // go of a hold the outer tree took.
    //
    // Not a Manipulator: it registers no callback. Nor does it ask StyleChildOwnership before letting go, as the
    // per-child manipulators do: PickingHold counts the holds, so letting go of one hands back no mode while another
    // scope or a layoutId member still holds the element.
    internal sealed class PointerEventsScope
    {
        private static readonly ConditionalWeakTable<VisualElement, PointerEventsScope> s_roots = new();
        private static readonly ConditionalWeakTable<VisualElement, List<PointerEventsScope>> s_holders = new();
        // Scopes created and not yet released, across every context, so a pass in a process with none pays nothing.
        private static int s_liveScopes;

        private readonly VisualElement _root;
        private HashSet<VisualElement> _held = new();
        private HashSet<VisualElement> _reached = new();

        public PointerEventsScope(VisualElement root, PointerEventsMode mode)
        {
            _root = root;
            Mode = mode;
            s_roots.AddOrUpdate(root, this);
            s_liveScopes++;
        }

        // Setting it takes and drops nothing: Sync does that.
        public PointerEventsMode Mode { get; set; }

        // Inside a pass the pass's own end (OnPassEnd) walks the scopes, by which point every element the pass mounts
        // is in place.
        internal static void RequestSyncAll(ReconcilerContext ctx)
        {
            if (ctx.SharedReconcileDepth > 0)
            {
                return;
            }
            SyncAll(ctx);
        }

        // The end of a top-level pass (Reconciler.FinishTopLevelPass). Inside a batch drain the drain's end does the
        // walking instead, once for all of its passes, so the element the pass reconciled into is put aside for it.
        internal static void OnPassEnd(ReconcilerContext ctx, VisualElement? reconciledInto)
        {
            if (s_liveScopes == 0)
            {
                return;
            }
            if (ctx.DeferDrainLayoutEffects)
            {
                if (reconciledInto != null)
                {
                    ctx.PointerEventsAnchors.Add(reconciledInto);
                }
                return;
            }
            SyncAll(ctx);
            SyncEnclosing(ctx, reconciledInto);
        }

        internal static void OnDrainEnd(ReconcilerContext ctx)
        {
            SyncAll(ctx);
            foreach (var anchor in ctx.PointerEventsAnchors)
            {
                SyncEnclosing(ctx, anchor);
            }
            // MUTANT_SURVIVES(equivalent): an anchor kept past its drain is walked again at the next one, and walking
            // a scope over an unchanged tree takes and drops nothing; clearing only stops the set holding elements.
            ctx.PointerEventsAnchors.Clear();
        }

        internal static void SyncAll(ReconcilerContext ctx)
        {
            if (ctx.PointerEventsScopes.Count == 0)
            {
                return;
            }
            foreach (var scope in ctx.PointerEventsScopes.Values)
            {
                scope.Sync();
            }
        }

        // A tree mounted, or portalled, into an element of another tree's scope: that scope belongs to a context
        // whose own passes never see this tree's elements arrive, so the pass that inserted them walks it.
        private static void SyncEnclosing(ReconcilerContext ctx, VisualElement? reconciledInto)
        {
            for (var element = reconciledInto; element != null; element = element.hierarchy.parent)
            {
                // MUTANT_SURVIVES(equivalent, clause removed): a scope of this context was walked by SyncAll just
                // before, and walking it again over the same tree takes and drops nothing.
                if (s_roots.TryGetValue(element, out var scope) && !ctx.PointerEventsScopes.ContainsKey(element))
                {
                    scope.Sync();
                }
            }
        }

        // Called for each element FiberElementCleaner tears down. Descends only through elements a scope has held: a
        // control's internal parts are held with it and are reached by no cleanup of their own.
        internal static void ReleaseTorn(VisualElement element)
        {
            if (!s_holders.TryGetValue(element, out var holders))
            {
                return;
            }
            for (var i = holders.Count - 1; i >= 0; i--)
            {
                holders[i].LetGo(element);
            }
            var hierarchy = element.hierarchy;
            for (var i = 0; i < hierarchy.childCount; i++)
            {
                ReleaseTorn(hierarchy[i]);
            }
        }

        public void Sync()
        {
            if (Mode == PointerEventsMode.None)
            {
                Reach(_root);
            }
            foreach (var element in _held)
            {
                if (!_reached.Contains(element))
                {
                    Unhold(element);
                }
            }
            foreach (var element in _reached)
            {
                if (!_held.Contains(element))
                {
                    PickingHold.Take(element);
                    s_holders.GetOrCreateValue(element).Add(this);
                }
            }
            (_held, _reached) = (_reached, _held);
            _reached.Clear();
        }

        // Drops every hold this scope took, for a scope about to be discarded: its utility left its element, its
        // element is torn down, or its tree is disposed.
        public void Release()
        {
            foreach (var element in _held)
            {
                Unhold(element);
            }
            // MUTANT_SURVIVES(equivalent): every caller drops the scope right after, so no read of the set follows;
            // emptying it is what keeps a second Release from dropping each hold twice.
            _held.Clear();
            if (s_roots.Remove(_root))
            {
                s_liveScopes--;
            }
        }

        private void Reach(VisualElement element)
        {
            _reached.Add(element);
            var hierarchy = element.hierarchy;
            for (var i = 0; i < hierarchy.childCount; i++)
            {
                var child = hierarchy[i];
                if (!s_roots.TryGetValue(child, out _))
                {
                    Reach(child);
                }
            }
        }

        private void LetGo(VisualElement element)
        {
            if (_held.Remove(element))
            {
                Unhold(element);
            }
        }

        private void Unhold(VisualElement element)
        {
            PickingHold.Drop(element);
            if (s_holders.TryGetValue(element, out var holders))
            {
                // MUTANT_SURVIVES(equivalent): a scope left listed answers no to the _held check in LetGo, which is
                // the only reader of the list besides the walk ReleaseTorn takes into the torn element's subtree.
                holders.Remove(this);
            }
        }
    }
}
