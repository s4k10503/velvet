using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Framework-level approximation of CSS `text-wrap: balance` for a TextElement (Label / Button / …)
    // carrying the `text-balance` class. UI Toolkit's text engine exposes no line-break hook — there is
    // no way to influence WHERE a line wraps — so balance cannot be realized the way a browser does it
    // (re-flowing line breaks within a fixed box). Instead this manipulator narrows the box itself:
    // TextElement.MeasureTextSize(text, width, widthMode, height, heightMode) is the same public method
    // the engine's own measure callback (TextElement.DoMeasure) already calls every autosize pass, so a
    // bounded binary search over it — a handful of calls — costs the same order of work UI Toolkit
    // already pays, and:
    //   1. measures the natural height at the element's available width (the height a normal, unbalanced
    //      layout would take);
    //   2. binary-searches the NARROWEST width that keeps the measured height at or under that natural
    //      height (font metrics are constant across candidates, so comparing HEIGHTS is a stand-in for
    //      comparing line counts — a narrower width that adds a line always measures taller);
    //   3. writes that width as an inline `maxWidth`, so the box shrinks and the same text redistributes
    //      across its existing line count more evenly instead of leaving a near-empty last line.
    //
    // Deviation from CSS (documented, not solved): real `text-wrap: balance` never changes the box's own
    // width — only where lines break inside it. Our approximation narrows the box via `maxWidth`, so the
    // element's outer width (and anything sized from it — a background, a sibling relying on its bounds)
    // can shrink on balanced multi-line text, and text alignment then reads relative to that SMALLER box
    // rather than the original one. See Documentation~/fonts.md for the full writeup.
    //
    // The available-width problem. The width to balance AGAINST is the box's width BEFORE this
    // manipulator's own constraint — but this manipulator is the thing that writes the element's OWN
    // maxWidth, so target.contentRect.width is self-contaminated the moment a narrower maxWidth has
    // already taken effect: re-deriving "natural" from an already-balanced width would ratchet narrower
    // every pass instead of converging. StyleGridManipulator sidesteps the analogous problem by reading a
    // width it never writes (the container's, while it sizes the CHILDREN) — this manipulator mirrors
    // that discipline by reading the PARENT's contentRect (never written by this manipulator) minus the
    // target's own resolved horizontal margin, instead of the target's own contentRect. This is exact
    // when the target is the parent's sole / stretch-to-fill child (the common paragraph/heading case)
    // and an OVER-estimate when siblings share the row or the target carries its own narrower width /
    // max-width utility — but an over-estimate only makes the search less aggressive (maxWidth resolves
    // wide enough to never bind, so the box falls back to whatever flex would have given it anyway); it
    // never widens the box past what normal layout would already produce, so the deviation is a
    // conservative under-balance, not a wrong one. text-balance therefore OWNS the element's inline
    // maxWidth outright while its class is present — like StyleGridManipulator owns a column's width — so
    // a co-present max-w-* utility's inline write is overwritten. Unlike a one-time attach-time overwrite,
    // this ownership is enforced every patch: FiberNodePatcher.ApplyTextBalanceManipulator calls Refresh()
    // on an already-attached manipulator on every pass where the class remains present (mirroring
    // StyleGridManipulator.UpdateSpec / StyleGapManipulator.UpdateGap), so a same-patch max-w-* write that
    // lands earlier in the same pass (DiffClassList / ApplyClassNames apply class-driven inline styles
    // before this manipulator runs) is always re-overwritten before the patch ends, rather than sitting
    // exposed until an unrelated geometry event happens to fire. Ownership ends when the text-balance
    // class itself is removed: the teardown that clears the inline maxWidth then restores a co-present
    // max-w-* utility's own value via FiberNodePatcher.ReapplyArbitraryValues — the same shared-inline-slot
    // restore FiberWrapperElementAppliers.RestoreSharedInlineSlot performs after detaching a Hue/Pulse
    // motion — so removing just the text-balance token does not also erase an unrelated max-width the
    // element still carries.
    //
    // Single-line gate. CSS balance is a visual no-op on a single line. Since this approximation instead
    // shrinks the box, applying it to a single line would shrink a label to its tight text width and
    // change its box (background, centering-within-box) for no CSS-parity benefit — so Apply only writes
    // a narrower maxWidth when the text actually wraps (natural height at the available width exceeds the
    // unconstrained single-line height); otherwise any previously-written inline maxWidth is cleared.
    // Feeding a nowrap element through the same comparison naturally reaches the same "single line"
    // verdict without a separate white-space check, because MeasureTextSize already accounts for the
    // element's own resolved white-space when it measures.
    //
    // Prerequisite: Velvet's Label ships with no bundled base white-space rule, so its engine default is
    // nowrap. text-balance alone is therefore a silent no-op on a default Label — it also needs
    // `text-wrap` / `whitespace-normal` (or another wrap-enabling white-space) applied, or MeasureTextSize
    // reaches the single-line gate above on every measurement regardless of the text's length.
    //
    // Re-application / staleness. Re-derives on: AttachToPanelEvent (first resolve, and re-resolve after
    // a reparent that lands on an already-appropriately-sized rect with no fresh GeometryChangedEvent);
    // GeometryChangedEvent on the TARGET (the target's own resolved rect changed — covers the feedback its
    // own maxWidth write provokes, guarded below); GeometryChangedEvent on the PARENT (see the
    // ancestor-resize discussion below); and ChangeEvent<string> (TextElement dispatches this whenever
    // `.text` is set to a new value WHILE attached to a panel — see TextElement's
    // INotifyValueChanged<string>.value setter — so a text swap that happens to keep the exact same
    // wrapped box size, and would therefore raise no GeometryChangedEvent, is still caught).
    //
    // The ancestor-resize problem. Listening on the target alone is not enough: the maxWidth THIS
    // manipulator writes pins the target's own resolved size, so once it is narrower than the parent, an
    // ancestor WIDENING never changes the target's own rect at all (it stays clamped at the old maxWidth)
    // — no GeometryChangedEvent fires on the target, and the search stays stuck at the old, now-too-narrow
    // value forever; a narrowing ancestor that does not yet cross the clamped value is missed the same
    // way. Grid/Gap do not have this problem because they listen on the exact element whose contentRect
    // their own sizing reads (their own target IS the container); here that element is the PARENT (see
    // the available-width discussion above), so this manipulator additionally subscribes
    // GeometryChangedEvent on textElement.parent directly — the plain hierarchy parent, not
    // FiberNodePatcher.GetChildContainer(parent), to keep the subscribe/unsubscribe bookkeeping as simple
    // as the target's own listeners already are. The subscription is (re-)synced at the top of every
    // Apply() call, not only from AttachToPanelEvent, so a mid-life reparent is picked up by whichever
    // event fires next regardless of exactly how UI Toolkit sequences Attach/Detach for a same-panel
    // reparent. What remains uncovered: a resize confined entirely to a ScrollView parent's OWN
    // contentContainer (GetChildContainer's ScrollView case) with no accompanying change to the
    // ScrollView's own outer rect — e.g. a scrollbar toggling — since the subscription is the ScrollView
    // itself, not its contentContainer. A grandparent-or-higher resize is NOT a separate gap: available is
    // read from the parent's own contentRect, so if the parent's rect is genuinely unaffected by a
    // grandparent change, there is nothing that needed re-deriving in the first place.
    //
    // Feedback-loop guard. A signature over the available width (rounded), the text's hash, and the
    // resolved font size makes a GeometryChangedEvent that this manipulator's own maxWidth write provoked
    // — with none of those inputs actually different — a no-op regardless of which of the two
    // GeometryChangedEvent listeners fired it, mirroring StyleGridManipulator's documented discipline
    // exactly.
    //
    // Lifecycle mirrors the other per-element style manipulators (StyleGapManipulator /
    // StyleGridManipulator): the reconciler attaches one per text-balance element, keeps it in
    // ReconcilerContext.TextBalanceManipulators, and removes it on cleanup / dispose.
    // UnregisterCallbacksFromTarget clears the inline maxWidth it wrote (and drops the parent
    // subscription above) so removing the class or unmounting leaves no residue on the TARGET; a class
    // removal additionally has the reconciler restore a co-present max-w-* utility right after, per the
    // ownership paragraph above (a full unmount has no "current class list" to restore against, so it
    // does not). FiberElementPoolReset also nulls maxWidth generically on every pooled element as a
    // second line of defense against a pooled Label ghosting a prior consumer's balanced width.
    internal sealed class StyleTextBalanceManipulator : Manipulator
    {
        // Bounded binary-search depth: enough precision to land within a fraction of a pixel of the true
        // narrowest width without an unbounded measure-call budget.
        private const int MaxIterations = 8;

        // Floor for the search range, expressed as a fraction of the available width, so the search never
        // measures a degenerate near-zero width. A too-narrow candidate simply measures TALLER than the
        // natural height and is rejected by the comparison, so this floor only bounds the search range —
        // it does not need to equal the longest word's width for correctness.
        private const float MinWidthFraction = 0.1f;

        // Sub-pixel tolerance on height comparisons, mirroring StyleGridManipulator's WrapSafetyPx: absorbs
        // float rounding so an unchanged wrap outcome does not misregister as "one line taller".
        private const float HeightEpsilonPx = 0.5f;

        private int _lastSignature;
        private bool _hasSignature;

        // The parent currently subscribed for OnParentGeometryChanged — see the class doc's
        // ancestor-resize discussion. Tracked (rather than re-derived on every check) so SyncParentSubscription
        // can cheaply no-op when the parent has not changed, and so it can unregister the exact callback it
        // registered when the parent DOES change (or the manipulator detaches).
        private VisualElement? _subscribedParent;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            target.RegisterCallback<ChangeEvent<string>>(OnTextChanged);
            Apply();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Clear();
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            target.UnregisterCallback<ChangeEvent<string>>(OnTextChanged);
        }

        // Forces the next Apply() to fully re-derive even when nothing this manipulator's own signature
        // tracks has changed — mirrors StyleGridManipulator.UpdateSpec / StyleGapManipulator.UpdateGap.
        // FiberNodePatcher.ApplyTextBalanceManipulator calls this on an already-attached manipulator every
        // patch the text-balance class remains present, so a co-present max-w-* utility's inline write —
        // applied earlier in the SAME patch by DiffClassList / ApplyClassNames — is always re-overwritten
        // before the patch ends, instead of silently winning the shared maxWidth slot until an unrelated
        // geometry event happens to fire.
        public void Refresh()
        {
            _hasSignature = false;
            Apply();
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            _hasSignature = false;
            Apply();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => Apply();

        private void OnTextChanged(ChangeEvent<string> evt) => Apply();

        // Re-fires Apply() when the PARENT's own resolved rect changes — the half of ancestor-resize
        // staleness the target's own GeometryChangedEvent cannot see once a narrower inline maxWidth pins
        // the target's size (see the class doc's ancestor-resize discussion).
        private void OnParentGeometryChanged(GeometryChangedEvent evt) => Apply();

        // Keeps the parent subscription in sync with the CURRENT hierarchy parent. Called at the top of
        // every Apply() — not only from AttachToPanelEvent — so a mid-life reparent is picked up by
        // whichever event happens to fire next, without needing to know whether UI Toolkit raises
        // Attach/Detach for a same-panel reparent. Cheap no-op when the parent has not changed.
        private void SyncParentSubscription(VisualElement? parent)
        {
            if (ReferenceEquals(parent, _subscribedParent))
            {
                return;
            }
            _subscribedParent?.UnregisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
            _subscribedParent = parent;
            _subscribedParent?.RegisterCallback<GeometryChangedEvent>(OnParentGeometryChanged);
        }

        // Recomputes the balanced width from the current text / available width / font size, early-out
        // when nothing relevant to the last application has changed.
        private void Apply()
        {
            if (target is not TextElement textElement)
            {
                return;
            }

            var parent = textElement.parent;
            // Re-sync BEFORE the parent-null early-out (not just from AttachToPanelEvent) so every Apply()
            // call — from whichever event triggered it — keeps the subscription pointed at the CURRENT
            // parent. See the class doc's ancestor-resize discussion.
            SyncParentSubscription(parent);
            if (parent == null)
            {
                // Off-panel / detached: nothing meaningful to measure against yet. Defer to the next
                // AttachToPanelEvent / GeometryChangedEvent, mirroring StyleGridManipulator's off-panel
                // deferral (it re-arms _hasSignature so a later resolve is never skipped as a false repeat).
                _hasSignature = false;
                return;
            }

            // The PARENT's content width is never written by this manipulator (only the target's OWN
            // maxWidth is), so — unlike target.contentRect.width — it cannot be self-contaminated by a
            // previous pass's narrower write. See the available-width discussion in the class doc comment.
            var container = FiberNodePatcher.GetChildContainer(parent);
            var available = container.contentRect.width
                - textElement.resolvedStyle.marginLeft - textElement.resolvedStyle.marginRight;
            var hasWidth = available > 0f && !float.IsNaN(available);
            if (!hasWidth)
            {
                _hasSignature = false;
                return;
            }

            var text = textElement.text ?? string.Empty;
            var fontSize = textElement.resolvedStyle.fontSize;
            var signature = ComputeSignature(available, text, fontSize);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                ClearMaxWidth(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            // The text's own preferred size with no width constraint at all — the single-line reference
            // (or however many hard line breaks it already carries, but no SOFT wrap on top of those).
            var singleLineHeight = textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).y;
            // The height a normal (unbalanced) layout would take at the full available width.
            var naturalHeight = textElement.MeasureTextSize(
                text, available, VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

            if (naturalHeight <= 0f || float.IsNaN(naturalHeight))
            {
                // Degenerate measurement (e.g. the font has not resolved yet): return WITHOUT recording
                // _lastSignature/_hasSignature for this signature. Caching a verdict derived from a
                // degenerate height would make a later, valid measurement with the same available
                // width/text/font-size early-out forever at the top-of-method signature check — the
                // signature alone cannot distinguish "genuinely unchanged" from "the font resolved since".
                // Leaving _hasSignature as-is lets the next triggering event retry from scratch.
                return;
            }

            if (naturalHeight <= singleLineHeight + HeightEpsilonPx)
            {
                // Single line at the available width (includes an element whose resolved white-space is
                // nowrap, since MeasureTextSize already measures against the element's own resolved
                // style): CSS balance is a visual no-op here, and our box-narrowing approximation would
                // only shrink the box for no parity benefit, so leave it alone.
                ClearMaxWidth(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            var minWidth = Mathf.Max(1f, available * MinWidthFraction);
            var narrowest = FindNarrowestWidth(textElement, text, minWidth, available, naturalHeight);
            textElement.style.maxWidth = new StyleLength(narrowest);

            _lastSignature = signature;
            _hasSignature = true;
        }

        // Binary search over [lo, hi] for the narrowest width whose measured height still fits under
        // naturalHeight. hi (the available width) is always feasible by construction (its own measured
        // height IS naturalHeight), so it is a safe fallback if the loop's precision never beats it.
        private static float FindNarrowestWidth(
            TextElement textElement, string text, float lo, float hi, float naturalHeight)
        {
            var best = hi;
            for (var i = 0; i < MaxIterations; i++)
            {
                var mid = (lo + hi) * 0.5f;
                var height = textElement.MeasureTextSize(
                    text, mid, VisualElement.MeasureMode.Exactly,
                    float.NaN, VisualElement.MeasureMode.Undefined).y;
                if (height <= naturalHeight + HeightEpsilonPx)
                {
                    // Still fits the natural line count at this narrower width: it is feasible, so record
                    // it and keep searching narrower. A too-narrow candidate instead measures TALLER (an
                    // extra wrapped line) and falls into the else branch, which the search rejects.
                    best = mid;
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            return best;
        }

        private static void ClearMaxWidth(TextElement textElement)
        {
            textElement.style.maxWidth = new StyleLength(StyleKeyword.Null);
        }

        // Clears the inline maxWidth this manipulator wrote and drops the parent subscription (invoked on
        // detach / removal). Restoring a co-present max-w-* utility's own value, when this is a class
        // removal rather than a full unmount, is the CALLER's job (FiberNodePatcher.ReapplyArbitraryValues
        // — see the class doc's ownership paragraph): this manipulator has no access to the element's
        // current class list, only FiberNodePatcher does.
        private void Clear()
        {
            if (target is TextElement textElement)
            {
                ClearMaxWidth(textElement);
            }
            SyncParentSubscription(null);
            _hasSignature = false;
        }

        // A hash of the inputs that change the balanced width: the available width (rounded to skip float
        // jitter), the full text (a length-only signature would miss a same-length text swap), and the
        // resolved font size (a size change re-wraps the same text differently).
        private static int ComputeSignature(float available, string text, float fontSize)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(available);
                hash = hash * 31 + text.GetHashCode();
                hash = hash * 31 + fontSize.GetHashCode();
                return hash;
            }
        }
    }
}
