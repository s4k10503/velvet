using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Framework-level [&>*]:<utility> child-combinator variant: applies the wrapped utility to every direct
    // child of the container — the CSS `& > *` "all direct children" rule. UI Toolkit has no `> *` selector,
    // so this manipulator, attached to the CONTAINER (not the children), walks the container's child list and
    // delegates each child to StyleVariantPayload.Apply — the same resolver the variant manipulators use to
    // turn a payload into either an inline style (w-[8px]) or a toggled USS class (bg-red-500). Threading
    // (ctx, this) through Apply means a state-variant payload (hover:bg-red-500, even dark:hover:bg-red-500)
    // composes for free: each child gets its own per-child stacked manipulator gated open by this walk and
    // ANDed with the child's own hover / theme state, torn down with the child by the element cleaner.
    //
    // Lifecycle mirrors StyleGapManipulator / StyleDivideManipulator: the reconciler attaches one per
    // container carrying a [&>*]: token, keeps it in ReconcilerContext.ChildVariantManipulators, and removes
    // it on cleanup / dispose. UnregisterCallbacksFromTarget clears the payloads it applied so removing the
    // token (or unmounting) leaves no residue. Re-application has the same three sources as gap / divide: the
    // reconciler's post-children pass (the panel-independent path that also covers EditMode), a
    // GeometryChangedEvent (a child add / remove / reorder from an unrelated reconcile), and an
    // AttachToPanelEvent. A signature makes a redundant Apply a no-op.
    //
    // Child container. Like gap / divide it resolves and iterates FiberNodePatcher.GetChildContainer(target)
    // (a ScrollView's contentContainer; else self), so the payload lands on the reconciled content and never
    // on a ScrollView's internal hierarchy.
    //
    // Out-of-flow children (position: absolute) are excluded from the walk via StyleOutOfFlowChild, the same
    // way gap / divide exclude them. This is a deliberate deviation from literal CSS `> *` (which DOES match
    // an absolute child): the wrapped utility is arbitrary and may target margin / position, the exact write
    // that would corrupt a GeneralPathReconciler.PinExitingChildOutOfFlow ghost's frozen compensated position
    // — the risk gap / divide were built to avoid. A mid-exit ghost keeps whatever [&>*]: payload it carried
    // while still in flow, frozen through its exit.
    //
    // Precedence. The reconciler runs this pass BEFORE gap / divide / grid, so on a SHARED style property
    // (e.g. [&>*]:ml-[2px] alongside gap-x-4, both writing margin-left) gap / divide / grid win — consistent
    // with their documented precedence over ANY per-child margin / border / width source; [&>*]: is now just
    // another such source rather than a new special case.
    internal sealed class StyleChildVariantManipulator : Manipulator
    {
        private readonly ReconcilerContext _ctx;
        private string[] _payloads;

        // Every child this manipulator has applied the payload to. On each Apply / Clear any tracked element
        // no longer a current child has its payload turned off, so a child reparented or removed out of the
        // container keeps no residual class / inline style.
        private readonly List<VisualElement> _applied = new();

        // Signature of the last successful Apply: the current in-flow child identity set. Apply() early-returns
        // when unchanged, so a redundant reconcile pass (or the GeometryChanged feedback a class toggle may
        // provoke) does no work. A payload-set swap invalidates the cache through _hasSignature instead.
        private int _lastSignature;
        private bool _hasSignature;

        public StyleChildVariantManipulator(ReconcilerContext ctx, string[] payloads)
        {
            _ctx = ctx;
            _payloads = payloads ?? System.Array.Empty<string>();
        }

        // Swaps the payload set and re-applies. When the set actually CHANGED, the OLD set is turned off on
        // every currently-applied child first: Apply() only ever ADDS the current set, so a dropped / changed
        // token would otherwise stay stuck on every child forever. An unchanged set skips the turn-off and
        // lets Apply re-derive against the live child set through its signature. Mirrors
        // StyleHasVariantManipulator.UpdatePayloads's pre-clear, NOT StyleGapManipulator.UpdateGap (gap never
        // needs it — it always overwrites the SAME fixed property with a new scalar, not a variable class set).
        public void UpdatePayloads(string[] payloads)
        {
            payloads ??= System.Array.Empty<string>();
            if (SamePayloads(payloads))
            {
                Apply();
                return;
            }
            if (target != null)
            {
                foreach (var child in _applied)
                {
                    ApplyPayloads(child, false);
                }
            }
            _payloads = payloads;
            _applied.Clear();
            _hasSignature = false;
            Apply();
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Apply();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Clear();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        // The container children are reconciled into (a ScrollView's contentContainer; else self).
        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Applies the payload set to every in-flow direct child. Resets the payload on any tracked child no
        // longer in the container first. Early-returns when the in-flow child set is unchanged.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            var signature = ComputeSignature(container);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Reset any previously-applied element that is no longer a current child before re-applying, so a
            // child moved or removed out of this container drops the class / inline style it carried.
            ResetStaleApplied(container);
            _applied.Clear();

            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // An out-of-flow child (a PopLayout-pinned ghost, or an app-authored .absolute child) holds no
                // slot in the flex line and its position is fragile — leave whatever payload it carried while
                // in flow untouched rather than re-writing an arbitrary margin / position onto a pinned ghost.
                if (StyleOutOfFlowChild.IsOutOfFlow(child))
                {
                    continue;
                }
                ApplyPayloads(child, true);
                _applied.Add(child);
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        // Clears every payload this manipulator applied (invoked on detach / removal / token drop).
        private void Clear()
        {
            foreach (var child in _applied)
            {
                ApplyPayloads(child, false);
            }
            _applied.Clear();
            _hasSignature = false;
        }

        // Turns the payload off on any tracked child that has left the container, then prunes it — so a child
        // reparented or removed out of the container drops the class / inline style this manipulator wrote.
        private void ResetStaleApplied(VisualElement container)
        {
            for (var i = _applied.Count - 1; i >= 0; i--)
            {
                var child = _applied[i];
                if (child.parent != container)
                {
                    ApplyPayloads(child, false);
                    _applied.RemoveAt(i);
                }
            }
        }

        // Applies (on) or clears (off) the payload set on one child. owner == this is threaded through so a
        // state-variant payload defers to a per-child stacked manipulator gated by this walk, exactly as the
        // has- manipulator threads itself through for its own composed payloads.
        private void ApplyPayloads(VisualElement child, bool on)
            => StyleVariantPayload.Apply(child, _payloads, on, StyleLayerPriority.ChildVariant, _ctx, this);

        private bool SamePayloads(string[] payloads)
        {
            if (payloads.Length != _payloads.Length)
            {
                return false;
            }
            for (var i = 0; i < payloads.Length; i++)
            {
                if (payloads[i] != _payloads[i])
                {
                    return false;
                }
            }
            return true;
        }

        // Order-sensitive hash of the inputs that change what the walk applies: the current child identity
        // sequence and each child's in-flow / out-of-flow state. Apply() early-returns when this matches the
        // last application; a payload-set swap invalidates the cache through _hasSignature instead.
        private int ComputeSignature(VisualElement container)
        {
            unchecked
            {
                var hash = 17;
                var count = container.childCount;
                hash = hash * 31 + count;
                for (var i = 0; i < count; i++)
                {
                    var child = container[i];
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child);
                    // A child's in-flow / out-of-flow transition (a PopLayout pin or its cancel) changes which
                    // children the walk applies to even though neither its identity nor the total count changed.
                    hash = hash * 31 + (StyleOutOfFlowChild.IsOutOfFlow(child) ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
