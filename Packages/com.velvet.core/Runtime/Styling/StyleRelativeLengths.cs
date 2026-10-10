using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What CSS takes a percentage of for one longhand, and what an em is.
    internal enum RelativeLengthAxis : byte
    {
        // The width of the parent's content box.
        Width,
        // The height of the parent's content box.
        Height,
        // The parent's content box along the parent's flex direction: flex-basis.
        MainAxis,
        // font-size: an em and a percentage are both the parent's font size.
        ParentFont,
        // letter-spacing: a percentage is the element's own font size, as an em is.
        OwnFont,
    }

    // Measures a StyleLengthExpression on the element it is written to. An em is the element's own font size
    // except on font-size, the viewport is the panel (an editor window's content, in an editor panel), and a
    // percentage is taken of the box of the parent the element is laid out in: a clip wrapper's parent for a
    // clipped element, whichever of the two boxes the value is written to (ClipPathLayoutBox). The box is the
    // content box, and the padding box for an absolute element; RelativeLengthPanelTests holds both to the
    // ones UI Toolkit takes its own percentages of.
    internal static class StyleRelativeLengths
    {
        private static readonly StyleList<TimeValue> s_noTime = new(new List<TimeValue> { new(0f) });

        // What expression comes to on element, or false while the element is on no panel or has not been laid
        // out (RelativeLengthPanelTests' before-any-frame cases); the caller then leaves the longhand as it is. A
        // percentage of an indefinite size comes to indefinite, the longhand's stand-in for one, or, where the
        // longhand has none (PropertySetters), is taken as zero beside the expression's other terms.
        public static bool TryResolve(VisualElement element, StyleLengthExpression expression, RelativeLengthAxis axis,
            StyleKeyword? indefinite, out StyleLength length)
        {
            length = default;
            var outer = ClipPathLayoutBox.Of(element);
            if (element.panel == null || float.IsNaN(element.layout.width))
            {
                return false;
            }
            // On a panel, only its root has no parent, and Velvet writes no length to it.
            var parent = outer.hierarchy.parent!;
            var em = axis == RelativeLengthAxis.ParentFont ? parent.resolvedStyle.fontSize : element.resolvedStyle.fontSize;
            // Null on the two font axes, whose percentage is the em.
            bool? vertical = axis == RelativeLengthAxis.Width ? false
                : axis == RelativeLengthAxis.Height ? true
                : axis == RelativeLengthAxis.MainAxis ? !IsRow(parent)
                : null;
            // An absolute box's containing block has been laid out by the time it is.
            var definite = vertical == null || !expression.ReadsPercentage
                || outer.resolvedStyle.position == Position.Absolute || IsDefinite(parent, vertical.Value);
            if (!definite && indefinite is { } keyword)
            {
                length = keyword;
                return true;
            }
            var box = PercentBox(parent, outer.resolvedStyle.position == Position.Absolute);
            var percent = !definite ? 0f : vertical is { } across ? (across ? box.y : box.x) : em;
            var viewport = ViewportOf(element);
            var px = expression.Evaluate(new RelativeLengthBasis(em, percent, viewport.width, viewport.height));
            length = new StyleLength(new Length(px, LengthUnit.Pixel));
            return float.IsFinite(px);
        }

        private static UnityEngine.Vector2 PercentBox(VisualElement parent, bool absolute)
        {
            var content = parent.contentRect.size;
            var style = parent.resolvedStyle;
            return absolute
                ? content + new UnityEngine.Vector2(style.paddingLeft + style.paddingRight, style.paddingTop + style.paddingBottom)
                : content;
        }

        // An editor window places its root on the panel absolutely, inset by the window's tab and borders, so in
        // an editor panel that root is the viewport (ViewportWindowTests); content placed on the panel some other
        // way, and every runtime panel, takes the panel itself.
        private static UnityEngine.Rect ViewportOf(VisualElement element)
        {
            var root = element.panel.visualTree;
            for (var top = element; ; top = top.hierarchy.parent!)
            {
                if (top.hierarchy.parent == root)
                {
                    return element.panel.contextType == ContextType.Editor && top.resolvedStyle.position == Position.Absolute
                        ? top.layout
                        : root.layout;
                }
            }
        }

        // Whether box's size along the axis is one a percentage can be taken of — CSS's definite size. Read from
        // what is declared rather than from the layout, which is what a percentage of an indefinite size would
        // feed back into.
        private static bool IsDefinite(VisualElement box, bool vertical)
        {
            var parent = box.hierarchy.parent;
            // The panel root, and a document root the panel sizes.
            if (parent?.hierarchy.parent == null)
            {
                return true;
            }
            var size = vertical
                ? DeclaredKind(box, StyleLonghand.Height, box.style.height)
                : DeclaredKind(box, StyleLonghand.Width, box.style.width);
            var style = box.resolvedStyle;
            // A percentage of an absolute box's containing block is definite, as that block is.
            if (size != Declared.Nothing && size != Declared.Keyword)
            {
                return size == Declared.Length || style.position == Position.Absolute || IsDefinite(parent, vertical);
            }
            // An absolute box is sized by its content unless an inset on each side pins it to its containing block,
            // which is always definite. Stretched across a parent's cross axis, or grown along its main
            // axis, a box in flow is as definite as the parent.
            if (style.position == Position.Absolute)
            {
                return vertical
                    ? Pins(box, StyleLonghand.Top, box.style.top) && Pins(box, StyleLonghand.Bottom, box.style.bottom)
                    : Pins(box, StyleLonghand.Left, box.style.left) && Pins(box, StyleLonghand.Right, box.style.right);
            }
            var sized = IsRow(parent) == vertical
                ? (style.alignSelf == Align.Auto ? parent.resolvedStyle.alignItems : style.alignSelf) == Align.Stretch
                : style.flexGrow > 0f;
            return sized && IsDefinite(parent, vertical);
        }

        private static bool Pins(VisualElement box, StyleLonghand inset, StyleLength inline)
            => DeclaredKind(box, inset, inline) is Declared.Length or Declared.Percentage;

        private enum Declared : byte
        {
            Nothing,
            Length,
            Percentage,
            Keyword,
        }

        // What the box itself declares for longhand: the inline value, else the bundled class latest in the cascade
        // among those writing it, whose value's kind the stylesheets give (StyleUtilityRule). A variant reaches
        // this through the class list, which StyleClassProjection rewrites while the variant is on
        // (RelativeLengthPanelTests' hover case). A rule gated on a pseudo-state would count while the state is
        // off as well; BundledStyleSheetCensusTests fails when a bundled one first writes a size or an inset. The
        // class list is indexed rather than enumerated, whose boxed enumerator would allocate on every poll tick
        // for every ancestor walked (RelativeLengthPanelTests' allocation case).
        private static Declared DeclaredKind(VisualElement box, StyleLonghand longhand, StyleLength inline)
        {
            if (inline.keyword != StyleKeyword.Null)
            {
                return inline.keyword != StyleKeyword.Undefined ? Declared.Keyword
                    : inline.value.unit == LengthUnit.Percent ? Declared.Percentage
                    : Declared.Length;
            }
            var classes = box.GetClasses() as IList<string> ?? new List<string>(box.GetClasses());
            var (kind, position) = (Declared.Nothing, -1);
            for (var i = 0; i < classes.Count; i++)
            {
                // MUTANT_SURVIVES(equivalent, boundary): a tie needs two classes at one cascade position, and the table gives each class a position of its own (RelativeLengthDefinitenessTests' distinct-positions case).
                if (StyleUtilityProperties.TryGet(classes[i], out var rule) && rule.Properties.Contains(longhand)
                    && rule.CascadePosition > position)
                {
                    position = rule.CascadePosition;
                    kind = rule.Percentages.Contains(longhand) ? Declared.Percentage
                        : rule.Keywords.Contains(longhand) ? Declared.Keyword
                        : Declared.Length;
                }
            }
            return kind;
        }

        private static bool IsRow(VisualElement element)
            => element.resolvedStyle.flexDirection is FlexDirection.Row or FlexDirection.RowReverse;

        // Holds the transitions of an element and of the box it is laid out as at zero duration and delay until
        // Release, so a re-measure lands rather than animating. GeneralPathReconciler.RestorePopLayoutChildToFlow
        // suspends the same way; zero rather than Null, so a transition a class declares is held as well as an
        // inline one. The saved lists are copies, so nothing the engine does to the list an inline value was read
        // from reaches what is restored (RelativeLengthPanelTests' inline-duration case).
        internal readonly struct TransitionHold
        {
            private readonly IStyle _inner;
            private readonly IStyle _outer;
            private readonly (StyleList<TimeValue> Duration, StyleList<TimeValue> Delay) _innerSaved;
            private readonly (StyleList<TimeValue> Duration, StyleList<TimeValue> Delay) _outerSaved;

            public TransitionHold(VisualElement element)
            {
                (_inner, _outer) = (element.style, ClipPathLayoutBox.Of(element).style);
                _innerSaved = (MotionTweenTiming.Copy(_inner.transitionDuration), MotionTweenTiming.Copy(_inner.transitionDelay));
                _outerSaved = (MotionTweenTiming.Copy(_outer.transitionDuration), MotionTweenTiming.Copy(_outer.transitionDelay));
                (_outer.transitionDuration, _outer.transitionDelay) = (s_noTime, s_noTime);
                (_inner.transitionDuration, _inner.transitionDelay) = (s_noTime, s_noTime);
            }

            public void Release()
            {
                (_inner.transitionDuration, _inner.transitionDelay) = _innerSaved;
                (_outer.transitionDuration, _outer.transitionDelay) = _outerSaved;
            }
        }

        // Everything TryResolve reads for element, whichever longhand and axis it is asked about.
        internal static Snapshot Capture(VisualElement element)
        {
            var outer = ClipPathLayoutBox.Of(element);
            var parent = outer.hierarchy.parent;
            return new Snapshot(element.resolvedStyle.fontSize, outer.resolvedStyle.position,
                parent == null ? default : Read(parent), element.panel == null ? default : ViewportOf(element).size,
                float.IsNaN(element.layout.width));
        }

        private static ParentReading Read(VisualElement parent)
        {
            var style = parent.resolvedStyle;
            return new ParentReading(style.fontSize, parent.layout.size,
                new UnityEngine.Vector4(style.borderLeftWidth, style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth),
                new UnityEngine.Vector4(style.paddingLeft, style.paddingTop, style.paddingRight, style.paddingBottom),
                IsRow(parent), (IsDefinite(parent, false), IsDefinite(parent, true)));
        }

        internal readonly record struct ParentReading(float Font, UnityEngine.Vector2 Size, UnityEngine.Vector4 Border,
            UnityEngine.Vector4 Padding, bool Row, (bool Width, bool Height) Definite);

        internal readonly record struct Snapshot(float OwnFont, Position Position, ParentReading Parent,
            UnityEngine.Vector2 Viewport, bool Unlaid);
    }

    // Re-resolves an element's relative lengths on the panel's scheduler tick after anything they measure has
    // moved, comparing a Capture each tick. Rejected: LeadingLengthProbe's style-resolved event, which needs the
    // bundled sheet on the panel, and would still leave the parent's and the panel's sizes to listen for.
    internal sealed class RelativeLengthPoll
    {
        // The tick StyleAnimateDriver and AnchoredDriver poll at.
        private const long TickMs = 16;

        private readonly VisualElement _element;
        private readonly IVisualElementScheduledItem _item;
        private StyleRelativeLengths.Snapshot? _last;

        public RelativeLengthPoll(VisualElement element, Action<VisualElement> reresolve)
        {
            _element = element;
            _item = element.schedule.Execute(() => reresolve(element)).Every(TickMs);
        }

        // Whether what the element's relative lengths measure has changed since the last call; true on the first.
        public bool Moved()
        {
            // The last capture, compared before this one replaces it.
            return _last != (_last = StyleRelativeLengths.Capture(_element));
        }

        public void Stop() => _item.Pause();
    }
}
