using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Approximates CSS `text-wrap: balance` on a TextElement carrying `text-balance`. UI Toolkit's text
    // engine exposes no line-break hook, so rather than moving line breaks inside a fixed box this narrows
    // the box: a bounded binary search over TextElement.MeasureTextSize — the same method the engine's own
    // measure pass calls — for the narrowest inline `width` whose measured height still matches the height
    // a normal layout takes at the available width. Font metrics are constant across candidates, so
    // comparing heights stands in for comparing line counts. Resizing the box at all is a deviation from
    // CSS; see Documentation~/fonts.md.
    //
    // `width` rather than `maxWidth`, for two engine facts: the engine clamps a written width to the
    // element's own max-width, so a declared ceiling holds whatever this manipulator computes; and
    // resolvedStyle.maxWidth then reports the cascade instead of this manipulator's own output, which is
    // what makes the ceiling readable at all.
    //
    // Available width comes from the PARENT's contentRect, through FiberNodePatcher.GetChildContainer,
    // which follows UI Toolkit's own contentContainer redirect and so answers with a composite widget's
    // inner box. For a RECONCILED child that redirect has already happened — the reconciler adds into the
    // inner box, so the element's parent IS it — which makes the call idempotent here and the element this
    // subscribes to the same one it measures. The target's own contentRect is never the source: that is
    // this manipulator's output.
    //
    // A hug-width parent defeats that indirection, since the parent's width follows the target's: the
    // search input then narrows every pass. With a fixed ceiling it converges. With a PERCENTAGE ceiling
    // it oscillates instead — the bound decays to a release, the released box re-widens the parent, and
    // the next pass starts over — so that combination needs a parent with a definite width.
    //
    // Balance stands down entirely when something else owns the box: a declared width, releasing the slot
    // back to it, or a grid parent, whose StyleGridManipulator writes this same child.style.width and is
    // left to hold it. Both are re-checked per derive rather than delivered per patch, because a variant
    // payload lands on the element outside any patch of its own.
    //
    // Ownership of the inline width lasts only while a balanced value sits in it. Every release re-resolves
    // the slot from the arbitrary-value layer map, which matters for a w-[..] applied in the same patch
    // that ends the ownership; a USS-spelled width needs nothing, since clearing the inline value reveals
    // the class again.
    //
    // Single-line gate: CSS balance is a no-op on one line, and narrowing a single-line box would shrink it
    // for no parity benefit. Measured at the width the text actually gets — ceiling-clamped, less the
    // element's own frame — so text that fits the parent but wraps inside either still balances. A nowrap element reaches the same verdict through the same
    // comparison, since MeasureTextSize honors the element's own resolved white-space.
    //
    // Prerequisite: Velvet's Label ships no base white-space rule, so its engine default is nowrap and
    // `text-balance` alone is a silent no-op — it needs `text-wrap` / `whitespace-normal` alongside.
    //
    // Re-derives on attach, on its own and its PARENT's GeometryChangedEvent, and on ChangeEvent<string>
    // (a text swap that keeps the same box size raises no geometry event). The parent subscription is what
    // catches an ancestor WIDENING: the written width pins the target's own rect, so nothing fires on the
    // target. A signature over the clamped content width, the frame around it, the text and the font size
    // absorbs the GeometryChangedEvent this manipulator's own write provokes.
    //
    // Lifecycle mirrors StyleGapManipulator / StyleGridManipulator: the reconciler attaches one per
    // element, tracks it in ReconcilerContext.TextBalanceManipulators, and removes it on cleanup. Detach
    // clears the inline width and the reconciler restores a co-present w-* right after; a full unmount
    // does not, since the element's layer record is dropped and FiberElementPoolReset nulls width anyway.
    internal sealed class StyleTextBalanceManipulator : Manipulator
    {
        private const int MaxIterations = 8;

        // Search floor as a fraction of the available width. Bounds the range only — a too-narrow
        // candidate measures taller and is rejected anyway, so it need not equal the longest word.
        private const float MinWidthFraction = 0.1f;

        // Absorbs float rounding so an unchanged wrap outcome does not misregister as "one line taller",
        // mirroring StyleGridManipulator's WrapSafetyPx.
        private const float HeightEpsilonPx = 0.5f;

        // Content room at or below this leaves nothing to redistribute, and keeps the search from being
        // entered with a floor above its own upper bound. A frame wider than the ceiling reaches it too,
        // which is why the released box can be far wider than this value.
        private const float MinBalanceableWidthPx = 1f;

        // Answers whether the target's parent is a grid container, whose manipulator writes the same slot.
        private readonly ReconcilerContext _ctx;

        private int _lastSignature;
        private bool _hasSignature;

        // Tracked so the callback can be unregistered from the exact element it was registered on.
        private VisualElement? _subscribedParent;

        internal StyleTextBalanceManipulator(ReconcilerContext ctx) => _ctx = ctx;

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

        // Forces a full re-derive, mirroring StyleGridManipulator.UpdateSpec / StyleGapManipulator.UpdateGap.
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

        private void OnParentGeometryChanged(GeometryChangedEvent evt) => Apply();

        // Re-pointed from every Apply, not only from AttachToPanelEvent, so a mid-life reparent is caught
        // without depending on how UI Toolkit sequences Attach/Detach for a same-panel reparent.
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

        private void Apply()
        {
            if (target is not TextElement textElement)
            {
                return;
            }

            var parent = textElement.parent;
            SyncParentSubscription(parent);
            if (parent == null)
            {
                // Re-arms so a later resolve is never skipped as a false repeat, mirroring
                // StyleGridManipulator's off-panel deferral.
                _hasSignature = false;
                return;
            }

            if (StyleTextBalanceClass.DeclaresWidthLayer(textElement))
            {
                // In FRONT of the signature guard: two dictionary lookups, no allocation. This is what
                // sees an inline-resolved variant width (md:w-[200px]) on both edges — its layer flips
                // without moving anything else this manipulator watches, and the release below re-arms the
                // signature so the OFF edge re-balances.
                ReleaseWidth(textElement);
                _hasSignature = false;
                return;
            }

            var container = FiberNodePatcher.GetChildContainer(parent);
            var available = container.contentRect.width
                - textElement.resolvedStyle.marginLeft - textElement.resolvedStyle.marginRight;
            var hasWidth = available > 0f && !float.IsNaN(available);
            if (!hasWidth)
            {
                _hasSignature = false;
                return;
            }

            // Clamped before the signature is computed, so a ceiling change that can alter the outcome
            // re-derives and one that cannot leaves the signature untouched.
            if (TryGetDeclaredCeiling(textElement, out var ceiling))
            {
                available = Mathf.Min(available, ceiling);
            }

            // The search measures text, which is laid out inside the element's content box, while the value
            // it writes is a `width` — and a width in UI Toolkit covers the padding and the border. So the
            // two are separated here: everything below searches over content widths, and the frame is added
            // back at the write. Measuring at the outer width instead hands the text less room than the
            // measurement assumed and it wraps one line further than the search settled on.
            var frame = textElement.resolvedStyle.paddingLeft + textElement.resolvedStyle.paddingRight
                        + textElement.resolvedStyle.borderLeftWidth
                        + textElement.resolvedStyle.borderRightWidth;
            var content = available - frame;

            var text = textElement.text ?? string.Empty;
            var fontSize = textElement.resolvedStyle.fontSize;
            var signature = ComputeSignature(content, frame, text, fontSize);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Behind the signature guard because the class walk below allocates; see DeclaresWidthClass
            // for which paths deliver a change and the one that does not.
            if (IsSizedByGridParent(parent))
            {
                // Left untouched rather than released: the grid writes this same slot with no layer behind
                // it, so clearing it would destroy the column width rather than a value of ours. The grid
                // cannot repair a write of ours either — its own re-derive is gated on the container's
                // contentRect WIDTH, which a child re-wrapping does not move.
                _hasSignature = false;
                return;
            }

            if (StyleTextBalanceClass.DeclaresWidthClass(textElement))
            {
                // Released rather than skipped, so a declaration arriving while a balanced value is held
                // takes effect at once.
                ReleaseWidth(textElement);
                _hasSignature = false;
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                ReleaseWidth(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            if (content < MinBalanceableWidthPx)
            {
                // Nothing to search over. Whatever the cascade then gives the box is the right answer here.
                ReleaseWidth(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            // Unconstrained: the single-line reference, carrying any hard line breaks but no soft wrap.
            var singleLineHeight = textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).y;
            var naturalHeight = textElement.MeasureTextSize(
                text, content, VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;

            if (naturalHeight <= 0f || float.IsNaN(naturalHeight))
            {
                // The font has not resolved yet. Recording this signature would make the later, valid
                // measurement early-out forever, since the signature cannot tell "unchanged" from "the
                // font resolved since".
                return;
            }

            if (naturalHeight <= singleLineHeight + HeightEpsilonPx)
            {
                ReleaseWidth(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            var minWidth = Mathf.Max(1f, content * MinWidthFraction);
            var narrowest = FindNarrowestWidth(textElement, text, minWidth, content, naturalHeight);
            textElement.style.width = new StyleLength(narrowest + frame);

            _lastSignature = signature;
            _hasSignature = true;
        }

        // hi is feasible by construction — its own measured height IS naturalHeight — so it is a safe
        // fallback when the loop's precision never beats it.
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

        private static void ClearWidth(TextElement textElement)
        {
            textElement.style.width = new StyleLength(StyleKeyword.Null);
        }

        // The null covers an element with no arbitrary layers at all, for which the re-assert early-returns;
        // the re-assert covers a w-[..] or size-[..] whose inline write the null would otherwise take with
        // it, including one a variant registered.
        private static void ReleaseWidth(TextElement textElement)
        {
            ClearWidth(textElement);
            StyleArbitraryValueResolver.ReapplyWidthSlot(textElement);
        }

        // Uncontaminated because this manipulator writes `width`, so every spelling — bracket, variant,
        // USS scale, percentage — reads back here. An absent max-width reports the None keyword while a
        // declared zero reports a value, so the two never blur; Auto is a third keyword no max-width
        // utility can currently produce, and would read as a ceiling of whatever value accompanies it.
        private static bool TryGetDeclaredCeiling(VisualElement element, out float ceilingPx)
        {
            var declared = element.resolvedStyle.maxWidth;
            ceilingPx = declared.value;
            return declared.keyword != StyleKeyword.None;
        }

        // StyleGridManipulator writes its children's own style.width. Asks the registry of attached grid
        // manipulators rather than re-deriving the grid's class condition, so the two cannot drift apart.
        // Walks ancestors because the grid sizes the children of GetChildContainer(target), and on any
        // widget carrying a contentContainer redirect — ScrollView, Foldout, TabView, … — that inner box
        // sits below the element the manipulator is keyed on; the match is that container being this
        // element's own parent, so no unrelated ancestor grid can claim it.
        private bool IsSizedByGridParent(VisualElement parent)
        {
            if (_ctx.GridManipulators.Count == 0)
            {
                return false;
            }
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (_ctx.GridManipulators.ContainsKey(ancestor)
                    && ReferenceEquals(FiberNodePatcher.GetChildContainer(ancestor), parent))
                {
                    return true;
                }
            }
            return false;
        }

        // Stops at the clear: only the caller can tell a class removal, which owes the element its
        // co-present w-* back, from an unmount, whose layer record is dropped moments later.
        private void Clear()
        {
            if (target is TextElement textElement)
            {
                ClearWidth(textElement);
            }
            SyncParentSubscription(null);
            _hasSignature = false;
        }

        // The available width is already ceiling-clamped, so a ceiling change that can alter the outcome
        // changes the signature. The full text rather than its length, which would miss a same-length swap.
        // The frame is its own term rather than folded into the content width: a padding change that a
        // container width change cancels out leaves the same content width and a different value to write.
        private static int ComputeSignature(float content, float frame, string text, float fontSize)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(content);
                hash = hash * 31 + Mathf.RoundToInt(frame);
                hash = hash * 31 + text.GetHashCode();
                hash = hash * 31 + fontSize.GetHashCode();
                return hash;
            }
        }
    }
}
