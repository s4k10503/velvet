using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Framework-level divide-* polyfill. The divide-x / divide-y utilities draw a border between adjacent
    // children (`& > * + *`): a left border on every child but the first for divide-x, a top border for
    // divide-y. UI Toolkit (6000.3) has no :first-child / :first-of-type and no `> *` child combinator,
    // so a USS rule cannot express "border on all children except the first." Velvet owns the ordered
    // child list, so this manipulator writes the inter-child LEADING border (border-left for an x divide,
    // border-top for a y divide) on every child EXCEPT the first — a divider strictly BETWEEN children,
    // no leading or trailing edge.
    //
    // Lifecycle mirrors StyleGapManipulator: the reconciler attaches one per divide container, keeps it
    // in ReconcilerContext.DivideManipulators, and removes it on cleanup / dispose.
    // UnregisterCallbacksFromTarget clears the borders it wrote so removing the divide class (or
    // unmounting) leaves no residue. Re-application has the same three sources as the gap manipulator:
    // the reconciler's post-child-reconcile call (the panel-independent path that also covers EditMode),
    // GeometryChangedEvent (child add / remove / reorder from an unrelated reconcile), and
    // AttachToPanelEvent. A signature makes a redundant Apply (notably the GeometryChanged feedback its
    // own writes provoke) a no-op.
    //
    // Line style: divide-solid is a plain inline border. divide-dashed / divide-dotted have no UI Toolkit
    // border-style, so the manipulator still reserves the SAME gutter (the real width) but masks the color
    // with the sentinel and hands each divided child a DivideDashChildBinding (DivideDashPainter) that paints
    // the dashed / dotted stroke on the child's own generateVisualContent — so switching between solid and
    // dashed is layout-identical and only the paint differs.
    //
    // Child container. Like the gap manipulator it resolves and iterates
    // FiberNodePatcher.GetChildContainer(target) (a ScrollView's contentContainer; else self), so the
    // divider lands on the reconciled content and never on a ScrollView's internal hierarchy.
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

        // Which edge is currently written, so an axis flip clears the old edge before writing the new one.
        private enum Edge { None, Left, Top }
        private Edge _applied = Edge.None;

        // Every child this manipulator has written a divider border to. On each Apply / Clear any tracked
        // element no longer a current child has its divider border reset, so a child reparented or removed
        // out of the divide container keeps no residual inline border.
        private readonly List<VisualElement> _bordered = new();

        private int _lastSignature;
        private bool _hasSignature;

        public StyleDivideManipulator(DivideSpec spec, ReconcilerContext ctx)
        {
            _spec = spec;
            _ctx = ctx;
        }

        // Swaps the spec and re-applies, clearing the old edge first if the axis changed.
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

        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Writes the inter-child border for the current spec: the leading edge (left for x, top for y) on
        // every child except the first. Clears the opposite edge first on an axis flip. Early-returns when
        // nothing relevant (spec, edge, child set) changed since the last successful application.
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

            // Must run before _bordered.Clear() below: it reads the pre-clear _bordered list to find children
            // that left the container.
            ResetStaleBordered(container);

            var edge = _spec.Axis == DivideAxis.Horizontal ? Edge.Left : Edge.Top;

            // An axis flip (divide-x ↔ divide-y) leaves the old edge behind; clear it before switching.
            if (_applied != Edge.None && _applied != edge)
            {
                ClearEdge(container, _applied);
            }
            _applied = edge;
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
                // The first child has no leading divider (the `> * + *` rule starts at the second child).
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
        private void ApplyToChild(VisualElement child, Edge edge, bool isDivider)
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

            var axis = edge == Edge.Left ? DivideAxis.Horizontal : DivideAxis.Vertical;
            if (hasBinding)
            {
                DivideDashPainter.Update(child, binding!, axis, _spec.Width, paintColor, _spec.Style);
            }
            else
            {
                _ctx.DivideDashBindings[child] = DivideDashPainter.Attach(child, axis, _spec.Width, paintColor, _spec.Style);
            }
        }

        // The child's would-be border color for an implicit (no divide-{color}) dashed divider, re-resolved every
        // pass so a class / theme change moving that color after the first bind is picked up. Mirrors
        // SilhouetteFaceStash.CaptureFace's three cases: a fresh inline value (the child's own border-[…] resolver
        // write, re-applied before this manipulator runs) wins; an unset inline slot reads the USS color via
        // resolvedStyle; and the previous pass's own suppression sentinel keeps the last captured color rather than
        // reading the mask back.
        private static Color ResolveImplicitColor(VisualElement child, Edge edge, Color? captured)
        {
            var inline = edge == Edge.Left ? child.style.borderLeftColor.value : child.style.borderTopColor.value;
            if (!SilhouetteFace.IsUnset(inline) && !SilhouetteFace.IsSentinel(inline))
            {
                return inline;
            }
            if (SilhouetteFace.IsSentinel(inline) && captured.HasValue)
            {
                return captured.Value;
            }
            return edge == Edge.Left ? child.resolvedStyle.borderLeftColor : child.resolvedStyle.borderTopColor;
        }

        private static void WriteEdge(VisualElement child, Edge edge, StyleFloat width, StyleColor color)
        {
            if (edge == Edge.Left)
            {
                child.style.borderLeftWidth = width;
                child.style.borderLeftColor = color;
            }
            else
            {
                child.style.borderTopWidth = width;
                child.style.borderTopColor = color;
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
                if (_applied != Edge.None)
                {
                    ClearEdge(container, _applied);
                    _applied = Edge.None;
                }
            }
            ResetAllBordered();
            _hasSignature = false;
        }

        private void ClearEdge(VisualElement container, Edge edge)
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
                    ResetEdge(child, Edge.Left);
                    ResetEdge(child, Edge.Top);
                    _bordered.RemoveAt(i);
                }
            }
        }

        private void ResetAllBordered()
        {
            foreach (var child in _bordered)
            {
                DetachDash(child);
                ResetEdge(child, Edge.Left);
                ResetEdge(child, Edge.Top);
            }
            _bordered.Clear();
        }

        // Resets the divider border width + color this manipulator may have written on an edge.
        private static void ResetEdge(VisualElement child, Edge edge)
        {
            if (edge == Edge.Left)
            {
                child.style.borderLeftWidth = new StyleFloat(StyleKeyword.Null);
                child.style.borderLeftColor = new StyleColor(StyleKeyword.Null);
            }
            else if (edge == Edge.Top)
            {
                child.style.borderTopWidth = new StyleFloat(StyleKeyword.Null);
                child.style.borderTopColor = new StyleColor(StyleKeyword.Null);
            }
        }

        // Order-sensitive hash of the inputs that change the applied borders: width, color, edge, line style,
        // and the current child identity sequence. Apply() early-returns when this matches the last application.
        private int ComputeSignature(VisualElement container)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _spec.Width.GetHashCode();
                hash = hash * 31 + (_spec.HasColor ? _spec.Color.GetHashCode() : 0);
                hash = hash * 31 + (_spec.Axis == DivideAxis.Horizontal ? 1 : 2);
                hash = hash * 31 + (int)_spec.Style;
                var count = container.childCount;
                hash = hash * 31 + count;
                for (var i = 0; i < count; i++)
                {
                    var child = container[i];
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child);
                    // A child's in-flow / out-of-flow transition changes which children the divider walk
                    // counts even though neither its identity nor the container's total count changed.
                    hash = hash * 31 + (StyleOutOfFlowChild.IsOutOfFlow(child) ? 1 : 0);
                }
                return hash;
            }
        }
    }
}
