using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a gap spaces children along. Within an axis, StyleGapManipulator still picks the leading
    // vs. trailing physical edge (margin-left/-top vs. margin-right/-bottom) from the resolved direction
    // and any reverse marker — see StyleGapManipulator.ResolveEdge.
    internal enum GapAxis
    {
        // Plain gap-*: follow the container's resolved flex-direction.
        Auto,
        // gap-x-*/space-x-*: always horizontal, between columns.
        Horizontal,
        // gap-y-*/space-y-*: always vertical, between rows.
        Vertical,
    }

    // Framework-level CSS-gap polyfill. Unity UI Toolkit (6000.3) has no native flex
    // gap and no :first-child / :last-child USS selectors, so a child-margin
    // USS rule (.gap-* > *) cannot avoid a trailing margin on the last child and cannot
    // follow flex-direction. Velvet owns the ordered child list, so this manipulator writes the
    // inter-child leading margin (margin-left for a row, margin-top for a column) on every
    // child EXCEPT the first — spacing BETWEEN children only, matching CSS gap: no leading,
    // trailing, or outer-edge margin. A row-reverse / column-reverse container (or a
    // space-x-reverse / space-y-reverse marker) moves that same inter-child margin to the axis's
    // TRAILING physical edge (margin-right / margin-bottom) instead — native CSS gap has no leading/
    // trailing distinction at all (it spaces between children regardless of direction), so this is what
    // reproduces that direction-agnostic behavior through a physical-margin polyfill: Yoga (like CSS
    // flexbox) resolves the "leading" flex-margin for a reversed main axis to the physical trailing edge,
    // so writing margin-right there is the leading-margin equivalent, not an extra trailing gap.
    // Lifecycle mirrors the other style manipulators (StyleVariantManipulator): the
    // reconciler attaches one per gap container, keeps it in ReconcilerContext.GapManipulators,
    // and removes it on cleanup / dispose. UnregisterCallbacksFromTarget clears the
    // margins it wrote so removing the gap class (or unmounting) leaves no residue.
    // Child container. The manipulator is attached to the gap ELEMENT, but its
    // children are reconciled into FiberNodePatcher.GetChildContainer(element) — for a composite widget
    // (ScrollView, Foldout, TabView, Tab, TwoPaneSplitView) that is an inner box, not the widget itself,
    // which wraps its own chrome around it. The manipulator therefore resolves and iterates
    // that same child container (ChildContainer); the wrap path's negative margin is
    // written on the child container too, so gap lands on the reconciled content and never on the widget's
    // internal hierarchy. The direction (ResolveEdge) and wrap (IsWrap) verdicts are read from
    // that same child container for the same reason: a direction or wrap class on the widget governs the
    // WIDGET's own box, so choosing an edge or a spacing strategy from it would describe a layout the
    // spaced children are not in.
    // Re-application. The spacing depends on the child set and, for every axis, the resolved
    // direction, both of which change outside this manipulator's own events. It is re-applied
    // from three sources: (1) the reconciler calls Apply right after it reconciles the
    // container's children (the panel-independent path that also covers EditMode, where layout never
    // ticks); (2) GeometryChangedEvent catches child add / remove / reorder driven by an
    // unrelated reconcile pass at runtime; (3) AttachToPanelEvent re-resolves once resolvedStyle is
    // valid, for the one case no class can cover (see StyleFlexDirectionResolver: the five
    // direction/display classes are the PRIMARY direction source, even on a panel — resolvedStyle is only
    // the fallback).
    // A signature (_lastSignature) makes repeated Apply calls with no relevant change — notably the
    // GeometryChanged feedback the manipulator's own margin writes provoke — into no-ops.
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
    // Wrap is detected from the child container's own wrap class markers first, and from
    // resolvedStyle.flexWrap only when none of them is present — see IsWrap. The half-margin path
    // writes layout-independent margins, so it is fully resolved (and assertable) without a layout tick.
    // Direction never changes which edges wrap spaces (always symmetric), only non-wrap's edge choice.
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

        // The space-x-reverse / space-y-reverse markers (StyleGapClass.ExtractReverseMarkers). Each is an
        // ABSOLUTE per-axis instruction — "put the margin on the trailing physical edge" — that Tailwind
        // never conditions on flex-direction, so ResolveEdge OR's a marker with a detected row-reverse /
        // column-reverse rather than XOR'ing: the idiomatic flex-row-reverse space-x-4 space-x-reverse still
        // lands trailing instead of cancelling back to leading.
        // Source asymmetry (by design, not an oversight): these markers are extracted at gap-config time
        // from the same class array StyleGapClass.TryExtract reads the gap value and axis from, so a
        // variant-prefixed md:space-x-reverse resolves with the rest of the spec — the patcher switches
        // that array to the element's live class list once a variant has toggled any layout gate class onto
        // it, which is what carries the marker across a breakpoint.
        // StyleFlexDirectionResolver, by contrast, reads the live child container's classList
        // unconditionally, because unlike the gap spec itself the direction can change out from under the
        // manipulator without a matching gap-config patch. The two halves of this feature therefore reach
        // the live list by different routes on principle, not by accident — each is authoritative for what
        // it answers.
        private bool _xReverse;
        private bool _yReverse;

        // Which margins are currently written, so a later pass (axis flip, mode flip, gap removal,
        // detach) clears exactly what was applied without disturbing other margins. Leading == one
        // inter-child edge (non-wrap); HalfMargin == four-side child margins + container negative margin.
        // Right/Bottom are the trailing physical edges a row-reverse / column-reverse container (or a
        // reverse marker) resolves to instead of Left/Top.
        private enum Edge { None, Left, Top, Right, Bottom }
        private enum Mode { None, Leading, HalfMargin }

        // (mode, edge) transition table read by ApplyLeading / ApplyHalfMargin / Clear: re-running the SAME
        // strategy overwrites its own margins wholesale (no clear needed), so only a MODE or EDGE change
        // needs one of the two clear helpers first.
        //   None            -> Leading(e)      : no clear (first application).
        //   Leading(e)      -> Leading(e)       : no clear (same edge; ApplyLeading rewrites in place).
        //   Leading(e1)     -> Leading(e2), e1!=e2: ClearEdge(e1) first (Auto row<->column direction flip).
        //   HalfMargin      -> Leading(e)      : ClearHalfMargin first (wrap -> non-wrap flip).
        //   None            -> HalfMargin      : no clear (_applied is already None).
        //   Leading(e)      -> HalfMargin      : ClearEdge(e) first (non-wrap -> wrap flip).
        //   HalfMargin      -> HalfMargin      : no clear (ApplyHalfMargin rewrites in place).
        //   any             -> None (Clear())  : ClearHalfMargin when HalfMargin, else ClearEdge when
        //                                        _applied != None; ResetAllMargined then covers every
        //                                        remaining tracked element regardless of which ran.
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

        // The inner box this manipulator additionally watches for geometry changes, when the child
        // container is not the target itself. Null for a plain element. See ObserveChildContainer.
        private VisualElement? _observed;

        public StyleGapManipulator(float gap, GapAxis axis, bool xReverse, bool yReverse)
        {
            _gap = gap;
            _axis = axis;
            _xReverse = xReverse;
            _yReverse = yReverse;
        }

        // Swaps the gap value / axis / reverse markers and re-applies, clearing the old edge first if it
        // changed.
        public void UpdateGap(float gap, GapAxis axis, bool xReverse, bool yReverse)
        {
            _gap = gap;
            _axis = axis;
            _xReverse = xReverse;
            _yReverse = yReverse;
            // Force a re-apply: gap/axis/markers changed even when the child set did not, so invalidate the
            // cache.
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

        // Watches a child container that is NOT the target — a composite widget's inner box. The direction
        // and wrap verdicts are read from that box, and GeometryChangedEvent neither bubbles nor trickles,
        // so a re-layout confined to it (the widget reconfiguring itself, its own USS changing) reaches no
        // callback on the target and would leave the verdict stale with nothing left to correct it.
        // AttachToPanelEvent is deliberately not doubled: the inner box lives inside the target's own
        // subtree and attaches in the same pass, so the target's handler already re-resolves then. The
        // element is remembered rather than re-derived at teardown, so unregistration always releases
        // whatever registration was made on.
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
            // resolvedStyle.flexDirection / flexWrap only become valid on a panel; force a re-resolve.
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        // The container children are reconciled into (a composite widget's inner box; else self).
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

            var wrap = IsWrap(container);
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
            var edge = ResolveEdge(container);

            // Clear whatever the previous pass wrote when the strategy changed: a stale wrap half-margin
            // set, or the previous leading edge after an Auto row↔column direction flip or a reverse-marker
            // change (any of the four edges may be the one abandoned).
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
                // Four explicit cases (matching ClearEdge's shape below) rather than a Left/Right/Top +
                // default: ResolveEdge never returns Edge.None, but an explicit case makes that invariant
                // visible here instead of silently folding None into Bottom.
                switch (edge)
                {
                    case Edge.Left:
                        child.style.marginLeft = value;
                        break;
                    case Edge.Right:
                        child.style.marginRight = value;
                        break;
                    case Edge.Top:
                        child.style.marginTop = value;
                        break;
                    case Edge.Bottom:
                        child.style.marginBottom = value;
                        break;
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

        // Unlike ApplyLeading, this does NOT skip out-of-flow children — it clears the ABANDONED edge on
        // every current child unconditionally, including a PopLayout ghost mid-exit. That ghost's margin on
        // the OLD edge is frozen into the left/top PinExitingChildOutOfFlow already computed for it (see
        // that method), so an edge flip landing here WHILE it is still mid-exit would null the same margin
        // its pinned position assumed stays put, visibly shifting it. This window already existed for the
        // original Left<->Top axis flip (a row<->column re-render mid-exit); the reverse-driven Left<->Right
        // / Top<->Bottom flips this file adds are the same shape of risk, not a new one.
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
                switch (edge)
                {
                    case Edge.Left:
                        container[i].style.marginLeft = nullLength;
                        break;
                    case Edge.Right:
                        container[i].style.marginRight = nullLength;
                        break;
                    case Edge.Top:
                        container[i].style.marginTop = nullLength;
                        break;
                    case Edge.Bottom:
                        container[i].style.marginBottom = nullLength;
                        break;
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
        // trigger, or reconcile passes that did not touch the child set) do no work. The non-wrap bucket
        // must fold in the resolved EDGE, not just an axis bit — Left→Right or Top→Bottom is a same-axis
        // flip (a reverse marker or direction toggling without the row/column axis itself changing), and a
        // signature collision here would skip the re-apply that moves the margin to the new edge.
        private int ComputeSignature(VisualElement container, bool wrap)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _gap.GetHashCode();
                hash = hash * 31 + (wrap ? 1 : 100 + (int)ResolveEdge(container));
                var count = container.childCount;
                hash = hash * 31 + count;
                hash = StyleOutOfFlowChild.HashChildSequence(hash, container);
                return hash;
            }
        }

        // Chooses the edge from the axis and the resolved direction. GapAxis.Horizontal / Vertical fix the
        // AXIS (gap-x-*/gap-y-*/space-x-*/space-y-*); GapAxis.Auto (plain gap-*) follows the resolved axis.
        // Independently, each axis flips to its trailing edge (Left→Right, Top→Bottom) when EITHER its own
        // reverse marker is set OR the resolved direction is reversed on that SAME axis — the marker and a
        // detected row-reverse/column-reverse OR together (both mean "trailing"), never XOR, so
        // flex-row-reverse space-x-4 space-x-reverse still lands trailing. The flip is per-axis: a
        // horizontal gap never reacts to column-reverse, and a vertical gap never reacts to row-reverse —
        // StyleFlexDirectionResolver resolves ONE mutually-exclusive verdict per call, so there is no stale
        // leftover from a different family to react to.
        private Edge ResolveEdge(VisualElement container)
        {
            var direction = StyleFlexDirectionResolver.Resolve(container, !ReferenceEquals(container, target));
            switch (_axis)
            {
                case GapAxis.Horizontal:
                    return (_xReverse || direction == FlexDirection.RowReverse) ? Edge.Right : Edge.Left;
                case GapAxis.Vertical:
                    return (_yReverse || direction == FlexDirection.ColumnReverse) ? Edge.Bottom : Edge.Top;
                default:
                    return direction == FlexDirection.Row || direction == FlexDirection.RowReverse
                        ? ((_xReverse || direction == FlexDirection.RowReverse) ? Edge.Right : Edge.Left)
                        : ((_yReverse || direction == FlexDirection.ColumnReverse) ? Edge.Bottom : Edge.Top);
            }
        }

        // True when the child container wraps (selects the four-side half-margin path). Read from the child
        // container for the same reason the direction is (see StyleFlexDirectionResolver): whether the
        // spaced children wrap is a property of the box they sit in, and a wrap class on a composite widget
        // sets only the widget's own. Unlike the direction resolve this needs no separate default for a
        // widget's inner box: the final fallback here is already the engine's own (no wrap), so an inner box
        // with no wrap class off-panel answers the same as it will once a panel resolves it — with the same
        // residue the direction resolve has, an inner box whose own built-in USS sets flex-wrap, which only
        // resolvedStyle (and so only a live panel) can report. That residue bites harder here than it does
        // for direction, since wrap is the only mode that writes the container's own margin.
        // The flex-wrap / flex-nowrap / flex-wrap-reverse class markers are
        // consulted first, in _layout.uss's own declaration order (flex-wrap, flex-nowrap,
        // flex-wrap-reverse — so flex-wrap-reverse beats flex-nowrap beats flex-wrap when more than one is
        // present). UNLIKE the direction resolve, there is no further "direction class implies a default"
        // tier here: flex / flex-row(-reverse) / flex-col(-reverse) set flex-direction only — they say
        // nothing about flex-wrap — so their presence is not evidence either way. resolvedStyle.flexWrap is
        // the fallback whenever none of the three wrap classes is present, which is the catch-all for wrap
        // set some other way (a custom stylesheet rule, an inline style) — nearly every real container
        // carries a direction class, so folding direction classes into this check would take that fallback
        // away for almost all of them, misreading a genuinely wrapping inline-styled container as
        // non-wrapping. This fallback CAN read one pass stale on a same-rect toggle, the same
        // risk StyleFlexDirectionResolver has — but unlike a direction flip, gaining or losing a wrapped line usually
        // changes the container's own measured size, so the resulting GeometryChangedEvent self-corrects it
        // in the common case; see the guide for the residual fixed-size exception.
        private static bool IsWrap(VisualElement container)
        {
            if (container.ClassListContains("flex-wrap-reverse"))
            {
                return true;
            }
            if (container.ClassListContains("flex-nowrap"))
            {
                return false;
            }
            if (container.ClassListContains("flex-wrap"))
            {
                return true;
            }
            if (container.panel != null)
            {
                var wrap = container.resolvedStyle.flexWrap;
                return wrap == Wrap.Wrap || wrap == Wrap.WrapReverse;
            }
            return false;
        }
    }
}
