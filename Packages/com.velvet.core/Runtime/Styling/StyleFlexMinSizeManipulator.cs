using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The automatic minimum size CSS gives a flex item (CSS Flexbox §4.5), written inline along the parent's
    // main axis: the item's min-content size, capped by its definite preferred size on that axis, then by its
    // maximum size. For text, min-content is the widest unbreakable run in a row (the whole text once
    // white-space forbids soft wrapping) and the wrapped height in a column. Re-derive triggers and the
    // parent subscription are StyleTextItemManipulator's; the documented behaviour and its narrowings are in
    // Documentation~/styling-flexbox-and-gap.md.
    //
    // The spec gives the automatic minimum as zero for a scroll container, and a grid item in a
    // grid-cols-N track is sized by minmax(0, 1fr), so those stand down, as does an item that declares its
    // own min-width / min-height on the axis. A width or height declared as w-fit, w-min, w-max or w-auto is
    // a content keyword, not a definite size, and caps nothing.
    //
    // Declarations are read from what Velvet owns: the live class list (scanned when the reconciled classes
    // change), the arbitrary-value layers, and the inline style; the resolved style is not consulted.
    // TextItemBaselinePanelTests pins that a declaring class patched away is read at once.
    //
    // Ownership of the inline slot lasts only while a value this wrote sits in it: a bracket layer that
    // arrives for the same slot has already overwritten it and is never cleared by this.
    internal sealed class StyleFlexMinSizeManipulator : StyleTextItemManipulator
    {
        private const int MaxMeasuredRuns = 64;

        private const string MinWidthPrefix = "min-w-";
        private const string MinHeightPrefix = "min-h-";
        private const string MaxWidthPrefix = "max-w-";
        private const string MaxHeightPrefix = "max-h-";
        private const string WidthPrefix = "w-";
        private const string HeightPrefix = "h-";
        private const string SizePrefix = "size-";
        private const string OverflowHiddenClass = "overflow-hidden";
        private const string TruncateClass = "truncate";

        // Reused across derives: manipulators run on the main thread, one derive at a time.
        private static readonly List<string> s_runs = new();
        private static readonly HashSet<string> s_seen = new();
        private static readonly List<string> s_distinct = new();

        private enum Owned
        {
            None,
            Width,
            Height,
        }

        // A length a class declares: pixels, or a percentage of the parent's content box.
        private readonly struct Dim
        {
            public Dim(float value, bool percent)
            {
                Value = value;
                Percent = percent;
            }

            public float Value { get; }

            public bool Percent { get; }
        }

        private Owned _owned;

        // The last value this wrote and the axis it wrote it on; kept past a release so a later write of the
        // same value is skipped, and so a value on the slot that is not this one's reads as someone else's.
        private Owned _lastWrittenAxis;
        private float _lastWritten;

        private string[]? _lastClassNames;

        // What the live class list declares, rebuilt by Scan when _scanStale.
        private bool _scanStale = true;
        private bool _clipped;
        private bool _declaresMinWidth;
        private bool _declaresMinHeight;
        private Dim? _specifiedWidth;
        private Dim? _specifiedHeight;
        private Dim? _maxWidth;
        private Dim? _maxHeight;

        internal StyleFlexMinSizeManipulator(ReconcilerContext ctx, string[] classNames)
            : base(ctx)
        {
            _lastClassNames = classNames;
        }

        // Re-derives after a patch. A patch can change what the classes declare without moving anything the
        // signature reads, so a changed reconciled array rescans; an unchanged one keeps the guard. A variant
        // payload that lights one of the scanned classes outside a patch is read at the next patch.
        public void Refresh(string[] classNames)
        {
            if (_lastClassNames == null || !System.MemoryExtensions.SequenceEqual(
                    new System.ReadOnlySpan<string>(classNames), new System.ReadOnlySpan<string>(_lastClassNames)))
            {
                _scanStale = true;
            }
            _lastClassNames = classNames;
            Apply();
        }

        protected override void Derive(TextElement textElement, VisualElement parent)
        {
            if (_scanStale)
            {
                Scan(textElement);
                _scanStale = false;
                _hasSignature = false;
            }

            var horizontal = parent.resolvedStyle.flexDirection is FlexDirection.Row or FlexDirection.RowReverse;
            if (StyleArbitraryValueResolver.HasLayer(
                    textElement, horizontal ? ArbitraryProperty.MinWidth : ArbitraryProperty.MinHeight))
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

            var parentContent = horizontal ? parent.contentRect.width : parent.contentRect.height;
            var specified = ResolveLength(
                horizontal ? textElement.style.width : textElement.style.height,
                horizontal ? _specifiedWidth : _specifiedHeight, parentContent);
            var maximum = ResolveLength(
                horizontal ? textElement.style.maxWidth : textElement.style.maxHeight,
                horizontal ? _maxWidth : _maxHeight, parentContent);
            var signature = ComputeSignature(
                horizontal, text, resolved.fontSize, resolved.whiteSpace, frame, crossContent,
                resolved.flexShrink > 0f, specified, maximum);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            if (StandsDown(textElement, parent, horizontal, resolved.flexShrink))
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

            var value = Mathf.Ceil(contentMinimum) + frame;
            if (specified.HasValue)
            {
                value = Mathf.Min(value, specified.Value);
            }
            if (maximum.HasValue)
            {
                value = Mathf.Min(value, maximum.Value);
            }
            Write(textElement, horizontal, Mathf.Max(0f, value));
            _lastSignature = signature;
            _hasSignature = true;
        }

        // An absolutely positioned or hidden element is not a flex item, so the automatic minimum does not
        // apply to it.
        private static bool IsInFlow(VisualElement element)
            => element.resolvedStyle.position != Position.Absolute
                && element.resolvedStyle.display != DisplayStyle.None;

        private bool StandsDown(TextElement textElement, VisualElement parent, bool horizontal, float flexShrink)
        {
            if (flexShrink <= 0f || _clipped || (horizontal ? _declaresMinWidth : _declaresMinHeight))
            {
                return true;
            }
            if (textElement.style.overflow.keyword == StyleKeyword.Undefined
                && textElement.style.overflow.value == Overflow.Hidden)
            {
                return true;
            }

            // A min-size written by something other than this: the value on the slot is not the last one
            // this wrote, or this has not written there.
            var inline = horizontal ? textElement.style.minWidth : textElement.style.minHeight;
            var axis = horizontal ? Owned.Width : Owned.Height;
            if (inline.keyword == StyleKeyword.Undefined
                && !(_lastWrittenAxis == axis && Mathf.Abs(inline.value.value - _lastWritten) < 0.01f))
            {
                return true;
            }
            return IsSizedByGridParent(parent);
        }

        private void Scan(TextElement textElement)
        {
            _clipped = false;
            _declaresMinWidth = false;
            _declaresMinHeight = false;
            _specifiedWidth = null;
            _specifiedHeight = null;
            _maxWidth = null;
            _maxHeight = null;
            foreach (var cls in textElement.GetClasses())
            {
                if (cls == OverflowHiddenClass || cls == TruncateClass)
                {
                    _clipped = true;
                }
                else if (cls.StartsWith(MinWidthPrefix, System.StringComparison.Ordinal))
                {
                    _declaresMinWidth = true;
                }
                else if (cls.StartsWith(MinHeightPrefix, System.StringComparison.Ordinal))
                {
                    _declaresMinHeight = true;
                }
                else
                {
                    ScanLength(cls);
                }
            }
        }

        private void ScanLength(string cls)
        {
            if (TryClassLength(cls, WidthPrefix, out var width))
            {
                _specifiedWidth = width;
            }
            else if (TryClassLength(cls, HeightPrefix, out var height))
            {
                _specifiedHeight = height;
            }
            else if (TryClassLength(cls, SizePrefix, out var size))
            {
                _specifiedWidth = size;
                _specifiedHeight = size;
            }
            else if (TryClassLength(cls, MaxWidthPrefix, out var maxWidth))
            {
                _maxWidth = maxWidth;
            }
            else if (TryClassLength(cls, MaxHeightPrefix, out var maxHeight))
            {
                _maxHeight = maxHeight;
            }
        }

        // Recognizes prefix + a spacing-scale step, `full`, or a keyword that declares no definite size
        // (auto, fit, min, max, none, screen): the last returns true with no length, so it clears an earlier
        // class's. Any other suffix is not a length class this reads.
        private static bool TryClassLength(string cls, string prefix, out Dim? length)
        {
            length = null;
            if (!cls.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return false;
            }
            var suffix = cls.Substring(prefix.Length);
            if (suffix == "full")
            {
                length = new Dim(100f, true);
                return true;
            }
            if (suffix is "auto" or "fit" or "min" or "max" or "none" or "screen")
            {
                return true;
            }
            if (StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out var px))
            {
                length = new Dim(px, false);
                return true;
            }
            return false;
        }

        // An inline value outranks a class; a keyword inline (auto, none) declares no definite length.
        private static float? ResolveLength(StyleLength inline, Dim? fromClass, float parentContent)
        {
            if (inline.keyword == StyleKeyword.Undefined)
            {
                var length = inline.value;
                return length.unit == LengthUnit.Percent
                    ? Percent(length.value, parentContent)
                    : length.value;
            }
            if (inline.keyword != StyleKeyword.Null || !fromClass.HasValue)
            {
                return null;
            }
            return fromClass.Value.Percent ? Percent(fromClass.Value.Value, parentContent) : fromClass.Value.Value;
        }

        private static float? Percent(float percent, float parentContent)
            => float.IsNaN(parentContent) ? null : parentContent * percent / 100f;

        // The whole text once white-space forbids soft wrapping; otherwise the widest unbreakable run, among
        // the MaxMeasuredRuns longest distinct ones (ties by first occurrence).
        private static float MeasureMinContentWidth(TextElement textElement, string text, WhiteSpace whiteSpace)
        {
            if (whiteSpace == WhiteSpace.NoWrap || whiteSpace == WhiteSpace.Pre)
            {
                return MeasureWidth(textElement, text);
            }

            s_runs.Clear();
            s_seen.Clear();
            s_distinct.Clear();
            TextBreakOpportunities.CollectRuns(text, s_runs);
            foreach (var run in s_runs)
            {
                if (s_seen.Add(run))
                {
                    s_distinct.Add(run);
                }
            }
            if (s_distinct.Count > MaxMeasuredRuns)
            {
                // List.Sort is unstable, so the first-occurrence order is the explicit tie-break.
                var order = new Dictionary<string, int>(s_distinct.Count);
                for (var i = 0; i < s_distinct.Count; i++)
                {
                    order[s_distinct[i]] = i;
                }
                s_distinct.Sort((a, b) => a.Length != b.Length ? b.Length.CompareTo(a.Length) : order[a].CompareTo(order[b]));
                s_distinct.RemoveRange(MaxMeasuredRuns, s_distinct.Count - MaxMeasuredRuns);
            }

            var widest = 0f;
            foreach (var run in s_distinct)
            {
                widest = Mathf.Max(widest, MeasureWidth(textElement, run));
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
            if (_owned == axis && Mathf.Approximately(_lastWritten, value))
            {
                return;
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
            bool horizontal, string text, float fontSize, WhiteSpace whiteSpace, float frame, float crossContent,
            bool canShrink, float? specified, float? maximum)
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
                hash = hash * 31 + (canShrink ? 1 : 0);
                hash = hash * 31 + (specified.HasValue ? Mathf.RoundToInt(specified.Value) + 1 : 0);
                hash = hash * 31 + (maximum.HasValue ? Mathf.RoundToInt(maximum.Value) + 1 : 0);
                return hash;
            }
        }
    }
}
