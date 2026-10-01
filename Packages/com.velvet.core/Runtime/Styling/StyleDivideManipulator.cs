using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Framework-level divide-* polyfill: Tailwind v4's `:where(& > :not(:last-child))` rule, a border on
    // every child but the last, horizontal for divide-x and vertical for divide-y. UI Toolkit (6000.3) has no
    // :last-child and no `> *` child combinator, so a USS rule cannot express it; Velvet owns the ordered child
    // list, so this manipulator writes it. The edge is the end one (border-right / border-bottom), or the start
    // one under divide-x-reverse / divide-y-reverse — see ResolveEdge.
    //
    // Lifecycle mirrors StyleGapManipulator: the reconciler attaches one per divide container, keeps it
    // in ReconcilerContext.DivideManipulators, and removes it on cleanup / dispose.
    // UnregisterCallbacksFromTarget clears what it still owns, on the same terms the gap manipulator's
    // header states, against ReconcilerContext.ChildDividerOwners.
    // Re-application has the same three sources as the gap manipulator:
    // the reconciler's post-child-reconcile call (the panel-independent path that also covers EditMode),
    // GeometryChangedEvent (child add / remove / reorder from an unrelated reconcile), and
    // AttachToPanelEvent. A
    // signature makes a redundant Apply (notably the GeometryChanged feedback its own writes provoke) a
    // no-op.
    //
    // Line style: divide-solid is a plain inline border. divide-dashed / divide-dotted have no UI Toolkit
    // border-style, so the manipulator still reserves the SAME gutter (the real width) but masks the color
    // with the sentinel and hands each divided child a DivideDashChildBinding (DivideDashPainter) that paints
    // the dashed / dotted stroke on the child's own generateVisualContent — so switching between solid and
    // dashed is layout-identical and only the paint differs.
    //
    // Child container. Like the gap manipulator it iterates FiberNodePatcher.GetChildContainer(target) — a
    // composite widget's inner box; else self.
    //
    // A child's own border width or color on an edge the divider writes (e.g. border-r-4 on a child of a
    // divide-x row) wins there, as it does over Tailwind's zero-specificity divider — see ApplyToChild.
    // Limitations: a child whose border face is owned by a higher paint layer — a skew
    // silhouette or a drop shadow — keeps its border owned there, so its dashed divider renders solid (a
    // documented known limitation, mirroring the element-level border-dashed gate which defers to either).
    // A dashed / dotted divider's color is read when the child paints, not when the container applies — see
    // DivideDashPainter.PaintColor.
    //
    // Out-of-flow children (position: absolute) are excluded from the index walk — see
    // StyleOutOfFlowChild — the same way StyleGapManipulator excludes them: an out-of-flow child (a
    // PopLayout-pinned ghost, or an app-authored .absolute child) draws no divider, but it counts toward which
    // child is last, as `:last-child` counts it — see StyleOutOfFlowChild.LastSpacedIndex.
    internal sealed class StyleDivideManipulator : Manipulator, IChildClassWatcher
    {
        private static readonly DivideEdge[] s_edges = (DivideEdge[])System.Enum.GetValues(typeof(DivideEdge));

        private DivideSpec _spec;
        private readonly ReconcilerContext _ctx;

        // Which of the four edges is currently written, so an axis flip OR a same-axis start↔end flip
        // (a reverse marker appearing or going) clears the abandoned edge before
        // writing the new one. Null until the first application.
        private DivideEdge? _applied;

        // Bit per DivideEdge this manipulator has applied at least once. A child that leaves the container
        // may still carry a border from an edge an earlier flip abandoned while that child was a member, so
        // the departure / teardown reset needs the union rather than just the current edge — and no more than
        // the union, so a child's own border on an edge this divide never claimed survives.
        private int _everApplied;

        // Every child this manipulator has written a divider border to. On each Apply / Clear any tracked
        // element no longer a current child is offered for release; whether it loses the border is the
        // claim's answer, not this list's — see ReconcilerContext.ChildDividerOwners.
        private readonly List<VisualElement> _bordered = new();

        private int _lastSignature;
        private bool _hasSignature;

        // Null unless the child container is a separate element — see ObserveChildContainer.
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

        // A class of a child's own changed: its divider width or color may now give way to it.
        public void Reapply()
        {
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

        // GeometryChangedEvent neither bubbles nor trickles — see StyleGapManipulator.ObserveChildContainer.
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
        // except the last. Clears the abandoned edge first whenever that edge changed. Early-returns when
        // nothing relevant (spec, edge, child set) changed since the last successful application.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            var edge = ResolveEdge();
            var signature = ComputeSignature(container, edge);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Must run before _bordered.Clear() below: it reads the pre-clear _bordered list to find children
            // that left the container.
            ResetStaleBordered(container);

            // Any of the four edges may be the one abandoned — an axis flip (divide-x ↔ divide-y) or a
            // same-axis start↔end flip (a reverse marker appearing or going).
            if (_applied.HasValue && _applied.Value != edge)
            {
                ClearEdge(container, _applied.Value);
            }
            _applied = edge;
            _everApplied |= 1 << (int)edge;
            _bordered.Clear();

            var count = container.childCount;
            var lastIndex = StyleOutOfFlowChild.LastSpacedIndex(container);
            for (var i = 0; i < count; i++)
            {
                // An out-of-flow child is not a layout sibling, so it draws no divider; it still takes the "last
                // child" slot, as it does for Tailwind's `:not(:last-child)`.
                if (StyleOutOfFlowChild.IsOutOfFlow(container[i]))
                {
                    continue;
                }
                var child = ClipPathLayoutBox.InnerOf(container[i]);
                var isDivider = i != lastIndex;
                ApplyToChild(child, edge, isDivider);
                StyleChildOwnership.Claim(_ctx.ChildDividerOwners, child, this);
                _bordered.Add(child);
            }

            _lastSignature = signature;
            _hasSignature = true;
        }

        // Writes one child's divider on the given edge. Solid keeps the plain inline-border path verbatim; a
        // dashed / dotted divider reserves the same gutter width, masks the native color with the sentinel, and
        // paints the stroke on the child's own generateVisualContent (DivideDashChildBinding). A divide-{color}
        // colors all four edges of a divided child, as Tailwind's `border-color` does. Tailwind writes all of it
        // at zero specificity, so a width or color the child's own classes set on an edge wins there; under
        // divide-dashed that width is still dashed, since Tailwind's border-*-N takes the border style the
        // divider set.
        private void ApplyToChild(VisualElement child, DivideEdge edge, bool isDivider)
        {
            var ownWidth = StyleArbitraryValueResolver.DeclaresOwn(child, WidthSlot(edge));
            // A skew silhouette or a drop shadow owns the child's border face and repaints a solid border, so a
            // dashed divider on the same child would fight it — route it through the solid path (documented known
            // limitation). Gates on EITHER owner, mirroring the element-level border-dashed gate.
            var dashed = (_spec.Style == BorderLineStyle.Dashed || _spec.Style == BorderLineStyle.Dotted)
                && !_ctx.SkewBindings.ContainsKey(child)
                && !_ctx.ShadowBindings.ContainsKey(child);

            if (!dashed || !isDivider)
            {
                // Solid divider, the last child (no divider), or a child whose face a skew / shadow layer owns:
                // a plain inline border. Detach any stale dash paint (e.g. a divide-dashed → divide-solid flip, or
                // a colored child reordered to the last slot).
                DetachDash(child);
                WriteWidth(child, edge, isDivider && !ownWidth);
                WriteColors(child, null, isDivider);
                return;
            }

            // Dashed / dotted divider: read any inline color of the child's own BEFORE masking.
            _ctx.DivideDashBindings.TryGetValue(child, out var binding);
            var inline = InlineOwnColor(child, edge, binding);
            var driven = DrivenColor(child, edge);

            // Reserve the gutter as a solid divider would, but mask the native border color so only the dashed /
            // dotted paint shows.
            WriteWidth(child, edge, !ownWidth);
            StyleArbitraryValueResolver.Mask(child, ColorSlot(edge));
            WriteColors(child, edge, isDivider);

            if (binding != null)
            {
                DivideDashPainter.Update(child, binding, edge, _spec.Style);
            }
            else
            {
                binding = DivideDashPainter.Attach(child, edge, _spec.Style);
                _ctx.DivideDashBindings[child] = binding;
            }
            binding.Divider = _spec.HasColor ? _spec.Color : (Color?)null;
            binding.Inline = inline;
            if (driven.HasValue)
            {
                binding.Driven = driven;
            }
        }

        // The color a motion driver wrote on the edge before the mask took it, where one drives the slot.
        private static Color? DrivenColor(VisualElement child, DivideEdge edge)
        {
            var written = InlineColor(child, edge);
            if (!StyleArbitraryValueResolver.IsDriven(child, ColorSlot(edge)) || DivideDashPainter.IsMask(written)
                || written.keyword != StyleKeyword.Undefined)
            {
                return null;
            }
            return written.value;
        }

        // Holds the divider's width on edge, giving way to a layer of the child's own, or hands the slot back.
        private void WriteWidth(VisualElement child, DivideEdge edge, bool holds)
        {
            if (holds)
            {
                StyleArbitraryValueResolver.Hold(child, WidthSlot(edge), new StyleFloat(_spec.Width));
                StyleArbitraryValueResolver.Yield(child, WidthSlot(edge));
            }
            else
            {
                StyleArbitraryValueResolver.HandBack(child, WidthSlot(edge));
            }
        }

        // A color written inline on the child's edge by code of its own, kept once the mask takes the slot. On an
        // edge the binding does not mask yet, a value a layer or a hold wrote is not one: PaintColor reads layers
        // itself, and a hold is a divider's. On the edge it masks, a value other than the mask was written over it
        // after the last Apply, and is taken as the child's own, as a solid divider without a divide color leaves
        // a color written over its edge.
        private static Color? InlineOwnColor(VisualElement child, DivideEdge edge, DivideDashChildBinding? binding)
        {
            var slot = ColorSlot(edge);
            var inline = InlineColor(child, edge);
            if (DivideDashPainter.IsMask(inline))
            {
                return binding?.Inline;
            }
            // A motion driver's value is not the child's own: the dash takes it while the drive lasts (Drive).
            if (inline.keyword != StyleKeyword.Undefined || StyleArbitraryValueResolver.IsDriven(child, slot))
            {
                return null;
            }
            if (binding?.Edge == edge)
            {
                return inline.value;
            }
            if (StyleArbitraryValueResolver.IsHeld(child, slot)
                || StyleArbitraryValueResolver.ResolveLayered(child, slot) != null)
            {
                return null;
            }
            return inline.value;
        }

        // Holds the divide-{color} on every edge of a divided child but skip, where the child's own classes set
        // no color of their own, and hands back the edges this manipulator holds and no longer colors.
        private void WriteColors(VisualElement child, DivideEdge? skip, bool isDivider)
        {
            foreach (var edge in s_edges)
            {
                if (edge == skip)
                {
                    continue;
                }
                var slot = ColorSlot(edge);
                if (isDivider && _spec.HasColor && !StyleArbitraryValueResolver.DeclaresOwn(child, slot))
                {
                    StyleArbitraryValueResolver.Hold(child, slot, new StyleColor(_spec.Color));
                    StyleArbitraryValueResolver.Yield(child, slot);
                }
                else
                {
                    StyleArbitraryValueResolver.HandBackIfHeld(child, slot);
                }
            }
        }

        // Tailwind's border-inline-end / border-bottom, or border-inline-start / border-top under the axis's
        // reverse marker. flex-direction is never read: a reversed container takes the marker, as in Tailwind.
        private DivideEdge ResolveEdge()
        {
            if (_spec.Axis == DivideAxis.Horizontal)
            {
                return _spec.Reverse ? DivideEdge.Left : DivideEdge.Right;
            }
            return _spec.Reverse ? DivideEdge.Top : DivideEdge.Bottom;
        }

#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
        internal static StyleColor InlineColor(VisualElement child, DivideEdge edge) => edge switch
        {
            DivideEdge.Left => child.style.borderLeftColor,
            DivideEdge.Right => child.style.borderRightColor,
            DivideEdge.Top => child.style.borderTopColor,
            DivideEdge.Bottom => child.style.borderBottomColor,
        };
#pragma warning restore CS8524

#pragma warning disable CS8524 // no discard arm: a new edge has to name the slots it writes
        private static HeldSlot WidthSlot(DivideEdge edge) => edge switch
        {
            DivideEdge.Left => HeldSlot.BorderLeftWidth,
            DivideEdge.Right => HeldSlot.BorderRightWidth,
            DivideEdge.Top => HeldSlot.BorderTopWidth,
            DivideEdge.Bottom => HeldSlot.BorderBottomWidth,
        };

        internal static HeldSlot ColorSlot(DivideEdge edge) => edge switch
        {
            DivideEdge.Left => HeldSlot.BorderLeftColor,
            DivideEdge.Right => HeldSlot.BorderRightColor,
            DivideEdge.Top => HeldSlot.BorderTopColor,
            DivideEdge.Bottom => HeldSlot.BorderBottomColor,
        };
#pragma warning restore CS8524

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
                var child = ClipPathLayoutBox.InnerOf(container[i]);
                DetachDash(child);
                ResetEdge(child, edge);
            }
        }

        // Offers any tracked element no longer a current child for release, then prunes it.
        private void ResetStaleBordered(VisualElement container)
        {
            for (var i = _bordered.Count - 1; i >= 0; i--)
            {
                var child = _bordered[i];
                if (ClipPathLayoutBox.Of(child).parent != container)
                {
                    ReleaseBordered(child);
                    _bordered.RemoveAt(i);
                }
            }
        }

        private void ResetAllBordered()
        {
            foreach (var child in _bordered)
            {
                ReleaseBordered(child);
            }
            _bordered.Clear();
        }

        // The claim in ReconcilerContext.ChildDividerOwners decides this, not the tracked list, and every
        // turn-off on a child that has LEFT the container goes through here. The hand-backs that land on
        // CURRENT children do not ask — ClearEdge clears an edge this pass is about to rewrite, and the
        // last child's own pass hands the no-divider edge back. The dash paint goes with the border:
        // it is the same divider drawn a different way, so a container that may not reset one may not
        // detach the other.
        private void ReleaseBordered(VisualElement child)
        {
            if (StyleChildOwnership.TryRelease(_ctx.ChildDividerOwners, child, this))
            {
                DetachDash(child);
                ResetOwnedEdges(child);
                WriteColors(child, null, false);
            }
        }

        // Resets every edge this manipulator has ever written, not just the currently applied one: an element
        // reaching here has left the container (or the manipulator is being torn down), so it may still carry
        // a border from an edge an earlier flip abandoned while it was still a member. Its width is reset only on
        // the edges a divider has used, so a child's own width on another edge survives; the colors on the other
        // edges are WriteColors' to hand back.
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
        {
            StyleArbitraryValueResolver.HandBack(child, WidthSlot(edge));
            StyleArbitraryValueResolver.HandBack(child, ColorSlot(edge));
        }

        // Order-sensitive hash of the inputs that change the applied borders: width, color, edge, line style,
        // and the current child identity sequence. Apply() early-returns when this matches the last
        // application. The edge term must distinguish all FOUR edges, not just the axis: Left→Right and
        // Top→Bottom are same-axis flips (a reverse marker appearing or going, with the spec otherwise
        // untouched), and a signature collision there would skip the re-apply that moves the
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
