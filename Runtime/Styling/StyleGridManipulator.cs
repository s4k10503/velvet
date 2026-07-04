using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A resolved grid spec: the column count plus the row / column gaps the grid owns.
    internal readonly struct GridSpec
    {
        public readonly int Columns;
        public readonly float ColumnGap;
        public readonly float RowGap;

        public GridSpec(int columns, float columnGap, float rowGap)
        {
            Columns = columns;
            ColumnGap = columnGap;
            RowGap = rowGap;
        }
    }

    // Framework-level CSS-Grid polyfill. UI Toolkit (6000.3) has no `display: grid` — its layout is a Flexbox
    // subset — so Velvet realizes `grid` as a flex-wrap row (_layout.uss) and this manipulator sizes the
    // children into N equal columns: each direct child gets width = (rowWidth - (N-1)*columnGap) / N, the
    // column gap as a leading margin-left on every child but the first OF ITS ROW, and the row gap as a
    // leading margin-top on every row but the first. The children then wrap into aligned rows of N; a final
    // short row left-packs under the leading columns, matching CSS grid auto-placement.
    //
    // The column width depends on the resolved row width, so Apply reads contentRect.width and re-runs from
    // GeometryChangedEvent on resize. A signature keyed on that width makes the height-change feedback its own
    // wrapping provokes a no-op. Off-panel (no resolved width yet) it writes only the gap margins and defers
    // the widths until a real width resolves on attach. The width is reduced by a sub-pixel safety margin so
    // floating-point division never overflows the row and forces a spurious wrap.
    //
    // The grid OWNS its children's width + all four margins (like the gap manipulator owns its margin edge),
    // so a grid container routes its gap-* through this manipulator and StyleGapManipulator is suppressed when
    // grid-cols-* is present — a single owner avoids a double-write race. A per-child width / margin utility on
    // a grid child is overwritten: the column owns the box.
    //
    // Lifecycle mirrors StyleGapManipulator / StyleDivideManipulator: the reconciler attaches one per grid
    // container, keeps it in ReconcilerContext.GridManipulators, and removes it on cleanup / dispose.
    // UnregisterCallbacksFromTarget clears the widths + margins it wrote so removing the grid class (or
    // unmounting) leaves no residue. Like the gap manipulator it iterates FiberNodePatcher.GetChildContainer,
    // so the sizing lands on the reconciled content and never on a ScrollView's internal hierarchy.
    internal sealed class StyleGridManipulator : Manipulator
    {
        // A sub-pixel shave off each column so that N*colWidth + (N-1)*gap never exceeds the row width through
        // float-division rounding (which would overflow the line and wrap N columns down to N-1).
        private const float WrapSafetyPx = 0.5f;

        private GridSpec _spec;

        // Every child this manipulator has sized, so a child reparented or removed out of the grid has its
        // inline width + margins reset and keeps no residue.
        private readonly List<VisualElement> _sized = new();

        private int _lastSignature;
        private bool _hasSignature;

        public StyleGridManipulator(GridSpec spec)
        {
            _spec = spec;
        }

        // Swaps the spec (column count / gaps changed) and re-applies.
        public void UpdateSpec(GridSpec spec)
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

        // Sizes the children into N equal columns and writes the row / column gap margins. Early-returns when
        // nothing relevant (spec, resolved row width, child set) changed since the last application.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            // Resolved content width of the row; 0 / NaN off-panel before the first layout.
            var width = container.contentRect.width;
            var hasWidth = width > 0f && !float.IsNaN(width);

            var signature = ComputeSignature(container, width);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            ResetStaleSized(container);
            _sized.Clear();

            var n = _spec.Columns < 1 ? 1 : _spec.Columns;
            var colWidth = hasWidth
                ? Mathf.Max(0f, (width - (n - 1) * _spec.ColumnGap) / n - WrapSafetyPx)
                : 0f;

            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                var col = i % n;
                var row = i / n;
                // The column owns the box: width + all four margins. Width is deferred (Null) until a real
                // row width resolves on panel.
                child.style.width = hasWidth ? new StyleLength(colWidth) : new StyleLength(StyleKeyword.Null);
                child.style.marginLeft = new StyleLength(col == 0 ? 0f : _spec.ColumnGap);
                child.style.marginTop = new StyleLength(row == 0 ? 0f : _spec.RowGap);
                child.style.marginRight = new StyleLength(0f);
                child.style.marginBottom = new StyleLength(0f);
                _sized.Add(child);
            }

            // Only lock the signature once a real width resolved, so the deferred (off-panel) pass re-runs
            // when contentRect.width becomes available on attach / first layout.
            if (hasWidth)
            {
                _lastSignature = signature;
                _hasSignature = true;
            }
            else
            {
                _hasSignature = false;
            }
        }

        // Clears every width + margin this manipulator wrote (invoked on detach / removal).
        private void Clear()
        {
            var container = ChildContainer;
            if (container != null)
            {
                ResetStaleSized(container);
            }
            ResetAllSized();
            _hasSignature = false;
        }

        // Resets the sizing on any tracked element no longer a current child, then prunes it.
        private void ResetStaleSized(VisualElement container)
        {
            for (var i = _sized.Count - 1; i >= 0; i--)
            {
                var child = _sized[i];
                if (child.parent != container)
                {
                    ResetChild(child);
                    _sized.RemoveAt(i);
                }
            }
        }

        private void ResetAllSized()
        {
            foreach (var child in _sized)
            {
                ResetChild(child);
            }
            _sized.Clear();
        }

        // Resets the width + four margins this manipulator may have written, falling each back to its
        // class / default value.
        private static void ResetChild(VisualElement child)
        {
            child.style.width = new StyleLength(StyleKeyword.Null);
            child.style.marginLeft = new StyleLength(StyleKeyword.Null);
            child.style.marginTop = new StyleLength(StyleKeyword.Null);
            child.style.marginRight = new StyleLength(StyleKeyword.Null);
            child.style.marginBottom = new StyleLength(StyleKeyword.Null);
        }

        // Order-sensitive hash of the inputs that change the sizing: columns, gaps, resolved row width
        // (rounded to skip float jitter), and the current child identity sequence.
        private int ComputeSignature(VisualElement container, float width)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _spec.Columns;
                hash = hash * 31 + _spec.ColumnGap.GetHashCode();
                hash = hash * 31 + _spec.RowGap.GetHashCode();
                hash = hash * 31 + Mathf.RoundToInt(width);
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
