using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a gap spaces children along. Within an axis, StyleGapManipulator still picks the leading
    // vs. trailing physical edge (margin-left/-top vs. margin-right/-bottom) — see
    // StyleGapManipulator.ResolveEdge.
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
    // trailing, or outer-edge margin. A row-reverse / column-reverse container moves that same
    // inter-child margin to the axis's TRAILING physical edge (margin-right / margin-bottom) instead —
    // native CSS gap has no leading/
    // trailing distinction at all (it spaces between children regardless of direction), so this is what
    // reproduces that direction-agnostic behavior through a physical-margin polyfill: Yoga (like CSS
    // flexbox) resolves the "leading" flex-margin for a reversed main axis to the physical trailing edge,
    // so writing margin-right there is the leading-margin equivalent, not an extra trailing gap.
    // Lifecycle mirrors the other style manipulators (StyleVariantManipulator): the
    // reconciler attaches one per gap container, keeps it in ReconcilerContext.GapManipulators,
    // and removes it on cleanup / dispose. UnregisterCallbacksFromTarget clears what it still owns —
    // the margins on its current children, and on a departed one only while
    // ReconcilerContext.ChildBoxOwners still names this manipulator.
    // Child container. The manipulator is attached to the gap ELEMENT, but its children are reconciled
    // into FiberNodePatcher.GetChildContainer(element) — a composite widget's inner box, not the widget.
    // Everything naming a container names that one: the iteration, the wrap path's negative margin, and
    // the direction / wrap verdicts (ResolveEdge, IsWrap). A direction or wrap class on the widget governs
    // the WIDGET's box, which is not the box the spaced children are in.
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
    // (_margined); on each Apply / Clear any tracked element that is no longer a current child is offered
    // for release first, so a child moved out of (or removed from) a gap container carries no margin this
    // container still owns. What it owns is ReconcilerContext.ChildBoxOwners' answer rather than the
    // tracked list's: a child re-rented by another gap or grid container keeps what that one wrote.
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
    // Wrap is detected from the child container — see IsWrap. The half-margin path writes
    // layout-independent margins, so it is fully resolved (and assertable) without a layout tick.
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
    // The wrap half-margin path writes the CHILD CONTAINER's own four margins (-gap/2),
    // so an explicit container margin (e.g. m-4 on the same element) is OVERWRITTEN while a wrapping
    // gap is active. Non-wrap containers never touch the container's own margin. Use an outer wrapper
    // for a margin on a wrapping gap container.
    // The wrap half-margin path's container negative margin (-gap/2 on all four sides)
    // bleeds gap/2 OUTWARD, overlapping the container's own siblings or its parent's padding by
    // gap/2. This is inherent to every pre-native-gap wrap polyfill; only native UITK gap
    // avoids it. Non-wrap containers never bleed (they write no container margin).
    internal sealed class StyleGapManipulator : Manipulator
    {
        private readonly ReconcilerContext _ctx;

        // Source asymmetry (by design, not an oversight): the reverse markers are extracted at gap-config time
        // from the same class array StyleGapClass.TryExtract reads the gap value and axis from, so a
        // variant-prefixed md:space-x-reverse resolves with the rest of the spec — the patcher switches
        // that array to the element's live class list once a variant has toggled any layout gate class onto
        // it, which is what carries the marker across a breakpoint.
        // StyleFlexDirectionResolver, by contrast, reads the live child container's classList
        // unconditionally: unlike the gap spec, the direction can change with no matching gap-config patch.
        private GapSpec _spec;

        // Which margins are currently written, so a later pass (axis flip, mode flip, gap removal,
        // detach) clears exactly what was applied without disturbing other margins. Leading == one
        // inter-child edge (non-wrap); HalfMargin == four-side child margins + container negative margin.
        // Right/Bottom are the trailing physical edges ResolveEdge can choose instead of Left/Top.
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
        // element that is no longer a current child of the child container is offered for release; whether
        // it loses the margin is the claim's answer, not this list's — see
        // ReconcilerContext.ChildBoxOwners.
        private readonly List<VisualElement> _margined = new();

        // Signature of the last successful Apply: gap, mode, edge, and the current child identity set.
        // Apply() early-returns when this is unchanged, so the GeometryChanged churn the margin writes
        // themselves provoke (and repeated reconcile passes that do not touch the child set) are no-ops.
        private int _lastSignature;
        private bool _hasSignature;

        // Whether the last IsWrap verdict came from a flex-wrap / flex-nowrap / flex-wrap-reverse class, and
        // whether one has left since the last GeometryChangedEvent — see IsWrap.
        private bool _wrapFromMarker;
        private bool _wrapMarkerLeft;

        // Null unless the child container is a separate element — see ObserveChildContainer.
        private VisualElement? _observed;

        public StyleGapManipulator(ReconcilerContext ctx, GapSpec spec)
        {
            _ctx = ctx;
            _spec = spec;
        }

        // Swaps the spec and re-applies, clearing the old edge first if it changed.
        public void UpdateGap(GapSpec spec)
        {
            _spec = spec;
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

        // GeometryChangedEvent neither bubbles nor trickles, so a re-layout confined to a composite widget's
        // inner box — the box the verdicts come from — reaches nothing registered on the target.
        // AttachToPanelEvent is not doubled: the inner box attaches in the target's own subtree pass.
        // The element is remembered, not re-derived, so teardown releases what registration took.
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

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _wrapMarkerLeft = false;
            Apply();
        }

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

            // Tailwind's space-* writes one margin edge per child, which spaces one axis however the container
            // wraps; only CSS gap spaces the wrapped lines too.
            var wrap = !_spec.Space && IsWrap(container);
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
                // The first child takes no gap, so its edge goes back to whatever the child's own layers say.
                if (logicalIndex == 0)
                {
                    StyleArbitraryValueResolver.HandBack(child, SlotOf(edge));
                }
                else
                {
                    StyleArbitraryValueResolver.Hold(child, SlotOf(edge), new StyleLength(_spec.Gap));
                }
                StyleChildOwnership.Claim(_ctx.ChildBoxOwners, child, this);
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

            var half = new StyleLength(_spec.Gap / 2f);
            var negHalf = new StyleLength(-_spec.Gap / 2f);

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
                HoldMargins(child, half);
                StyleChildOwnership.Claim(_ctx.ChildBoxOwners, child, this);
                _margined.Add(child);
            }

            HoldMargins(ClipPathLayoutBox.Of(container), negHalf);
        }

        // Clears every margin this manipulator still owns (invoked on detach / removal / mode flip).
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
        // that method), so an edge flip landing here WHILE it is still mid-exit would change the same margin
        // its pinned position assumed stays put, visibly shifting it. This window already existed for the
        // original Left<->Top axis flip (a row<->column re-render mid-exit); the reverse-driven Left<->Right
        // / Top<->Bottom flips this file adds are the same shape of risk, not a new one.
        private void ClearEdge(VisualElement container, Edge edge)
        {
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                StyleArbitraryValueResolver.HandBack(container[i], SlotOf(edge));
            }
        }

        // Hands back the four-side child margins and the container's negative margin written by the wrap path.
        private void ClearHalfMargin(VisualElement container)
        {
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                HandBackMargins(container[i]);
            }
            HandBackMargins(ClipPathLayoutBox.Of(container));
            _applied = Edge.None;
        }

        // Offers every tracked element that is no longer a current child of container (reparented or
        // removed) for release, then prunes it from the tracking list.
        private void ResetStaleMargined(VisualElement container)
        {
            for (var i = _margined.Count - 1; i >= 0; i--)
            {
                var child = _margined[i];
                if (child.parent != container)
                {
                    ReleaseGapMargins(child);
                    _margined.RemoveAt(i);
                }
            }
        }

        // Offers every tracked element for release (used on Clear / detach).
        private void ResetAllMargined()
        {
            foreach (var child in _margined)
            {
                ReleaseGapMargins(child);
            }
            _margined.Clear();
        }

        // The claim in ReconcilerContext.ChildBoxOwners decides this, not the tracked list, and every
        // turn-off on a child that has LEFT the container goes through here. The hand-backs that land on
        // CURRENT children do not ask — ClearEdge and ClearHalfMargin clear an edge this pass is about to
        // rewrite. It hands back all four margin edges
        // rather than the currently applied one: an earlier axis or mode flip may have written any of them
        // while this element was still a member.
        private void ReleaseGapMargins(VisualElement child)
        {
            if (StyleChildOwnership.TryRelease(_ctx.ChildBoxOwners, child, this))
            {
                HandBackMargins(child);
            }
        }

        private static void HoldMargins(VisualElement element, StyleLength value)
        {
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginLeft, value);
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginRight, value);
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginTop, value);
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginBottom, value);
        }

        private static void HandBackMargins(VisualElement element)
        {
            StyleArbitraryValueResolver.HandBack(element, HeldSlot.MarginLeft);
            StyleArbitraryValueResolver.HandBack(element, HeldSlot.MarginRight);
            StyleArbitraryValueResolver.HandBack(element, HeldSlot.MarginTop);
            StyleArbitraryValueResolver.HandBack(element, HeldSlot.MarginBottom);
        }

