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
    // Child container. Like the gap manipulator it resolves and iterates
    // FiberNodePatcher.GetChildContainer(target) (a ScrollView's contentContainer; else self), so the
    // divider lands on the reconciled content and never on a ScrollView's internal hierarchy.
    //
    // Limitations: UI Toolkit has no border-style, so only solid dividers exist
    // (divide-dashed / divide-dotted are unsupported, handled by StyleDivideClass). An explicit per-child
    // border on the SAME edge the divider draws on (e.g. border-l on a child of a divide-x row) is
    // OVERWRITTEN — this manipulator owns that edge, exactly as the gap manipulator owns its margin edge.
    internal sealed class StyleDivideManipulator : Manipulator
    {
        private DivideSpec _spec;

        // Which edge is currently written, so an axis flip clears the old edge before writing the new one.
        private enum Edge { None, Left, Top }
        private Edge _applied = Edge.None;

        // Every child this manipulator has written a divider border to. On each Apply / Clear any tracked
        // element no longer a current child has its divider border reset, so a child reparented or removed
        // out of the divide container keeps no residual inline border.
        private readonly List<VisualElement> _bordered = new();

        private int _lastSignature;
        private bool _hasSignature;

        public StyleDivideManipulator(DivideSpec spec)
        {
            _spec = spec;
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

            // Reset any previously-bordered element that is no longer a current child before re-applying.
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
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // The first child has no leading divider (the `> * + *` rule starts at the second child).
                var isDivider = i != 0;
                var width = isDivider ? new StyleFloat(_spec.Width) : new StyleFloat(StyleKeyword.Null);
                // Own the edge's color channel on EVERY pass (like the gap manipulator owns its margin):
                // write the divider color only on a colored divider, else reset to Null. Without the reset a
                // dropped divide-{color} class (axis kept) or a colored child reordered to the first slot
                // would keep a stale inline color; Null falls the edge back to the element's default border color.
                var color = isDivider && _spec.HasColor ? new StyleColor(_spec.Color) : new StyleColor(StyleKeyword.Null);
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
                _bordered.Add(child);
            }

            _lastSignature = signature;
            _hasSignature = true;
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
                ResetEdge(container[i], edge);
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

        // Order-sensitive hash of the inputs that change the applied borders: width, color, edge, and the
        // current child identity sequence. Apply() early-returns when this matches the last application.
        private int ComputeSignature(VisualElement container)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _spec.Width.GetHashCode();
                hash = hash * 31 + (_spec.HasColor ? _spec.Color.GetHashCode() : 0);
                hash = hash * 31 + (_spec.Axis == DivideAxis.Horizontal ? 1 : 2);
                var count = container.childCount;
                hash = hash * 31 + count;
                for (var i = 0; i < count; i++)
                {
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(container[i]);
                }
                return hash;
            }
        }
    }
}
