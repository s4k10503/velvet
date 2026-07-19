using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Approximates CSS skewX / skewY's descendant shear on a skewed caster's direct children. UI Toolkit's
    // transform has no shear, so SkewSilhouette paints only the caster's own box sheared while its children
    // stay axis-aligned; without compensation they float free of the slanted frame. This manipulator writes
    // each in-flow child a translate that seats its CENTROID where the shear would carry it, using the same
    // model SilhouetteBoundsSpacer.ShearedAabb documents: a point (x, y) maps to
    // (x + (y - h/2)*tanX, y + (x - w/2)*tanY). The compensation is exact only at each child's centroid and
    // piecewise-constant across the child (a real shear also rotates it), so a child large relative to the
    // caster reads slightly off at its far corners. It is the manual counter-translate a CSS author would
    // otherwise hand-write per row, made automatic.
    //
    // Owned by the caster's SkewBinding rather than a ReconcilerContext dictionary, so every
    // SkewSilhouette.Detach path (pool reset, root disposal) tears it down for free. Lifecycle mirrors
    // StyleGapManipulator: the caster's own AttachToPanelEvent / GeometryChangedEvent re-seat children as
    // layout settles, and the reconciler re-runs Apply on patch (SkewSilhouette.SyncChildTranslate) so a child
    // add / remove / reorder re-seats even when the caster's own box did not change size — the case that fires
    // no GeometryChangedEvent.
    //
    // Unlike gap's layout-independent margin writes, the centroid math needs a real Yoga pass: pre-layout it
    // no-ops (the same NaN / non-positive guard SkewSilhouette.Draw uses) and defers to the first geometry
    // event. Out-of-flow children (StyleOutOfFlowChild) are skipped — which also excludes the caster's own
    // SilhouetteBoundsSpacer and any PopLayout-pinned exit ghost, whose frozen position a translate would
    // fight. While active the manipulator OWNS each direct child's translate slot: an explicit translate-x-*
    // on such a child, or a live spring / drag write, is overwritten every Apply (the same limitation class as
    // gap's ml-2 overwrite); a needs-its-own-translate child wants an inner wrapper. To make a STATIC
    // translate-x-* survive losing the parent's skew, the child's own inline translate is CAPTURED before the
    // first shear overwrite and written back on reset — GetClasses cannot recover it, since translate has no
    // USS form and the reconciler applies translate-x-* inline without recording it in the class list. At reset
    // the capture is written back ONLY onto a child the manipulator still owns (a direct in-flow child): one that
    // moved out of the container or was recycled by the element pool for another node is released untouched, so a
    // translate it legitimately acquired after leaving management is never clobbered by a stale capture. A child
    // that transitions to OUT-OF-FLOW while still attached is handled eagerly instead: it holds no seat in the
    // slanted frame yet still carries the shear write, so its captured pre-shear translate is restored and it is
    // dropped — but ONLY while it still carries the exact shear the manipulator last wrote. The out-of-flow
    // signal is resolvedStyle-based and can lag the class change by a re-seat pass, so a child can go out-of-flow
    // AND author its own translate before the relinquish fires; a current translate that no longer matches the
    // last shear is that authored value, and is left untouched rather than clobbered back to the stale capture.
    // Restoring only the still-untouched shear keeps a stale offset from lingering on a seatless element and from
    // being re-captured as a 'baseline' on a later return to flow. A child leaving the panel drops itself from
    // tracking the instant it detaches, closing the window where the pool could reuse its instance while a stale
    // capture was still keyed to it.
    internal sealed class StyleSkewChildTranslateManipulator : Manipulator
    {
        private readonly SkewBinding _binding;

        // A tracked child's captured pre-shear OWN translate paired with the LAST shear the manipulator wrote to
        // it. Own restores the child's static translate-x-* on relinquish (a child with none was captured as
        // Null); Shear is the proof-of-ownership probe — the child still carrying that exact value means the
        // manipulator's write is intact and safe to clear, whereas any other value is one the child authored
        // itself and must survive.
        private readonly struct SeatState
        {
            public readonly StyleTranslate Own;
            public readonly StyleTranslate Shear;

            public SeatState(StyleTranslate own, StyleTranslate shear)
            {
                Own = own;
                Shear = shear;
            }

            public SeatState WithShear(StyleTranslate shear) => new SeatState(Own, shear);
        }

        // Every child a shear translate was written to, mapped to its seat state. Unskew writes the captured Own
        // back onto a child the manipulator still owns, so its static translate-x-* is preserved rather than
        // erased. A child that goes out-of-flow while still attached is restored to its capture and dropped — but
        // only while it still carries the last Shear, so a translate it authored across the transition survives;
        // one that has left the container entirely is dropped untouched, its translate now legitimately its own.
        private readonly Dictionary<VisualElement, SeatState> _seated = new();

        // Signature of the last successful Apply. Apply() early-returns when it is unchanged, so the
        // GeometryChanged churn the manipulator's own writes provoke, and patches that touch nothing relevant,
        // are no-ops.
        private int _lastSignature;
        private bool _hasSignature;

        public StyleSkewChildTranslateManipulator(SkewBinding binding)
        {
            _binding = binding;
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
            // layout only becomes meaningful on a panel; force a re-seat once attached.
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        // The children are reconciled into (a ScrollView's contentContainer; else the caster itself).
        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Seats every in-flow child's centroid at its sheared position. Reads the caster's live skew off the
        // shared binding (single source with SkewSilhouette.Draw). No-op pre-layout (deferred to the first
        // geometry event) and when nothing relevant changed since the last pass.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            // The shear box is the CASTER's OWN layout — the box SkewSilhouette.Draw shears the silhouette at.
            // For a ScrollView the children live in contentContainer, whose resolved height is the full unclamped
            // content extent, not the visible caster box; reading the box from the container would centre the
            // shear on the content midpoint and seat every child against a line the silhouette never paints.
            // Children are still enumerated from the container, where their laid-out centroids live; for a plain
            // element the container IS the caster, so the two boxes coincide.
            var box = target.layout;
            var w = box.width;
            var h = box.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            var tanX = Mathf.Tan(_binding.Spec.XDeg * Mathf.Deg2Rad);
            var tanY = Mathf.Tan(_binding.Spec.YDeg * Mathf.Deg2Rad);

            var signature = ComputeSignature(container, w, h, tanX, tanY);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Relinquish any tracked child the manipulator no longer owns before re-seating the rest.
            DropUnmanaged(container);

            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // An out-of-flow child holds no seat in the slanted frame: the bounds-spacer, a PopLayout exit
                // ghost (whose pinned position a translate would fight), or an app-authored .absolute child.
                if (StyleOutOfFlowChild.IsOutOfFlow(child))
                {
                    continue;
                }
                var layout = child.layout;
                if (float.IsNaN(layout.x) || float.IsNaN(layout.y)
                    || float.IsNaN(layout.width) || float.IsNaN(layout.height))
                {
                    // A freshly added child not yet through a Yoga pass — seat it on the next geometry event.
                    continue;
                }
                var offset = ComputeOffset(
                    layout.x + (layout.width * 0.5f), layout.y + (layout.height * 0.5f), w, h, tanX, tanY);
                StyleTranslate shear = new Translate(new Length(offset.x), new Length(offset.y));
                // Capture the child's own translate once, before the first overwrite, so unskew can restore it;
                // record the shear just written so relinquish can tell a still-owned seat from a translate the
                // child later authored for itself.
                if (_seated.TryGetValue(child, out var seat))
                {
                    _seated[child] = seat.WithShear(shear);
                }
                else
                {
                    _seated[child] = new SeatState(child.style.translate, shear);
                    // A seated child leaving the panel drops itself from tracking before the element pool can
                    // hand the instance to an unrelated node, so a later restore never lands a stale capture.
                    child.RegisterCallback<DetachFromPanelEvent>(OnChildDetach);
                }
                child.style.translate = shear;
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        // The sheared displacement of a child centroid on a caster of the given box and shear tangents, from
        // the model SilhouetteBoundsSpacer.ShearedAabb documents: x' = x + (y - h/2)*tanX, y' = y + (x - w/2)*tanY.
        // Pure and panel-independent so the seat math is unit-testable without a layout pass.
        internal static Vector2 ComputeOffset(float childCenterX, float childCenterY,
            float containerWidth, float containerHeight, float tanX, float tanY)
        {
            var dx = (childCenterY - (containerHeight * 0.5f)) * tanX;
            var dy = (childCenterX - (containerWidth * 0.5f)) * tanY;
            return new Vector2(dx, dy);
        }

        // Restores the captured translate onto every child the manipulator wrote a shear to, then stops tracking
        // all. Invoked on unskew / detach, which can fire before any re-seat has relinquished a child that just
        // went out-of-flow — so this teardown must relinquish such a child the same way DropUnmanaged would, not
        // skip it. A still-owned in-flow child is restored unconditionally (it always carries the latest shear); a
        // child now out-of-flow but still attached and STILL carrying the exact shear is restored too, so the
        // stale offset does not leak on a seatless element (and cannot be re-captured as its 'own' baseline on a
        // later return to flow). A child that has left the container — or one out-of-flow that already carries a
        // translate it authored across the transition — keeps that value: forcing the stale capture back would clobber it.
        private void Clear()
        {
            var container = ChildContainer;
            foreach (var pair in _seated)
            {
                var child = pair.Key;
                if (IsManaged(child, container))
                {
                    child.style.translate = pair.Value.Own;
                }
                else
                {
                    RestoreRelinquishedShear(child, pair.Value, container);
                }
                child.UnregisterCallback<DetachFromPanelEvent>(OnChildDetach);
            }
            _seated.Clear();
            _hasSignature = false;
        }

        // Relinquishes every tracked child the manipulator no longer owns before the re-seat, in two kinds. A
        // child still under this container but now OUT-OF-FLOW may still carry the shear translate the manipulator
        // wrote while holding no seat in the slanted frame — restore its captured pre-shear translate so that
        // stale offset does not linger (leaving it would also let a later return to flow re-capture the shear as
        // the child's 'own' baseline and restore it forever on a future unskew), but only while it still carries
        // that exact shear so a translate it authored across the transition is not clobbered. A child that has
        // LEFT the container keeps whatever translate it has: it is legitimately its own now, exactly as Clear
        // leaves it. Either way the drop frees a re-seat to re-capture a fresh baseline should the child return.
        private void DropUnmanaged(VisualElement container)
        {
            List<VisualElement>? stale = null;
            foreach (var pair in _seated)
            {
                if (!IsManaged(pair.Key, container))
                {
                    (stale ??= new List<VisualElement>()).Add(pair.Key);
                }
            }
            if (stale != null)
            {
                foreach (var child in stale)
                {
                    // A still-parented non-managed child went out-of-flow (the one non-managed reason left once
                    // container membership holds); restore its pre-shear capture, guarded so a translate it
                    // authored across the same transition survives. See RestoreRelinquishedShear.
                    RestoreRelinquishedShear(child, _seated[child], container);
                    Forget(child);
                }
            }
        }

        // Restores a relinquished child's captured pre-shear translate, but ONLY while it is still attached to the
        // container AND still carries the exact shear the manipulator last wrote. The out-of-flow signal is
        // resolvedStyle-based and lags the class change, so a child can author its own translate across the same
        // transition: a current value that no longer matches the shear is that authored translate and must
        // survive. A child that has left the container is not restored — its translate is legitimately its own.
        private static void RestoreRelinquishedShear(VisualElement child, in SeatState seat, VisualElement? container)
        {
            if (container != null && child.parent == container && child.style.translate == seat.Shear)
            {
                child.style.translate = seat.Own;
            }
        }

        // A seated child leaving the panel is on its way to the element pool (or the whole tree is unmounting).
        // Forget it now — without a write, since the pool scrubs translate on return — so the pool cannot hand
        // the instance to an unrelated node while this manipulator still holds a stale capture keyed to it. The
        // event trickles through the detaching subtree, so act only when this child is itself the one leaving.
        private void OnChildDetach(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is VisualElement child && ReferenceEquals(evt.target, child))
            {
                Forget(child);
            }
        }

        // The single relinquish primitive both DropUnmanaged and OnChildDetach funnel through. The Remove gate
        // makes it idempotent: whichever path fires second finds the child already gone, so the detach callback
        // is never double-unregistered and an already-relinquished element is never touched again.
        private void Forget(VisualElement child)
        {
            if (_seated.Remove(child))
            {
                child.UnregisterCallback<DetachFromPanelEvent>(OnChildDetach);
            }
        }

        // A child the manipulator currently owns: a direct in-flow child of the container. A child that has left
        // the container or gone out-of-flow has been relinquished and its translate is its own.
        private static bool IsManaged(VisualElement child, VisualElement? container)
            => container != null && child.parent == container && !StyleOutOfFlowChild.IsOutOfFlow(child);

        // Order-sensitive hash of everything the seat depends on: the caster box, both shear tangents, and each
        // child's identity, in / out-of-flow state, and laid-out rect. Unlike gap's index-only signature the
        // rect is included — a child that resizes in place shifts its centroid, so the compensation must change
        // even though the child set did not.
        private int ComputeSignature(VisualElement container, float w, float h, float tanX, float tanY)
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + w.GetHashCode();
                hash = (hash * 31) + h.GetHashCode();
                hash = (hash * 31) + tanX.GetHashCode();
                hash = (hash * 31) + tanY.GetHashCode();
                var count = container.childCount;
                hash = (hash * 31) + count;
                for (var i = 0; i < count; i++)
                {
                    var child = container[i];
                    hash = (hash * 31) + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child);
                    hash = (hash * 31) + (StyleOutOfFlowChild.IsOutOfFlow(child) ? 1 : 0);
                    var layout = child.layout;
                    hash = (hash * 31) + layout.x.GetHashCode();
                    hash = (hash * 31) + layout.y.GetHashCode();
                    hash = (hash * 31) + layout.width.GetHashCode();
                    hash = (hash * 31) + layout.height.GetHashCode();
                }
                return hash;
            }
        }
    }
}