#pragma warning disable CS8524 // no discard arm: a new edge has to name the slot it writes
        private static HeldSlot SlotOf(Edge edge) => edge switch
        {
            Edge.Left => HeldSlot.MarginLeft,
            Edge.Right => HeldSlot.MarginRight,
            Edge.Top => HeldSlot.MarginTop,
            Edge.Bottom => HeldSlot.MarginBottom,
            Edge.None => throw new System.ArgumentOutOfRangeException(nameof(edge), edge, "names no slot"),
        };
#pragma warning restore CS8524

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
                // MUTANT_SURVIVES(equivalent): the signature is only compared for equality, and subtracting the
                // gap's hash tells two gaps apart exactly as adding it does.
                hash = hash * 31 + _spec.Gap.GetHashCode();
                hash = hash * 31 + (wrap ? 1 : 100 + (int)ResolveEdge(container));
                var count = container.childCount;
                hash = hash * 31 + count;
                hash = StyleOutOfFlowChild.HashChildSequence(hash, container);
                return hash;
            }
        }

        // A space-* spec takes its edge from its own axis's reverse marker and nothing else: Tailwind's
        // space-x-reverse is what moves that margin, and flex-direction is never consulted. A gap-* spec takes
        // it from the resolved direction and never from a marker, since CSS gap spaces between children
        // whatever order they paint in. GapAxis.Horizontal / Vertical fix the axis; GapAxis.Auto (plain gap-*)
        // follows the resolved one. The flip is per axis: a horizontal gap never reacts to column-reverse, and
        // a vertical gap never reacts to row-reverse.
        private Edge ResolveEdge(VisualElement container)
        {
            if (_spec.Space)
            {
                return _spec.Axis == GapAxis.Horizontal
                    ? (_spec.XReverse ? Edge.Right : Edge.Left)
                    : (_spec.YReverse ? Edge.Bottom : Edge.Top);
            }
            var direction = StyleFlexDirectionResolver.Resolve(container, !ReferenceEquals(container, target));
            switch (_spec.Axis)
            {
                case GapAxis.Horizontal:
                    return direction == FlexDirection.RowReverse ? Edge.Right : Edge.Left;
                case GapAxis.Vertical:
                    return direction == FlexDirection.ColumnReverse ? Edge.Bottom : Edge.Top;
                default:
                    return direction == FlexDirection.Row || direction == FlexDirection.RowReverse
                        ? (direction == FlexDirection.RowReverse ? Edge.Right : Edge.Left)
                        : (direction == FlexDirection.ColumnReverse ? Edge.Bottom : Edge.Top);
            }
        }

        // True when the child container wraps (selects the four-side half-margin path). Read from the child
        // container, like the direction (see StyleFlexDirectionResolver): a wrap class on a composite widget
        // sets only the widget's own. This needs no engine-default variant for an inner box the way the
        // direction resolve does — the fallback below is already the engine's own no-wrap. The residue is
        // an inner box whose built-in USS wraps, which only a live panel reports; it bites harder here than
        // for direction, since wrap is the only mode that writes the container's own margin.
        // The flex-wrap / flex-nowrap / flex-wrap-reverse markers are consulted first, in _layout.uss's
        // declaration order (flex-wrap-reverse beats flex-nowrap beats flex-wrap when more than one is
        // present). There is no further "direction class implies a default" tier: flex / flex-row(-reverse)
        // / flex-col(-reverse) set flex-direction only, and since nearly every real container carries one,
        // treating them as evidence would take the resolvedStyle fallback away from almost all of them and
        // misread a genuinely wrapping inline-styled container as non-wrapping. That fallback is the
        // catch-all for wrap set some other way (a custom stylesheet rule, an inline style).
        // Once a marker leaves the class list the fallback is not taken until the next GeometryChangedEvent:
        // until a layout pass has run, resolvedStyle can still hold the style pass that saw the marker, and
        // a container that keeps its size would fire no event to correct a stale "wrap". Until then it
        // answers the engine's no-wrap.
        private bool IsWrap(VisualElement container)
        {
            var marker = WrapMarker(container);
            if (marker != null)
            {
                _wrapFromMarker = true;
                return marker.Value;
            }
            if (_wrapFromMarker)
            {
                _wrapFromMarker = false;
                _wrapMarkerLeft = true;
            }
            if (_wrapMarkerLeft)
            {
                return false;
            }
            if (container.panel != null)
            {
                var wrap = container.resolvedStyle.flexWrap;
                return wrap == Wrap.Wrap || wrap == Wrap.WrapReverse;
            }
            return false;
        }

        private static bool? WrapMarker(VisualElement container)
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
            return null;
        }
    }
}
