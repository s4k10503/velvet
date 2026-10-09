using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The axis a gap spaces children along. Within an axis, StyleGapManipulator still picks the leading
    // vs. trailing physical edge (margin-left/-top vs. margin-right/-bottom) — see
    // StyleGapManipulator.ResolveGapSlot.
    internal enum GapAxis
    {
        // Plain gap-*: follow the container's resolved flex-direction.
        Auto,
        // gap-x-*/space-x-*: always horizontal.
        Horizontal,
        // gap-y-*/space-y-*: always vertical.
        Vertical,
    }

    // Framework-level polyfill for CSS gap and for Tailwind's space-* margin rule. UI Toolkit (6000.3) has
    // no flex gap and no :first-child / :last-child selector, and Velvet owns the ordered child list, so this
    // manipulator writes the margins itself. Each in-flow child's four margin slots are summed from two
    // independent sources, as the two compose in a browser (gap on the container, margin on the child):
    //   gap-*: the leading margin (margin-left for a row, margin-top for a column) on every child EXCEPT the
    //   first, so the spacing sits strictly BETWEEN children. A row-reverse / column-reverse container moves
    //   it to the axis's trailing edge (margin-right / margin-bottom), the edge that sits between the
    //   visually adjacent pair there. Under flex-wrap it switches to the half-margin strategy below.
    //   space-x-* / space-y-*: Tailwind v4's `:where(& > :not(:last-child))` rule — margin-inline-end
    //   (margin-right) / margin-block-end (margin-bottom) on every child EXCEPT the last, or the start edge
    //   under space-x-reverse / space-y-reverse. flex-direction and flex-wrap are never read for it, and a
    //   margin the child's own classes set on that edge wins over it, as a class wins over Tailwind's
    //   zero-specificity `:where()` there.
    // A margin slot this pass does not ask for but still holds is handed back to the child's own layers.
    // Lifecycle mirrors the other style manipulators (StyleVariantManipulator): the
    // reconciler attaches one per gap container, keeps it in ReconcilerContext.GapManipulators,
    // and removes it on cleanup / dispose. UnregisterCallbacksFromTarget clears what it still owns —
    // the margins on its current children, and on a departed one only while
    // ReconcilerContext.ChildBoxOwners still names this manipulator.
    // Child container. The manipulator is attached to the gap ELEMENT, but its children are reconciled
    // into FiberNodePatcher.GetChildContainer(element) — a composite widget's inner box, not the widget.
    // Everything naming a container names that one: the iteration, the wrap path's negative margin, and
    // the direction / wrap verdicts (DirectionOf, IsWrap). A direction or wrap class on the widget
    // governs the WIDGET's box, which is not the box the spaced children are in.
    // Re-application. The spacing depends on the child set and, for a gap, the resolved direction, both of
    // which change outside this manipulator's own events. It is re-applied from three sources: (1) the
    // reconciler calls Apply right after it reconciles the container's children (the panel-independent path
    // that also covers EditMode, where layout never ticks); (2) GeometryChangedEvent catches child add /
    // remove / reorder driven by an unrelated reconcile pass at runtime; (3) AttachToPanelEvent re-resolves
    // once resolvedStyle is valid, for the one case neither an inline value nor a class can cover (see
    // StyleFlexDirectionResolver: an inline flex-direction is read first, then the five direction/display
    // classes, even on a panel — resolvedStyle is only the fallback).
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
    // space the main axis. So a gap switches strategy under wrap: gap/2 on ALL FOUR sides of EVERY child
    // and -gap/2 on all four sides of the CHILD CONTAINER. Adjacent items (in either axis, including across
    // wrapped lines) are then separated by gap/2 + gap/2 == gap, and the container's negative margin pulls
    // content flush to its edge. Wrap is detected from the child container — see IsWrap. The half-margin
    // path writes layout-independent margins, so it is fully resolved (and assertable) without a layout
    // tick. Direction never changes which edges wrap spaces (always symmetric).
    // Residual gaps versus native CSS gap (documented, not solved):
    // An explicit per-child margin on an edge a gap writes (e.g. ml-2 on a child under a gap-x-4 row) is
    // OVERWRITTEN — the manipulator owns the margin slots it writes each pass, where CSS adds the two. A
    // margin-based polyfill cannot both BE the gap and preserve an explicit margin on the same edge without
    // per-child base-margin tracking, which would be fragile against re-apply. Use padding, an inner
    // wrapper, or a different axis when a child needs its own margin on the gap edge. Margins on an edge
    // nothing here writes are preserved; under the wrap half-margin path all four edges belong to the gap.
    // The wrap half-margin path writes the CHILD CONTAINER's own four margins (-gap/2),
    // so an explicit container margin (e.g. m-4 on the same element) is OVERWRITTEN while a wrapping
    // gap is active. Non-wrap containers never touch the container's own margin. Use an outer wrapper
    // for a margin on a wrapping gap container.
    // The wrap half-margin path's container negative margin (-gap/2 on all four sides)
    // bleeds gap/2 OUTWARD, overlapping the container's own siblings or its parent's padding by
    // gap/2. Non-wrap containers never bleed (they write no container margin).
    internal sealed class StyleGapManipulator : Manipulator, IChildClassWatcher
    {
        private static readonly HeldSlot[] s_marginSlots =
        {
            HeldSlot.MarginTop, HeldSlot.MarginRight, HeldSlot.MarginBottom, HeldSlot.MarginLeft,
        };

        private readonly ReconcilerContext _ctx;

        // Source asymmetry (by design, not an oversight): the reverse markers are extracted at gap-config time
        // from the same class array the gap and space values are read from, so a variant-prefixed
        // md:space-x-reverse resolves with the rest of the spec — the patcher switches that array to the
        // element's live class list once a variant has toggled any layout gate class onto it, which is what
        // carries the marker across a breakpoint.
        // StyleFlexDirectionResolver, by contrast, reads the live child container's classList
        // unconditionally: unlike the gap spec, the direction can change with no matching gap-config patch.
        private GapSpec _spec;

        // Every element this manipulator has written a margin to. On each Apply / Clear, any tracked element
        // that is no longer a current child of the child container is offered for release; whether it loses
        // the margin is the claim's answer, not this list's — see ReconcilerContext.ChildBoxOwners.
        private readonly List<VisualElement> _margined = new();

        // Whether the child container carries the wrap path's negative margin.
        private bool _containerHeld;

        // One child's summed margins and which slots anything asked for, indexed by HeldSlot; reused per child.
        private readonly float[] _margins = new float[s_marginSlots.Length];
        private readonly bool[] _wanted = new bool[s_marginSlots.Length];

        // Which slots a gap writes; a slot only a space margin writes yields to the child's own layers.
        private readonly bool[] _gapWanted = new bool[s_marginSlots.Length];

        // Signature of the last successful Apply. Apply() early-returns when this is unchanged, so the
        // GeometryChanged churn the margin writes themselves provoke (and repeated reconcile passes that do
        // not touch the child set) are no-ops.
        private int _lastSignature;
        private bool _hasSignature;

        // Null unless the child container is a separate element — see ObserveChildContainer.
        private VisualElement? _observed;

        // Whether the last read found a class — a flex-wrap / flex-nowrap / flex-wrap-reverse marker for the
        // wrap, one of the five direction/display classes for the direction — and whether such a class has
        // left since the last GeometryChangedEvent. They record class presence, not which source answered.
        // See DirectionOf and IsWrap.
        private bool _wrapFromMarker;
        private bool _directionFromClass;
        private bool _directionClassLeft;
        private bool _wrapClassLeft;

        public StyleGapManipulator(ReconcilerContext ctx, GapSpec spec)
        {
            _ctx = ctx;
            _spec = spec;
        }

        // Swaps the spec and re-applies.
        public void UpdateGap(GapSpec spec)
        {
            _spec = spec;
            // Force a re-apply: the spec changed even when the child set did not, so invalidate the cache.
            Reapply();
        }

        // A class of a child's own changed: a space margin may now give way to it, or a display:none child
        // take no gap slot.
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
            _directionClassLeft = false;
            _wrapClassLeft = false;
            Apply();
        }

        // The container children are reconciled into (a composite widget's inner box; else self).
        private VisualElement? ChildContainer
            => target == null ? null : FiberNodePatcher.GetChildContainer(target);

        // Sums what the gap and the two space axes ask of each in-flow child's margin slots, holds those and
        // hands back the rest, and holds or hands back the container's negative margin for the wrap path.
        // Early-returns when nothing relevant (spec, wrap, gap slot, child set) changed since the last
        // successful application.
        public void Apply()
        {
            var container = ChildContainer;
            if (container == null)
            {
                return;
            }

            var wrap = (_spec.HasColumnGap || _spec.HasRowGap) && IsWrap(container);
            var gapSlot = ResolveGapSlot(container);
            var signature = ComputeSignature(container, wrap, gapSlot);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Must run before _margined.Clear(): it reads the pre-clear list to find children that left.
            ResetStaleMargined(container);
            _margined.Clear();

            // Tailwind's `:last-child` counts an absolutely positioned child, so the space rule reads the raw index;
            // CSS gap spaces only boxed in-flow children, so the gap reads its own count.
            var lastIndex = StyleOutOfFlowChild.LastSpacedIndex(container);
            var count = container.childCount;
            var gapIndex = 0;
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
                System.Array.Clear(_margins, 0, _margins.Length);
                System.Array.Clear(_wanted, 0, _wanted.Length);
                System.Array.Clear(_gapWanted, 0, _gapWanted.Length);
                // A display:none child has no box, so CSS gap neither spaces it nor counts it as the first.
                if (!StyleOutOfFlowChild.HasNoBox(child))
                {
                    SumGap(gapIndex, wrap, gapSlot);
                    gapIndex++;
                }
                if (i != lastIndex)
                {
                    SumSpace(child);
                }
                WriteMargins(child);
                StyleChildOwnership.Claim(_ctx.ChildBoxOwners, child, this);
                _margined.Add(child);
            }

            var box = ClipPathLayoutBox.Of(container);
            if (wrap)
            {
                HoldContainerAxis(box, HeldSlot.MarginLeft, HeldSlot.MarginRight, _spec.HasColumnGap, _spec.ColumnGap);
                HoldContainerAxis(box, HeldSlot.MarginTop, HeldSlot.MarginBottom, _spec.HasRowGap, _spec.RowGap);
            }
            else if (_containerHeld)
            {
                HandBackMargins(box);
            }
            _containerHeld = wrap;

            _lastSignature = signature;
            _hasSignature = true;
        }

        // Adds what CSS gap asks of the boxed child at gapIndex. Without wrap only the main axis is spaced: the
        // column gap along a row, the row gap down a column, each on every child but the first. Under wrap every
        // child takes half of each gap on both edges of that axis.
        private void SumGap(int gapIndex, bool wrap, HeldSlot gapSlot)
        {
            if (wrap)
            {
                AddGapPair(HeldSlot.MarginLeft, HeldSlot.MarginRight, _spec.HasColumnGap, _spec.ColumnGap / 2f);
                AddGapPair(HeldSlot.MarginTop, HeldSlot.MarginBottom, _spec.HasRowGap, _spec.RowGap / 2f);
                return;
            }
            if (gapIndex == 0)
            {
                return;
            }
            if (MainGap(gapSlot, out var gap))
            {
                AddGap(gapSlot, gap);
            }
        }

        // The main axis's gap, which gapSlot lies on: the column gap along a row, the row gap down a column.
        private bool MainGap(HeldSlot gapSlot, out float gap)
        {
            switch (gapSlot)
            {
                case HeldSlot.MarginLeft:
                case HeldSlot.MarginRight:
                    gap = _spec.ColumnGap;
                    return _spec.HasColumnGap;
                default:
                    gap = _spec.RowGap;
                    return _spec.HasRowGap;
            }
        }

        private void AddGapPair(HeldSlot start, HeldSlot end, bool has, float value)
        {
            if (!has)
            {
                return;
            }
            AddGap(start, value);
            AddGap(end, value);
        }

        // Adds Tailwind's space margins for a child that is not the last. A space margin gives way to one the
        // child's own classes set on that edge: a USS utility here, an arbitrary value through a yielding hold.
        private void SumSpace(VisualElement child)
        {
            var space = _spec.Space;
            AddSpace(child, space.XReverse ? HeldSlot.MarginLeft : HeldSlot.MarginRight, space.X);
            AddSpace(child, space.YReverse ? HeldSlot.MarginTop : HeldSlot.MarginBottom, space.Y);
        }

        private void AddSpace(VisualElement child, HeldSlot slot, float value)
        {
            if (value != 0f && !StyleArbitraryValueResolver.DeclaresOwn(child, slot))
            {
                Add(slot, value);
            }
        }

        private void Add(HeldSlot slot, float value)
        {
            _margins[(int)slot] += value;
            _wanted[(int)slot] = true;
        }

        private void AddGap(HeldSlot slot, float value)
        {
            Add(slot, value);
            _gapWanted[(int)slot] = true;
        }

        // Holds every slot SumMargins asked for and hands back the others this child still has held.
        private void WriteMargins(VisualElement child)
        {
            foreach (var slot in s_marginSlots)
            {
                if (!_wanted[(int)slot])
                {
                    StyleArbitraryValueResolver.HandBackIfHeld(child, slot);
                    continue;
                }
                StyleArbitraryValueResolver.Hold(child, slot, new StyleLength(_margins[(int)slot]));
                if (!_gapWanted[(int)slot])
                {
                    StyleArbitraryValueResolver.Yield(child, slot);
                }
            }
        }

        // Clears every margin this manipulator still owns (invoked on detach / removal).
        private void Clear()
        {
            var container = ChildContainer;
            if (container != null)
            {
                if (_containerHeld)
                {
                    HandBackMargins(ClipPathLayoutBox.Of(container));
                }
            }
            ResetAllMargined();
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
        // turn-off through Clear or on a child that has LEFT the container goes through here. The hand-backs
        // Apply makes on CURRENT children do not ask: this pass has just claimed them. It hands back all four
        // margin edges: an earlier pass may have written any of them while this element was still a member.
        private void ReleaseGapMargins(VisualElement child)
        {
            if (StyleChildOwnership.TryRelease(_ctx.ChildBoxOwners, child, this))
            {
                HandBackMargins(child);
            }
        }

        // The wrap path's negative margin on one axis of the container: -gap/2 on both edges, or nothing held
        // there when that axis has no gap.
        private static void HoldContainerAxis(VisualElement box, HeldSlot start, HeldSlot end, bool has, float gap)
        {
            if (!has)
            {
                StyleArbitraryValueResolver.HandBackIfHeld(box, start);
                StyleArbitraryValueResolver.HandBackIfHeld(box, end);
                return;
            }
            StyleArbitraryValueResolver.Hold(box, start, new StyleLength(-gap / 2f));
            StyleArbitraryValueResolver.Hold(box, end, new StyleLength(-gap / 2f));
        }

        private static void HandBackMargins(VisualElement element)
        {
            foreach (var slot in s_marginSlots)
            {
                StyleArbitraryValueResolver.HandBack(element, slot);
            }
        }

        // A cheap order-sensitive hash of the inputs besides the spec that change the applied margins — the
        // spec only changes through UpdateGap, which drops the signature itself: the wrap verdict, the gap's
        // resolved slot, the current child identity sequence, and which children have no box, which an inline
        // display changes with the sequence as it was. The gap slot has to be
        // the slot itself rather than an axis bit — Left→Right or Top→Bottom is a same-axis flip (a direction
        // toggling without the row/column axis itself changing), and a signature collision here would skip
        // the re-apply that moves the margin to the new edge.
        private static int ComputeSignature(VisualElement container, bool wrap, HeldSlot gapSlot)
        {
            unchecked
            {
                var hash = StyleOutOfFlowChild.HashChildSequence(wrap ? HeldSlotGroups.SlotCount : (int)gapSlot, container);
                var count = container.childCount;
                for (var i = 0; i < count; i++)
                {
                    // MUTANT_SURVIVES(equivalent, arithmetic): subtracting the term changes the hash wherever adding it does.
                    hash = hash * 31 + (StyleOutOfFlowChild.HasNoBox(container[i]) ? 1 : 0);
                }
                return hash;
            }
        }

        // The slot a non-wrap gap writes on every child but the first: the leading edge of the main axis, or the
        // trailing one when the resolved direction reverses it, which keeps it between the visually adjacent
        // pair. A space-* marker never reaches it: CSS gap has none.
        private HeldSlot ResolveGapSlot(VisualElement container)
        {
            switch (DirectionOf(container))
            {
                case FlexDirection.Row:
                    return HeldSlot.MarginLeft;
                case FlexDirection.RowReverse:
                    return HeldSlot.MarginRight;
                case FlexDirection.ColumnReverse:
                    return HeldSlot.MarginBottom;
                default:
                    return HeldSlot.MarginTop;
            }
        }

        // Read in the order inline flex-direction, then the class verdict (StyleFlexDirectionResolver), then
        // resolvedStyle. Once every direction/display class has left the class list, the fallback is not taken
        // until the next GeometryChangedEvent — for the reason IsWrap gives — and the verdict is Column, the
        // direction an element carrying none of those classes lays out in.
        private FlexDirection DirectionOf(VisualElement container)
        {
            var fromClass = StyleFlexDirectionResolver.FromClasses(container);
            var hadClass = _directionFromClass;
            _directionFromClass = fromClass != null;
            // MUTANT_SURVIVES(equivalent, clause removed): the flag is read only once the class is gone, and the
            // call that first finds it gone sets the flag itself, so setting it earlier changes nothing.
            if (hadClass && fromClass == null)
            {
                _directionClassLeft = true;
            }
            var inline = container.style.flexDirection;
            if (inline.keyword != StyleKeyword.Null)
            {
                return inline.value;
            }
            if (fromClass != null)
            {
                return fromClass.Value;
            }
            if (_directionClassLeft)
            {
                return FlexDirection.Column;
            }
            return StyleFlexDirectionResolver.ResolveWithoutClasses(container, !ReferenceEquals(container, target));
        }

        // True when the child container wraps (selects the four-side half-margin path). Read from the child
        // container, like the direction (see StyleFlexDirectionResolver): a wrap class on a composite widget
        // sets only the widget's own. This needs no engine-default variant for an inner box the way the
        // direction resolve does — the fallback below is already the engine's own no-wrap. The residue is
        // an inner box whose built-in USS wraps, which only a live panel reports; it bites harder here than
        // for direction, since wrap is the only mode that writes the container's own margin.
        // An inline flex-wrap is read before the markers. The flex-wrap / flex-nowrap / flex-wrap-reverse markers
        // are consulted next, in _layout.uss's
        // declaration order (flex-wrap-reverse beats flex-nowrap beats flex-wrap when more than one is
        // present). There is no further "direction class implies a default" tier: flex / flex-row(-reverse)
        // / flex-col(-reverse) set flex-direction only, and since nearly every real container carries one,
        // treating them as evidence would take the resolvedStyle fallback away from almost all of them and
        // misread a genuinely wrapping inline-styled container as non-wrapping. That fallback is the
        // catch-all for wrap set by a custom stylesheet rule.
        // Once a marker leaves the class list the fallback is not taken until the next GeometryChangedEvent:
        // until a layout pass has run, resolvedStyle can still hold the style pass that saw the marker, and
        // a container that keeps its size would fire no event to correct a stale "wrap". Until then an unset
        // inline flex-wrap answers no-wrap, the engine's own default.
        private bool IsWrap(VisualElement container)
        {
            var marker = WrapMarker(container);
            var hadMarker = _wrapFromMarker;
            _wrapFromMarker = marker != null;
            // MUTANT_SURVIVES(equivalent, clause removed): the flag is read only once the marker is gone, and the
            // call that first finds it gone sets the flag itself, so setting it earlier changes nothing.
            if (hadMarker && marker == null)
            {
                _wrapClassLeft = true;
            }
            var inline = container.style.flexWrap;
            if (inline.keyword != StyleKeyword.Null)
            {
                return inline.value != Wrap.NoWrap;
            }
            if (marker != null)
            {
                return marker.Value;
            }
            if (_wrapClassLeft)
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
