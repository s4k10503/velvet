using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a gap spaces children along.
    internal enum GapAxis
    {
        // Plain gap-*: follow the container's resolved flex-direction.
        Auto,
        // gap-x-*: always horizontal (margin-left between columns).
        Horizontal,
        // gap-y-*: always vertical (margin-top between rows).
        Vertical,
    }

    // Framework-level CSS-gap polyfill. Unity UI Toolkit (6000.3) has no native flex
    // gap and no :first-child / :last-child USS selectors, so a child-margin
    // USS rule (.gap-* > *) cannot avoid a trailing margin on the last child and cannot
    // follow flex-direction. Velvet owns the ordered child list, so this manipulator writes the
    // inter-child leading margin (margin-left for a row, margin-top for a column) on every
    // child EXCEPT the first — spacing BETWEEN children only, matching CSS gap: no leading,
    // trailing, or outer-edge margin.
    // Lifecycle mirrors the other style manipulators (StyleVariantManipulator): the
    // reconciler attaches one per gap container, keeps it in ReconcilerContext.GapManipulators,
    // and removes it on cleanup / dispose. UnregisterCallbacksFromTarget clears the
    // margins it wrote so removing the gap class (or unmounting) leaves no residue.
    // Child container. The manipulator is attached to the gap ELEMENT, but its
    // children are reconciled into FiberNodePatcher.GetChildContainer(element) — for a
    // ScrollView that is the contentContainer, not the ScrollView itself (which
    // wraps internal viewport / scroller elements). The manipulator therefore resolves and iterates
    // that same child container (ChildContainer); the wrap path's negative margin is
    // written on the child container too, so gap lands on the reconciled content and never on a
    // ScrollView's internal hierarchy.
    // Re-application. The spacing depends on the child set and (for GapAxis.Auto) the
    // resolved direction, both of which change outside this manipulator's own events. It is re-applied
    // from three sources: (1) the reconciler calls Apply right after it reconciles the
    // container's children (the panel-independent path that also covers EditMode, where layout never
    // ticks); (2) GeometryChangedEvent catches child add / remove / reorder driven by an
    // unrelated reconcile pass at runtime; (3) AttachToPanelEvent re-resolves
    // GapAxis.Auto once resolvedStyle.flexDirection is valid. A signature
    // (_lastSignature) makes repeated Apply calls with no relevant change
    // — notably the GeometryChanged feedback the manipulator's own margin writes provoke — into no-ops.
    // Reparent / removal. The manipulator tracks every element it wrote a margin to
    // (_margined); on each Apply / Clear any tracked element
    // that is no longer a current child has its gap margins reset first, so a child moved out of (or
    // removed from) a gap container carries no residual inline margin.
    // Out-of-flow children (position: absolute) are excluded from the index walk entirely — see
    // StyleOutOfFlowChild — matching CSS gap, which never spaces a child that has been taken out of
    // flow. This is not a PopLayout-only carve-out: any app-authored .absolute child under a gap
    // container was already exempt from occupying a flex slot, so it must not consume or shift a gap
    // margin either. It is also what lets AnimatePresenceMode.PopLayout deliver its purpose — a
    // GeneralPathReconciler.PinExitingChildOutOfFlow ghost must stop being counted the instant it is
    // pinned so its still-present siblings reflow into its slot immediately, and the ghost's own frozen
    // margin (folded into its pinned left/top) is left untouched rather than being reset or reassigned.
    // Wrap (flex-wrap) hybrid. CSS gap under wrapping spaces BOTH axes
    // (between items in a line AND between wrapped lines), but a single leading-edge margin can only
    // space the main axis. So this manipulator switches strategy by container mode:
    // Non-wrap (the common case): the exact leading-margin behavior described above —
    // leading margin on all-but-first child, no container margin, no outer bleed.
    // Wrap: the classic wrap-compatible half-margin polyfill — gap/2 on ALL FOUR
    // sides of EVERY child and -gap/2 on all four sides of the CHILD CONTAINER. Adjacent items
    // (in either axis, including across wrapped lines) are then separated by gap/2 + gap/2 == gap,
    // and the container's negative margin pulls content flush to its edge.
    // Wrap is detected on-panel via resolvedStyle.flexWrap (Wrap / WrapReverse) and
    // off-panel (EditMode) via the flex-wrap class marker. The half-margin path writes
    // layout-independent margins, so it is fully resolved (and assertable) without a layout tick.
    // Residual gaps versus native CSS gap (documented, not solved):
    // An explicit per-child margin on the SAME logical edge as the gap (e.g. ml-2 on a
    // child under a gap-x-4 row) is OVERWRITTEN — this manipulator owns the margin edge(s) it
    // spaces along and writes the gap value there each pass. A margin-based polyfill cannot both BE the
    // gap and preserve an explicit margin on the same edge without per-child base-margin tracking, which
    // would be fragile against re-apply; only native UITK gap composes the two. Use padding, an
    // inner wrapper, or a different axis when a child needs its own margin on the gap edge. Margins on a
    // DIFFERENT edge than the gap (e.g. mt-2 on a child under a NON-wrap gap-x-4 row) are
    // preserved; under the wrap half-margin path all four edges belong to the gap, so any explicit child
    // margin is overwritten on every side.
    // The non-wrap path forces the FIRST child's leading-edge margin to Null, so an explicit
    // per-first-child margin on the gap edge (e.g. ml-2 on the first child of a gap-x-4
    // row) is ERASED. The first child must have no leading gap to match CSS gap (no outer-edge
    // spacing), and the manipulator cannot tell an intentional first-child margin from a stale gap
    // value it wrote on a previous pass, so it always resets it. Use container padding for a leading
    // inset.
    // The wrap half-margin path writes the CHILD CONTAINER's own four margins (-gap/2),
    // so an explicit container margin (e.g. m-4 on the same element) is OVERWRITTEN while gap is
    // active, and Clear resets the container margin to Null — the user's container margin
    // is LOST (not restored) for as long as a wrapping gap is applied. Non-wrap containers never touch
    // the container's own margin. Use an outer wrapper for a margin on a wrapping gap container.
    // The wrap half-margin path's container negative margin (-gap/2 on all four sides)
    // bleeds gap/2 OUTWARD, overlapping the container's own siblings or its parent's padding by
    // gap/2. This is inherent to every pre-native-gap wrap polyfill; only native UITK gap
    // avoids it. Non-wrap containers never bleed (they write no container margin).
    internal sealed class StyleGapManipulator : Manipulator
    {
        private float _gap;
        private GapAxis _axis;

        // Which margins are currently written, so a later pass (axis flip, mode flip, gap removal,
        // detach) clears exactly what was applied without disturbing other margins. Leading == one
        // inter-child edge (non-wrap); HalfMargin == four-side child margins + container negative margin.
        private enum Edge { None, Left, Top }
        private enum Mode { None, Leading, HalfMargin }
        private Edge _applied = Edge.None;
        private Mode _mode = Mode.None;

        // Every element this manipulator has written a gap margin to. On each Apply / Clear, any tracked
        // element that is no longer a current child of the child container has its margins reset, so a
        // child reparented or removed out of the gap container does not keep its inline gap margin.
        private readonly List<VisualElement> _margined = new();

        // Signature of the last successful Apply: gap, mode, edge, and the current child identity set.
        // Apply() early-returns when this is unchanged, so the GeometryChanged churn the margin writes
        // themselves provoke (and repeated reconcile passes that do not touch the child set) are no-ops.
        private int _lastSignature;
        private bool _hasSignature;

        public StyleGapManipulator(float gap, GapAxis axis)
        {
            _gap = gap;
            _axis = axis;
        }

        // Swaps the gap value / axis and re-applies, clearing the old edge first if it changed.
        public void UpdateGap(float gap, GapAxis axis)
        {
            _gap = gap;
            _axis = axis;
            // Force a re-apply: gap/axis changed even when the child set did not, so invalidate the cache.
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
            // resolvedStyle.flexDirection / flexWrap only become valid on a panel; force a re-resolve.
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        // The container children are reconciled into (a ScrollView's contentContainer; else self).
        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Spaces the children for the current container mode. Non-wrap writes the leading inter-child
        // margin on every child except the first (spacing strictly BETWEEN children, no container
        // margin); wrap writes the four-side half-margin polyfill on children and a four-side negative
        // half-margin on the container so both axes are spaced. Any margins written under the previous
        // mode / edge are cleared first so a mode flip or axis flip leaves no residue. Early-returns when
        // nothing relevant (gap, axis, mode, child set) changed since the last successful application.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            var wrap = IsWrap();
            var signature = ComputeSignature(container, wrap);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Must run before _margined.Clear() (inside ApplyHalfMargin / ApplyLeading below): it reads the
            // pre-clear _margined list to find children that left the container.
            ResetStaleMargined(container);

            if (wrap)
            {
                ApplyHalfMargin(container);
            }
            else
            {
                ApplyLeading(container);
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        private void ApplyLeading(VisualElement container)
        {
            var edge = ResolveEdge();

            // Clear whatever the previous pass wrote when the strategy changed: a stale wrap half-margin
            // set, or the opposite leading edge after an Auto row↔column direction flip.
            if (_mode == Mode.HalfMargin)
            {
                ClearHalfMargin(container);
            }
            else if (_applied != Edge.None && _applied != edge)
            {
                ClearEdge(container, _applied);
            }
            _applied = edge;
            _mode = Mode.Leading;
            _margined.Clear();

            var count = container.childCount;
            var logicalIndex = 0;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // Out-of-flow children (a PopLayout-pinned exiting ghost, or an app-authored .absolute
                // child) hold no slot in the flex line — see StyleOutOfFlowChild. Skip them entirely rather
                // than resetting their margin: a pinned ghost's own margin is frozen into the compensated
                // left/top PinExitingChildOutOfFlow already computed for it, and touching it here would
                // reintroduce the same double-application it was pinned to avoid.
                if (StyleOutOfFlowChild.IsOutOfFlow(child))
                {
                    continue;
                }
                var value = logicalIndex == 0 ? new StyleLength(StyleKeyword.Null) : new StyleLength(_gap);
                if (edge == Edge.Left)
                {
                    child.style.marginLeft = value;
                }
                else
                {
                    child.style.marginTop = value;
                }
                _margined.Add(child);
                logicalIndex++;
            }
        }

        // Wrap-compatible polyfill: gap/2 on all four sides of every child and -gap/2 on
        // all four sides of the child container. Adjacent items (any axis, including across wrapped lines)
        // are separated by two half-margins == gap; the container's negative margin cancels the
        // children's outer-edge half-margins so content stays flush to the container edge. Margins are
        // layout-independent, so this resolves fully without a layout tick.
        private void ApplyHalfMargin(VisualElement container)
        {
            // A non-wrap→wrap flip leaves a single leading edge behind; clear it before switching.
            if (_mode == Mode.Leading && _applied != Edge.None)
            {
                ClearEdge(container, _applied);
                _applied = Edge.None;
            }
            _mode = Mode.HalfMargin;
            _margined.Clear();

            var half = new StyleLength(_gap / 2f);
            var negHalf = new StyleLength(-_gap / 2f);

            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                // See ApplyLeading: an out-of-flow child takes no line slot, so it gets no half-margin
                // either — the wrap polyfill only spaces children that actually wrap.
                if (StyleOutOfFlowChild.IsOutOfFlow(child))
                {
                    continue;
                }
                child.style.marginLeft = half;
                child.style.marginRight = half;
                child.style.marginTop = half;
                child.style.marginBottom = half;
                _margined.Add(child);
            }

            container.style.marginLeft = negHalf;
            container.style.marginRight = negHalf;
            container.style.marginTop = negHalf;
            container.style.marginBottom = negHalf;
        }

        // Clears every margin this manipulator wrote (invoked on detach / removal / mode flip).
        private void Clear()
        {
            var container = ChildContainer;
            if (container != null)
            {
                ResetStaleMargined(container);
                if (_mode == Mode.HalfMargin)
                {
                    ClearHalfMargin(container);
                }
                else if (_applied != Edge.None)
                {
                    ClearEdge(container, _applied);
                    _applied = Edge.None;
                }
            }
            ResetAllMargined();
            _mode = Mode.None;
            _hasSignature = false;
        }

        private void ClearEdge(VisualElement container, Edge edge)
        {
            if (container == null)
            {
                return;
            }
            var nullLength = new StyleLength(StyleKeyword.Null);
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                if (edge == Edge.Left)
                {
                    container[i].style.marginLeft = nullLength;
                }
                else if (edge == Edge.Top)
                {
                    container[i].style.marginTop = nullLength;
                }
            }
        }

        // Clears the four-side child margins and the container's negative margin written by the wrap path.
        private void ClearHalfMargin(VisualElement container)
        {
            if (container == null)
            {
                return;
            }
            var nullLength = new StyleLength(StyleKeyword.Null);
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                child.style.marginLeft = nullLength;
                child.style.marginRight = nullLength;
                child.style.marginTop = nullLength;
                child.style.marginBottom = nullLength;
            }
            container.style.marginLeft = nullLength;
            container.style.marginRight = nullLength;
            container.style.marginTop = nullLength;
            container.style.marginBottom = nullLength;
            _applied = Edge.None;
        }

        // Resets the gap margins on any tracked element that is no longer a current child of
        // container (reparented or removed), then prunes it from the tracking list.
        // Resets ALL margins this manipulator may have written (both modes' edges) since the element has
        // left the container and the manipulator no longer owns its layout.
        private void ResetStaleMargined(VisualElement container)
        {
            for (var i = _margined.Count - 1; i >= 0; i--)
            {
                var child = _margined[i];
                if (child.parent != container)
                {
                    ResetGapMargins(child);
                    _margined.RemoveAt(i);
                }
            }
        }

        // Resets the gap margins on every tracked element (used on Clear / detach).
        private void ResetAllMargined()
        {
            foreach (var child in _margined)
            {
                ResetGapMargins(child);
            }
            _margined.Clear();
        }

        // Resets the inline margin edges this manipulator writes (leading edge + all four half-margin sides).
        private static void ResetGapMargins(VisualElement child)
        {
            var nullLength = new StyleLength(StyleKeyword.Null);
            child.style.marginLeft = nullLength;
            child.style.marginRight = nullLength;
            child.style.marginTop = nullLength;
            child.style.marginBottom = nullLength;
        }

        // A cheap order-sensitive hash of the inputs that change the applied margins: gap value, mode,
        // resolved edge, and the current child identity sequence. Apply() early-returns when this matches
        // the last application, so redundant re-applies (the GeometryChanged feedback its own writes
        // trigger, or reconcile passes that did not touch the child set) do no work.
        private int ComputeSignature(VisualElement container, bool wrap)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _gap.GetHashCode();
                hash = hash * 31 + (wrap ? 1 : ResolveEdge() == Edge.Left ? 2 : 3);
                var count = container.childCount;
                hash = hash * 31 + count;
                for (var i = 0; i < count; i++)
                {
                    var child = container[i];
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child);
                    // A child's in-flow / out-of-flow transition (a PopLayout pin or its cancel) changes
                    // which children the margin walk counts even though neither its identity nor the
                    // container's total count changed, so it must also flip the signature.
                    hash = hash * 31 + (StyleOutOfFlowChild.IsOutOfFlow(child) ? 1 : 0);
                }
                return hash;
            }
        }

        // Chooses the leading edge from the axis. GapAxis.Auto follows the resolved
        // flex-direction when the element is on a panel; off-panel (EditMode, pre-attach) it
        // falls back to the flex-row / flex-col class markers, defaulting to row (the .flex
        // default direction) when neither is present.
        private Edge ResolveEdge()
        {
            switch (_axis)
            {
                case GapAxis.Horizontal:
                    return Edge.Left;
                case GapAxis.Vertical:
                    return Edge.Top;
                default:
                    return IsRow() ? Edge.Left : Edge.Top;
            }
        }

        private bool IsRow()
        {
            if (target.panel != null)
            {
                var dir = target.resolvedStyle.flexDirection;
                return dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
            }
            // Off-panel: resolvedStyle is not yet meaningful. Mirror the .flex=row default:
            // plain gap (Auto axis) resolves to a ROW unless flex-col explicitly forces column.
            // flex-col still wins; flex-row is the same as the default here.
            if (target.ClassListContains("flex-col"))
            {
                return false;
            }
            return true;
        }

        // True when the container wraps (selects the four-side half-margin path). On a panel this reads
        // resolvedStyle.flexWrap (Wrap or WrapReverse); off-panel (EditMode) it falls
        // back to the flex-wrap class marker, mirroring IsRow's off-panel idiom.
        private bool IsWrap()
        {
            if (target.panel != null)
            {
                var wrap = target.resolvedStyle.flexWrap;
                return wrap == Wrap.Wrap || wrap == Wrap.WrapReverse;
            }
            return target.ClassListContains("flex-wrap");
        }
    }
}
