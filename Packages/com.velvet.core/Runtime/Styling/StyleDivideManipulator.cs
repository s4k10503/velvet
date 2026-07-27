using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Framework-level divide-* polyfill. The divide-x / divide-y utilities draw a border between adjacent
    // children (`& > * + *`): a border on every child but the first, horizontal for divide-x, vertical for
    // divide-y. UI Toolkit (6000.3) has no :first-child / :first-of-type and no `> *` child combinator,
    // so a USS rule cannot express "border on all children except the first." Velvet owns the ordered
    // child list, so this manipulator writes the inter-child border on every child EXCEPT the first — a
    // divider strictly BETWEEN children, never on the container's outer edges.
    //
    // Which physical edge of the axis carries that border follows the container's resolved direction (see
    // ResolveEdge): a row-reverse / column-reverse container paints its children in the opposite order, so
    // the edge that sits between a given pair is the axis's TRAILING one (border-right / border-bottom).
    // Picking the edge from the axis alone would draw one rule on the container's outer edge and leave the
    // boundary between the visually adjacent pair blank. divide-x-reverse / divide-y-reverse ask for that
    // same trailing edge unconditionally.
    //
    // Lifecycle mirrors StyleGapManipulator: the reconciler attaches one per divide container, keeps it
    // in ReconcilerContext.DivideManipulators, and removes it on cleanup / dispose.
    // UnregisterCallbacksFromTarget clears the borders it wrote so removing the divide class (or
    // unmounting) leaves no residue. Re-application has the same three sources as the gap manipulator:
    // the reconciler's post-child-reconcile call (the panel-independent path that also covers EditMode),
    // GeometryChangedEvent (child add / remove / reorder from an unrelated reconcile), and
    // AttachToPanelEvent (the one path that can answer a direction set outside the class list). A
    // signature makes a redundant Apply (notably the GeometryChanged feedback its own writes provoke) a
    // no-op.
    //
    // Line style: divide-solid is a plain inline border. divide-dashed / divide-dotted have no UI Toolkit
    // border-style, so the manipulator still reserves the SAME gutter (the real width) but masks the color
    // with the sentinel and hands each divided child a DivideDashChildBinding (DivideDashPainter) that paints
    // the dashed / dotted stroke on the child's own generateVisualContent — so switching between solid and
    // dashed is layout-identical and only the paint differs.
    //
    // Child container. Like the gap manipulator it resolves and iterates
    // FiberNodePatcher.GetChildContainer(target) (a composite widget's inner box; else self), so the
    // divider lands on the reconciled content and never on the widget's internal hierarchy. The direction
    // verdict comes from that same child container too — a direction class on such a widget reverses the
    // widget's own box, not the content the dividers separate.
    //
    // Limitations: an explicit per-child border on the SAME edge the divider draws on (e.g. border-l on a
    // child of a divide-x row) is OVERWRITTEN — this manipulator owns that edge, exactly as the gap
    // manipulator owns its margin edge. A child whose border face is owned by a higher paint layer — a skew
    // silhouette or a drop shadow — keeps its border owned there, so its dashed divider renders solid (a
    // documented known limitation, mirroring the element-level border-dashed gate which defers to either).
    // An IMPLICIT (no divide-{color}) dashed divider takes its color from the divided child's would-be border
    // color, captured and re-resolved on the CONTAINER's Apply (reconcile / GeometryChanged / attach) — the same
    // container-Apply cadence the gap manipulator runs on. A child that re-renders on its own fiber alone, with
    // no container Apply, keeps the last captured color until an unrelated container reconcile.
    //
    // Out-of-flow children (position: absolute) are excluded from the index walk — see
    // StyleOutOfFlowChild — the same way StyleGapManipulator excludes them: an out-of-flow child (a
    // PopLayout-pinned ghost, or an app-authored .absolute child) is not a layout sibling, so it neither
    // draws a divider nor counts toward which of the remaining children is "first".
    internal sealed class StyleDivideManipulator : Manipulator
    {
        private DivideSpec _spec;
        private readonly ReconcilerContext _ctx;

        // Which of the four edges is currently written, so an axis flip OR a same-axis leading↔trailing flip
        // (a reverse marker appearing, a container becoming reversed) clears the abandoned edge before
        // writing the new one. Null until the first application.
        private DivideEdge? _applied;

        // Bit per DivideEdge this manipulator has applied at least once. A child that leaves the container
        // may still carry a border from an edge an earlier flip abandoned while that child was a member, so
        // the departure / teardown reset needs the union rather than just the current edge — and no more than
        // the union, so a child's own border on an edge this divide never claimed survives.
        private int _everApplied;

        // Every child this manipulator has written a divider border to. On each Apply / Clear any tracked
        // element no longer a current child has its divider border reset, so a child reparented or removed
        // out of the divide container keeps no residual inline border.
        private readonly List<VisualElement> _bordered = new();

        private int _lastSignature;
        private bool _hasSignature;

        // The inner box this manipulator additionally watches for geometry changes, when the child
        // container is not the target itself. Null for a plain element. See ObserveChildContainer.
        private VisualElement? _observed;

        public StyleDivideManipulator(DivideSpec spec, ReconcilerContext ctx)
        {
            _spec = spec;
            _ctx = ctx;
        }

        // Swaps the spec and re-applies, clearing the old edge first if the resolved edge changed.
        public void UpdateSpec(DivideSpec spec)
        {
            _spec = spec;
            _hasSignature = false;
            Apply();
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            ObserveChildContainer();
            Apply();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Clear();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            if (_observed != null)
            {
                _observed.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                _observed = null;
            }
        }

        // Watches a child container that is NOT the target, for the reason StyleGapManipulator's own
        // ObserveChildContainer gives: the direction verdict is read from a composite widget's inner box,
        // and GeometryChangedEvent neither bubbles nor trickles, so a re-layout confined to that box
        // reaches nothing registered on the target.
        private void ObserveChildContainer()
        {
            var container = ChildContainer;
            if (container == null || ReferenceEquals(container, target))
            {
                return;
            }
            _observed = container;
            container.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Writes the inter-child border for the current spec on the edge ResolveEdge picks, on every child
        // except the first. Clears the abandoned edge first whenever that edge changed. Early-returns when
        // nothing relevant (spec, edge, child set) changed since the last successful application.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            var edge = ResolveEdge(container);
            var signature = ComputeSignature(container, edge);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Must run before _bordered.Clear() below: it reads the pre-clear _bordered list to find children
            // that left the container.
            ResetStaleBordered(container);

            // Any of the four edges may be the one abandoned — an axis flip (divide-x ↔ divide-y) or a
            // same-axis leading↔trailing flip (a reverse marker, or the container's direction changing).
            if (_applied.HasValue && _applied.Value != edge)
            {
                ClearEdge(container, _applied.Value);
            }
            _applied = edge;
            _everApplied |= 1 << (int)edge;
            _bordered.Clear();

            var count = container.childCount;
            var logicalIndex = 0;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // An out-of-flow child is not a layout sibling, so it draws no divider and does not
                // consume the "first child" slot for whichever in-flow child follows it.
                if (StyleOutOfFlowChild.IsOutOfFlow(child))
                {
                    continue;
                }
                // The first child has no divider (the `> * + *` rule starts at the second child).
                var isDivider = logicalIndex != 0;
                ApplyToChild(child, edge, isDivider);
                _bordered.Add(child);
                logicalIndex++;
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        // Writes one child's divider on the given edge. Solid keeps the plain inline-border path verbatim; a
        // dashed / dotted divider reserves the same gutter width, masks the native color with the sentinel, and
        // paints the stroke on the child's own generateVisualContent (DivideDashChildBinding).
        private void ApplyToChild(VisualElement child, DivideEdge edge, bool isDivider)
        {
            // A skew silhouette or a drop shadow owns the child's border face and repaints a solid border, so a
            // dashed divider on the same child would fight it — route it through the solid path (documented known
            // limitation). Gates on EITHER owner, mirroring the element-level border-dashed gate.
            var dashed = (_spec.Style == BorderLineStyle.Dashed || _spec.Style == BorderLineStyle.Dotted)
                && !_ctx.SkewBindings.ContainsKey(child)
                && !_ctx.ShadowBindings.ContainsKey(child);

            if (!dashed || !isDivider)
            {
                // Solid divider, the first child (no divider), or a child whose face a skew / shadow layer owns:
                // a plain inline border. Detach any stale dash paint (e.g. a divide-dashed → divide-solid flip, or
                // a colored child reordered to the first slot).
                DetachDash(child);
                var width = isDivider ? new StyleFloat(_spec.Width) : new StyleFloat(StyleKeyword.Null);
                // Own the edge's color channel on EVERY pass (like the gap manipulator owns its margin): write
                // the divider color only on a colored divider, else reset to Null so a dropped divide-{color}
                // class (or a colored child reordered to the first slot) leaves no stale inline color.
                var color = isDivider && _spec.HasColor ? new StyleColor(_spec.Color) : new StyleColor(StyleKeyword.Null);
                WriteEdge(child, edge, width, color);
                return;
            }

            // Dashed / dotted divider: resolve the paint color BEFORE masking. An explicit divide-{color} wins;
            // otherwise the child's would-be border color, re-resolved every pass so a class / theme change moving
            // it after the first bind is picked up (rather than captured once and cached forever).
            var hasBinding = _ctx.DivideDashBindings.TryGetValue(child, out var binding);
            var paintColor = _spec.HasColor
                ? _spec.Color
                : ResolveImplicitColor(child, edge, hasBinding ? binding!.Color : (Color?)null);

            // Reserve the same gutter as a solid divider (real width) but mask the native border color so only
            // the dashed / dotted paint shows.
            WriteEdge(child, edge, new StyleFloat(_spec.Width), new StyleColor(SilhouetteFace.SuppressedColor));

            if (hasBinding)
            {
                DivideDashPainter.Update(child, binding!, edge, _spec.Width, paintColor, _spec.Style);
            }
            else
            {
                _ctx.DivideDashBindings[child] = DivideDashPainter.Attach(child, edge, _spec.Width, paintColor, _spec.Style);
            }
        }

        // The physical edge of each divided child that carries the border. The axis is fixed by the class
        // (divide-x / divide-y); within it the divider moves to the TRAILING edge (Left→Right, Top→Bottom)
        // when EITHER the axis's own reverse marker is set OR the container's resolved direction is reversed
        // on that SAME axis — the marker and a detected row-reverse / column-reverse OR together (both mean
        // "trailing"), never XOR, so flex-row-reverse divide-x divide-x-reverse still lands trailing. The
        // flip is per-axis: a horizontal divide never reacts to column-reverse, and a vertical divide never
        // reacts to row-reverse — StyleFlexDirectionResolver returns ONE mutually-exclusive verdict per call,
        // so there is no stale leftover from a different family to react to.
        private DivideEdge ResolveEdge(VisualElement container)
        {
            var direction = StyleFlexDirectionResolver.Resolve(container, !ReferenceEquals(container, target));
            if (_spec.Axis == DivideAxis.Horizontal)
            {
                return (_spec.Reverse || direction == FlexDirection.RowReverse) ? DivideEdge.Right : DivideEdge.Left;
            }
            return (_spec.Reverse || direction == FlexDirection.ColumnReverse) ? DivideEdge.Bottom : DivideEdge.Top;
        }

        // The child's would-be border color for an implicit (no divide-{color}) dashed divider, re-resolved every
        // pass so a class / theme change moving that color after the first bind is picked up. Mirrors
        // SilhouetteFaceStash.CaptureFace's three cases: a fresh inline value (the child's own border-[…] resolver
        // write, re-applied before this manipulator runs) wins; an unset inline slot reads the USS color via
        // resolvedStyle; and the previous pass's own suppression sentinel keeps the last captured color rather than
        // reading the mask back.
        private static Color ResolveImplicitColor(VisualElement child, DivideEdge edge, Color? captured)
        {
            var inline = InlineColor(child, edge);
            if (!SilhouetteFace.IsUnset(inline) && !SilhouetteFace.IsSentinel(inline))
            {
                return inline;
            }
            if (SilhouetteFace.IsSentinel(inline) && captured.HasValue)
            {
                return captured.Value;
            }
            return ResolvedColor(child, edge);
        }

        private static Color InlineColor(VisualElement child, DivideEdge edge) => edge switch
        {
            DivideEdge.Left => child.style.borderLeftColor.value,
            DivideEdge.Right => child.style.borderRightColor.value,
            DivideEdge.Top => child.style.borderTopColor.value,
            _ => child.style.borderBottomColor.value,
        };

        private static Color ResolvedColor(VisualElement child, DivideEdge edge) => edge switch
        {
            DivideEdge.Left => child.resolvedStyle.borderLeftColor,
            DivideEdge.Right => child.resolvedStyle.borderRightColor,
            DivideEdge.Top => child.resolvedStyle.borderTopColor,
            _ => child.resolvedStyle.borderBottomColor,
        };

        private static void WriteEdge(VisualElement child, DivideEdge edge, StyleFloat width, StyleColor color)
        {
            switch (edge)
            {
                case DivideEdge.Left:
                    child.style.borderLeftWidth = width;
                    child.style.borderLeftColor = color;
                    break;
                case DivideEdge.Right:
                    child.style.borderRightWidth = width;
                    child.style.borderRightColor = color;
                    break;
                case DivideEdge.Top:
                    child.style.borderTopWidth = width;
                    child.style.borderTopColor = color;
                    break;
                case DivideEdge.Bottom:
                    child.style.borderBottomWidth = width;
                    child.style.borderBottomColor = color;
                    break;
            }
        }

        private void DetachDash(VisualElement child)
        {
            if (_ctx.DivideDashBindings.TryGetValue(child, out var binding))
            {
                DivideDashPainter.Detach(child, binding);
                _ctx.DivideDashBindings.Remove(child);
            }
        }

        // Clears every border this manipulator wrote (invoked on detach / removal).
        private void Clear()
        {
            var container = ChildContainer;
            if (container != null)
            {
                ResetStaleBordered(container);
                if (_applied.HasValue)
                {
                    ClearEdge(container, _applied.Value);
                    _applied = null;
                }
            }
            ResetAllBordered();
            _hasSignature = false;
        }

        // Unlike the Apply walk, this does NOT skip out-of-flow children — it clears the ABANDONED edge on
        // every current child unconditionally, including a PopLayout ghost mid-exit. That ghost's inline
        // position was computed by GeneralPathReconciler.PinExitingChildOutOfFlow against the box it had when
        // it was pinned, so an edge flip landing here while it is still exiting changes its border box under
        // it. The consequence is milder than the same window in the gap manipulator: the pin folds a child's
        // MARGIN into the compensated left/top it computes, so clearing a margin edge shifts the ghost by the
        // full gap, whereas a border width it never folded in only changes the ghost's own content inset by
        // the divider width. Skipping ghosts instead would be worse — a ghost that outlives the flip would
        // keep painting a rule on an edge no live sibling still uses.
        private void ClearEdge(VisualElement container, DivideEdge edge)
        {
            if (container == null)
            {
                return;
            }
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                DetachDash(child);
                ResetEdge(child, edge);
            }
        }

        // Resets the divider border on any tracked element no longer a current child, then prunes it.
        private void ResetStaleBordered(VisualElement container)
        {
            for (var i = _bordered.Count - 1; i >= 0; i--)
            {
                var child = _bordered[i];
                if (child.parent != container)
                {
                    DetachDash(child);
                    ResetOwnedEdges(child);
                    _bordered.RemoveAt(i);
                }
            }
        }

        private void ResetAllBordered()
        {
            foreach (var child in _bordered)
            {
                DetachDash(child);
                ResetOwnedEdges(child);
            }
            _bordered.Clear();
        }

        // Resets every edge this manipulator has ever written, not just the currently applied one: an element
        // reaching here has left the container (or the manipulator is being torn down), so it may still carry
        // a border from an edge an earlier flip abandoned while it was still a member. It is deliberately not
        // all four edges — this manipulator only claims the divider's own edge (an app-authored border on any
        // other edge of a divided child is preserved), and a blanket reset would erase a child's own
        // border-r/border-b on the way out.
        private void ResetOwnedEdges(VisualElement child)
        {
            for (var edge = DivideEdge.Left; edge <= DivideEdge.Bottom; edge++)
            {
                if ((_everApplied & (1 << (int)edge)) != 0)
                {
                    ResetEdge(child, edge);
                }
            }
        }

        // Resets the divider border width + color this manipulator may have written on an edge. Both channels
        // go together: the width is the gutter that participates in the box model, and the color is the
        // channel a colored or sentinel-masked divider wrote alongside it.
        private static void ResetEdge(VisualElement child, DivideEdge edge)
            => WriteEdge(child, edge, new StyleFloat(StyleKeyword.Null), new StyleColor(StyleKeyword.Null));

        // Order-sensitive hash of the inputs that change the applied borders: width, color, edge, line style,
        // and the current child identity sequence. Apply() early-returns when this matches the last
        // application. The edge term must distinguish all FOUR edges, not just the axis: Left→Right and
        // Top→Bottom are same-axis flips (a reverse marker, or the container's direction changing, with the
        // spec otherwise untouched), and a signature collision there would skip the re-apply that moves the
        // border to the new edge.
        private int ComputeSignature(VisualElement container, DivideEdge edge)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _spec.Width.GetHashCode();
                hash = hash * 31 + (_spec.HasColor ? _spec.Color.GetHashCode() : 0);
                hash = hash * 31 + (int)edge;
                hash = hash * 31 + (int)_spec.Style;
                var count = container.childCount;
                hash = hash * 31 + count;
                hash = StyleOutOfFlowChild.HashChildSequence(hash, container);
                return hash;
            }
        }
    }
}
