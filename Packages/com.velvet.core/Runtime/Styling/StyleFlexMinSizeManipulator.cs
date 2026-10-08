using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Emulates the automatic minimum size CSS gives a flex item (CSS Flexbox §4.5): along the parent's main
    // axis a text item cannot shrink below its content-based minimum size, which for text is its min-content
    // size — the widest unbreakable word in a row while it may wrap, the whole line once white-space forbids
    // soft wrapping, and the wrapped height in a column. UI Toolkit's initial min-width / min-height is `auto`,
    // so this writes the inline value CSS would have resolved it to.
    //
    // The spec gives the automatic minimum as zero for a scroll container, and caps the content size by a
    // definite preferred size on that axis. Both are honoured by standing down, not by computing: an item
    // that is clipped (`overflow-hidden`, `truncate`, an inline overflow of hidden) or that declares its own
    // main size or its own min-width / min-height keeps whatever its cascade gives it, since any of those is
    // the author taking the axis over. The stand-down for a declared main size is a narrowing — CSS would
    // still cap the minimum at that size rather than lift it.
    //
    // Measurement is TextElement.MeasureTextSize, the call StyleTextBalanceManipulator measures with, and the
    // frame (padding and border on the axis) is added back at the write for the reason that manipulator gives.
    // The widest word is searched among the MaxMeasuredWords longest distinct words by character count, so a
    // long paragraph costs a bounded number of measurements; a short word set in a much wider face than a
    // longer one is the case this can miss.
    //
    // Ownership of the inline slot lasts only while a value this wrote sits in it: a bracket layer that
    // arrives for the same slot has already overwritten it and is never cleared by this.
    //
    // Re-derives on attach, on its own and its parent's GeometryChangedEvent, and on ChangeEvent<string>.
    // A signature over the inputs absorbs the GeometryChangedEvent this manipulator's own write provokes.
    // Lifecycle mirrors StyleTextBalanceManipulator: the reconciler attaches one per text element, tracks it in
    // ReconcilerContext.FlexMinSizeManipulators, and removes it on cleanup.
    internal sealed class StyleFlexMinSizeManipulator : StyleTextItemManipulator
    {
        private const int MaxMeasuredWords = 16;

        private const string MinWidthPrefix = "min-w-";
        private const string MinHeightPrefix = "min-h-";
        private const string HeightPrefix = "h-";
        private const string SizePrefix = "size-";
        private const string AutoHeightClass = "h-auto";
        private const string OverflowHiddenClass = "overflow-hidden";
        private const string TruncateClass = "truncate";

        private static readonly char[] BreakChars = { ' ', '\t', '\n', '\r' };

        private enum Owned
        {
            None,
            Width,
            Height,
        }

        private Owned _owned;

        // The last value this wrote and the axis it wrote it on, kept past a release: the resolved style lags
        // an inline clear, so a reading equal to it is still this manipulator's own and not a declaration.
        private Owned _lastWrittenAxis;
        private float _lastWritten;

        private string[]? _lastClassNames;

        internal StyleFlexMinSizeManipulator(string[] classNames)
        {
            _lastClassNames = classNames;
        }

        // Re-derives after a patch. The class scan is behind the signature, so a patch that left the
        // reconciled classes as they were keeps the guard: a class change is the only thing a patch can do
        // that no signature term sees, and the text is a term. A variant payload that lights a min-w-,
        // h- or overflow class without a patch is therefore read at the element's next patch.
        public void Refresh(string[] classNames)
        {
            if (_lastClassNames == null || !System.MemoryExtensions.SequenceEqual(
                    new System.ReadOnlySpan<string>(classNames), new System.ReadOnlySpan<string>(_lastClassNames)))
            {
                _hasSignature = false;
            }
            _lastClassNames = classNames;
            Apply();
        }

        protected override void Derive(TextElement textElement, VisualElement parent)
        {
            var horizontal = parent.resolvedStyle.flexDirection is FlexDirection.Row or FlexDirection.RowReverse;
            var slot = horizontal ? ArbitraryProperty.MinWidth : ArbitraryProperty.MinHeight;
            if (StyleArbitraryValueResolver.HasLayer(textElement, slot))
            {
                // The layer's own write is already in the slot, over any value of ours.
                _owned = Owned.None;
                _hasSignature = false;
                return;
            }

            var text = textElement.text ?? string.Empty;
            if (text.Length == 0 || textElement.childCount > 0 || !IsInFlow(textElement))
            {
                Release(textElement);
                _hasSignature = false;
                return;
            }

            var resolved = textElement.resolvedStyle;
            var frame = horizontal
                ? resolved.paddingLeft + resolved.paddingRight + resolved.borderLeftWidth + resolved.borderRightWidth
                : resolved.paddingTop + resolved.paddingBottom + resolved.borderTopWidth + resolved.borderBottomWidth;
            var crossContent = horizontal
                ? 0f
                : resolved.width - resolved.paddingLeft - resolved.paddingRight
                    - resolved.borderLeftWidth - resolved.borderRightWidth;
            if (!horizontal && (float.IsNaN(crossContent) || crossContent <= 0f))
            {
                // No width to wrap against yet; the geometry event that supplies one re-enters here.
                _hasSignature = false;
                return;
            }

            var signature = ComputeSignature(horizontal, text, resolved.fontSize, resolved.whiteSpace, frame, crossContent);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Behind the signature guard because the class walk allocates; Refresh resets the guard for every
            // path that can change the answer.
            if (StandsDown(textElement, horizontal))
            {
                Release(textElement);
                _lastSignature = signature;
                _hasSignature = true;
                return;
            }

            var contentMinimum = horizontal
                ? MeasureMinContentWidth(textElement, text, resolved.whiteSpace)
                : textElement.MeasureTextSize(
                    text, crossContent, VisualElement.MeasureMode.Exactly,
                    float.NaN, VisualElement.MeasureMode.Undefined).y;
            if (contentMinimum <= 0f || float.IsNaN(contentMinimum))
            {
                // The font has not resolved yet; recording the signature would early-out the valid
                // measurement forever.
                return;
            }

            Write(textElement, horizontal, Mathf.Ceil(contentMinimum) + frame);
            _lastSignature = signature;
            _hasSignature = true;
        }

        // An absolutely positioned or hidden element is not a flex item, so the automatic minimum does not
        // apply to it.
        private static bool IsInFlow(VisualElement element)
            => element.resolvedStyle.position != Position.Absolute
                && element.resolvedStyle.display != DisplayStyle.None;

        private bool StandsDown(TextElement textElement, bool horizontal)
        {
            if (textElement.style.overflow.keyword == StyleKeyword.Undefined
                && textElement.style.overflow.value == Overflow.Hidden)
            {
                return true;
            }

            var sizeLayer = horizontal ? ArbitraryProperty.Width : ArbitraryProperty.Height;
            if (StyleArbitraryValueResolver.HasLayer(textElement, sizeLayer)
                || StyleArbitraryValueResolver.HasLayer(textElement, ArbitraryProperty.Size))
            {
                return true;
            }

            // A minimum another source declared (a class, a theme rule, an inline value) reads back as
            // something other than `auto`. While this owns the slot the reading includes its own value, so
            // only the class scan below can tell the author's from it.
            if (_owned == Owned.None)
            {
                var declared = horizontal ? textElement.resolvedStyle.minWidth : textElement.resolvedStyle.minHeight;
                var axis = horizontal ? Owned.Width : Owned.Height;
                var isOwnEcho = _lastWrittenAxis == axis
                    && declared.keyword == StyleKeyword.Undefined
                    && Mathf.Abs(declared.value - _lastWritten) < 0.5f;
                if (declared.keyword != StyleKeyword.Auto && !isOwnEcho)
                {
                    return true;
                }
            }

            foreach (var cls in textElement.GetClasses())
            {
                if (DeclaresOwnAxis(cls, horizontal))
                {
                    return true;
                }
            }
            return false;
        }

        // The class forms of the stand-downs: the bracket forms never enter the class list and are asked of
        // the layer map instead.
        private static bool DeclaresOwnAxis(string cls, bool horizontal)
        {
            if (cls == OverflowHiddenClass || cls == TruncateClass)
            {
                return true;
            }
            if (horizontal)
            {
                return cls.StartsWith(MinWidthPrefix, System.StringComparison.Ordinal)
                    || StyleTextBalanceClass.IsWidthDeclaringToken(cls);
            }
            return cls.StartsWith(MinHeightPrefix, System.StringComparison.Ordinal)
                || (cls != AutoHeightClass
                    && (cls.StartsWith(HeightPrefix, System.StringComparison.Ordinal)
                        || cls.StartsWith(SizePrefix, System.StringComparison.Ordinal)));
        }

        // The whole text once white-space forbids soft wrapping (nowrap, pre); otherwise the widest word,
        // searched among the longest distinct ones.
        private static float MeasureMinContentWidth(TextElement textElement, string text, WhiteSpace whiteSpace)
        {
            if (whiteSpace == WhiteSpace.NoWrap || whiteSpace == WhiteSpace.Pre)
            {
                return MeasureWidth(textElement, text);
            }

            var words = text.Split(BreakChars, System.StringSplitOptions.RemoveEmptyEntries);
            System.Array.Sort(words, (a, b) => b.Length.CompareTo(a.Length));
            var widest = 0f;
            string? previous = null;
            var measured = 0;
            foreach (var word in words)
            {
                if (word == previous)
                {
                    continue;
                }
                previous = word;
                widest = Mathf.Max(widest, MeasureWidth(textElement, word));
                if (++measured == MaxMeasuredWords)
                {
                    break;
                }
            }
            return widest;
        }

        private static float MeasureWidth(TextElement textElement, string text)
            => textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        private void Write(TextElement textElement, bool horizontal, float value)
        {
            var axis = horizontal ? Owned.Width : Owned.Height;
            if (_owned != Owned.None && _owned != axis)
            {
                Release(textElement);
            }
            if (horizontal)
            {
                textElement.style.minWidth = new StyleLength(value);
            }
            else
            {
                textElement.style.minHeight = new StyleLength(value);
            }
            _owned = axis;
            _lastWrittenAxis = axis;
            _lastWritten = value;
        }

        // Clears only a slot this wrote, so a value another source put there is never taken with it.
        private void Release(TextElement textElement)
        {
            switch (_owned)
            {
                case Owned.Width:
                    textElement.style.minWidth = new StyleLength(StyleKeyword.Null);
                    break;
                case Owned.Height:
                    textElement.style.minHeight = new StyleLength(StyleKeyword.Null);
                    break;
            }
            _owned = Owned.None;
        }

        protected override void Clear()
        {
            if (target is TextElement textElement)
            {
                Release(textElement);
            }
        }

        // The text in full rather than its length, which would miss a same-length swap. The white-space is a
        // term because a variant can switch the wrap mode without moving anything else here.
        private static int ComputeSignature(
            bool horizontal, string text, float fontSize, WhiteSpace whiteSpace, float frame, float crossContent)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (horizontal ? 1 : 0);
                hash = hash * 31 + text.GetHashCode();
                hash = hash * 31 + fontSize.GetHashCode();
                hash = hash * 31 + (int)whiteSpace;
                hash = hash * 31 + Mathf.RoundToInt(frame);
                hash = hash * 31 + Mathf.RoundToInt(crossContent);
                return hash;
            }
        }
    }
}
