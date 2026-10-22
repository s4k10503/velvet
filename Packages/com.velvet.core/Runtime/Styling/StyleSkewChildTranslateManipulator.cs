using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Approximates CSS skewX / skewY's descendant shear on a skewed caster's direct children. UI Toolkit's
    // transform has no shear, so SkewSilhouette paints only the caster's own box sheared while its children
    // stay axis-aligned; without compensation they float free of the slanted frame. This manipulator seats
    // each in-flow child's CENTROID where the shear would carry it, using the same model SilhouetteFace.Shear
    // documents. The compensation is exact only at each child's centroid and piecewise-constant across the
    // child (a real shear also rotates it), so a child large relative to the caster reads slightly off at its
    // far corners.
    //
    // The seat composes with the child's own translate as CSS composes the parent's transform with the child's:
    // the child shows its own translate plus the shear's displacement at its translated centroid
    // (SeatedTranslate). The writers of an element's own translate — the arbitrary-value resolver, the Motion
    // drivers, the layoutId projection, the animate driver and a drag — go through SeatedTranslate, so a static
    // translate-x-*, a Motion driver's frame or a drag keeps moving the child while it is seated, and the
    // child's own value is what is left when the seat goes.
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
    // event. Out-of-flow children (StyleOutOfFlowChild) hold no seat — which also excludes the caster's own
    // SilhouetteBoundsSpacer and any PopLayout-pinned exit ghost. A child that leaves the container, goes
    // out-of-flow or leaves the panel is unseated, which writes its own translate back.
    internal sealed class StyleSkewChildTranslateManipulator : Manipulator
    {
        private readonly SkewBinding _binding;

        // Every child this seated (SeatedTranslate.Seat), so unskew and relinquish can unseat it.
        private readonly HashSet<VisualElement> _seated = new();

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

        // The children are reconciled into (a composite widget's inner box; else the caster itself).
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
            // For a composite widget the children live in its inner box — a ScrollView's content container has the
            // full unclamped content extent, not the visible caster box; reading the box from the container would
            // centre the shear on the content midpoint and seat every child against a line the silhouette never paints.
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
                SeatedTranslate.Seat(child, new SeatFrame(
                    layout.x + (layout.width * 0.5f), layout.y + (layout.height * 0.5f), w, h, tanX, tanY));
                if (_seated.Add(child))
                {
                    child.RegisterCallback<DetachFromPanelEvent>(OnChildDetach);
                }
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        // The sheared displacement of a child centroid on a caster of the given box and shear tangents, from
        // the model SilhouetteFace.Shear documents.
        // Pure and panel-independent so the seat math is unit-testable without a layout pass.
        internal static Vector2 ComputeOffset(float childCenterX, float childCenterY,
            float containerWidth, float containerHeight, float tanX, float tanY)
        {
            var u = childCenterX - (containerWidth * 0.5f);
            var v = childCenterY - (containerHeight * 0.5f);
            return new Vector2((u * tanX * tanY) + (v * tanX), u * tanY);
        }

        // Unseats every seated child, which writes its own translate back. Invoked on unskew / detach.
        private void Clear()
        {
            foreach (var child in _seated)
            {
                SeatedTranslate.Unseat(child);
                child.UnregisterCallback<DetachFromPanelEvent>(OnChildDetach);
            }
            _seated.Clear();
            _hasSignature = false;
        }

        // Unseats every seated child that has left the container or gone out-of-flow, before the re-seat, which
        // re-seats one that returns.
        private void DropUnmanaged(VisualElement container)
        {
            List<VisualElement>? stale = null;
            foreach (var child in _seated)
            {
                if (!IsManaged(child, container))
                {
                    (stale ??= new List<VisualElement>()).Add(child);
                }
            }
            if (stale != null)
            {
                foreach (var child in stale)
                {
                    Forget(child);
                }
            }
        }

        // A seated child leaving the panel is unseated at once, so the pool cannot hand the instance to an
        // unrelated node still seated. The event trickles through the detaching subtree, so act only when this
        // child is itself the one leaving.
        private void OnChildDetach(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is VisualElement child && ReferenceEquals(evt.target, child))
            {
                Forget(child);
            }
        }

        // The single relinquish primitive DropUnmanaged and OnChildDetach funnel through; idempotent.
        private void Forget(VisualElement child)
        {
            if (_seated.Remove(child))
            {
                SeatedTranslate.Unseat(child);
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

    // Where a skewed caster seats one child: its laid-out centroid, the caster's box and the shear tangents.
    internal readonly struct SeatFrame
    {
        public SeatFrame(float centerX, float centerY, float width, float height, float tanX, float tanY)
        {
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
            TanX = tanX;
            TanY = tanY;
        }

        public float CenterX { get; }
        public float CenterY { get; }
        public float Width { get; }
        public float Height { get; }
        public float TanX { get; }
        public float TanY { get; }
    }

    // An element's inline translate as its own writers see it, and as a skewed caster's seat composes it
    // (StyleSkewChildTranslateManipulator). On an unseated element Read and Write are the slot itself; on a
    // seated one the slot shows the composition and Read gives back what was written.
    internal static class SeatedTranslate
    {
        private sealed class Seated
        {
            public StyleTranslate Own;
            public SeatFrame Frame;
        }

        private static readonly ConditionalWeakTable<VisualElement, Seated> s_seated = new();

        internal static StyleTranslate Read(VisualElement element)
            => s_seated.TryGetValue(element, out var seated) ? seated.Own : element.style.translate;

        internal static void Write(VisualElement element, StyleTranslate value)
        {
            if (!s_seated.TryGetValue(element, out var seated))
            {
                element.style.translate = value;
                return;
            }
            seated.Own = value;
            element.style.translate = Compose(value, seated.Frame);
        }

        // Seats element at frame, taking what its slot holds as its own translate when it was not seated.
        internal static void Seat(VisualElement element, in SeatFrame frame)
        {
            if (!s_seated.TryGetValue(element, out var seated))
            {
                seated = new Seated { Own = element.style.translate };
                s_seated.Add(element, seated);
            }
            seated.Frame = frame;
            element.style.translate = Compose(seated.Own, frame);
        }

        internal static void Unseat(VisualElement element)
        {
            if (s_seated.TryGetValue(element, out var seated))
            {
                s_seated.Remove(element);
                element.style.translate = seated.Own;
            }
        }

        // resolved, a translate element is drawn with, less the displacement its seat composes into it, so what is
        // left is the element's own.
        internal static Vector3 Unseated(VisualElement element, Vector3 resolved)
        {
            if (!s_seated.TryGetValue(element, out var seated))
            {
                return resolved;
            }
            var own = seated.Own;
            var shown = Compose(own, seated.Frame).value;
            var x = own.keyword == StyleKeyword.Undefined ? own.value.x : new Length(0f);
            var y = own.keyword == StyleKeyword.Undefined ? own.value.y : new Length(0f);
            var dx = x.unit == LengthUnit.Pixel ? shown.x.value - x.value : 0f;
            var dy = y.unit == LengthUnit.Pixel ? shown.y.value - y.value : 0f;
            return new Vector3(resolved.x - dx, resolved.y - dy, resolved.z);
        }

        // Drops a seat without writing, for an element whose style is being scrubbed anyway.
        internal static void Forget(VisualElement element) => s_seated.Remove(element);

        // own plus the shear's displacement at the centroid own moves the child to. An axis own gives in a unit
        // other than pixels cannot be summed with a pixel offset, so it is shown as written.
        internal static StyleTranslate Compose(StyleTranslate own, in SeatFrame frame)
        {
            var set = own.keyword == StyleKeyword.Undefined;
            var x = set ? own.value.x : new Length(0f);
            var y = set ? own.value.y : new Length(0f);
            var tx = x.unit == LengthUnit.Pixel ? x.value : 0f;
            var ty = y.unit == LengthUnit.Pixel ? y.value : 0f;
            var offset = StyleSkewChildTranslateManipulator.ComputeOffset(frame.CenterX + tx, frame.CenterY + ty,
                frame.Width, frame.Height, frame.TanX, frame.TanY);
            return new Translate(
                x.unit == LengthUnit.Pixel ? new Length(tx + offset.x) : x,
                y.unit == LengthUnit.Pixel ? new Length(ty + offset.y) : y,
                set ? own.value.z : 0f);
        }
    }
}
