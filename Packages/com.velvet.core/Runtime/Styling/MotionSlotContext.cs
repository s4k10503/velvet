using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Resolve before cancellation or class swaps remove starting values and ownership.
    internal sealed class MotionSlotContext
    {
        private readonly VisualElement _element;
        private readonly Func<ArbitraryProperty, bool> _drivenByRunningPlay;
        private Func<ArbitraryProperty, bool> _drivenByLivePlay;
        private readonly HashSet<string> _swapped = new(StringComparer.Ordinal);
        private readonly HashSet<ArbitraryProperty> _inlineBySwap = new();
        private readonly HashSet<ArbitraryProperty> _importantInlineBySwap = new();

        private MotionSlotContext(VisualElement element, Func<ArbitraryProperty, bool> drivenByRunningPlay,
            bool attached)
        {
            _element = element;
            _drivenByRunningPlay = drivenByRunningPlay;
            _drivenByLivePlay = drivenByRunningPlay;
            Attached = attached;
        }

        // False for ReadImportant's: a play planned off a panel takes from it only the important declarations of the
        // element's own, and reads no current value and no other resting holder.
        public bool Attached { get; }

        public static MotionSlotContext? Read(VisualElement element, string[]? fromClasses, string[]? toClasses,
            Func<ArbitraryProperty, bool> drivenByRunningPlay, Func<ArbitraryProperty, bool>? drivenByLivePlay = null)
        {
            if (element.panel == null)
            {
                return null;
            }
            var context = new MotionSlotContext(element, drivenByRunningPlay, attached: true);
            if (drivenByLivePlay != null) context._drivenByLivePlay = drivenByLivePlay;
            return Swapping(context, fromClasses, toClasses);
        }

        // An important declaration of the element's own holds its slot through a play on a panel or off one, as
        // !important outranks an animation in CSS.
        public static MotionSlotContext ReadImportant(VisualElement element, string[]? fromClasses, string[]? toClasses)
            => Swapping(new MotionSlotContext(element, static _ => false, attached: false), fromClasses, toClasses);

        private static MotionSlotContext Swapping(MotionSlotContext context, string[]? fromClasses, string[]? toClasses)
        {
            context.NoteSwapped(fromClasses);
            context.NoteSwapped(toClasses);
            return context;
        }

        private void NoteSwapped(string[]? classes)
        {
            if (classes == null)
            {
                return;
            }
            foreach (var cls in classes)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                _swapped.Add(core);
                if (StyleArbitraryValueResolver.IsInlineResolved(core) && StyleArbitraryValueResolver.TryParse(core, out var style))
                {
                    _inlineBySwap.Add(style.Property);
                    if (important) _importantInlineBySwap.Add(style.Property);
                }
            }
        }

        public IEnumerable<string> RestingClasses()
        {
            var resting = new List<string>();
            foreach (var cls in _element.GetClasses())
            {
                if (!_swapped.Contains(cls))
                {
                    resting.Add(StyleArbitraryValueResolver.TryGetProjection(_element)?.HasImportantClass(cls) == true
                        ? "!" + cls : cls);
                }
            }
            return resting;
        }

        // Running-play frames are current-value samples instead of resting holders.
        public bool HoldsInlineOutsideSwap(ArbitraryProperty slot)
        {
            if (_drivenByRunningPlay(slot)) return false;
            var longhands = StyleArbitraryLonghands.Of(slot);
            var owner = Owner(slot);
            var style = owner.style;
            // A projected face's hidden slot holds none of the element's own (ProjectedCornerFace.InlineOf).
            var hasInline = slot == ArbitraryProperty.BorderColor
                ? HoldsBorderEdge(owner, DivideEdge.Top, ProjectedCornerFace.InlineOf(owner, 1, style.borderTopColor))
                    || HoldsBorderEdge(owner, DivideEdge.Right, ProjectedCornerFace.InlineOf(owner, 2, style.borderRightColor))
                    || HoldsBorderEdge(owner, DivideEdge.Bottom, ProjectedCornerFace.InlineOf(owner, 3, style.borderBottomColor))
                    || HoldsBorderEdge(owner, DivideEdge.Left, ProjectedCornerFace.InlineOf(owner, 4, style.borderLeftColor))
                : TryReadInline(slot, out _) && !HasRunningNativeTransition(slot) && OwnsInline(owner, longhands);
            if (!hasInline) return false;
            foreach (var property in _inlineBySwap)
            {
                if (StyleArbitraryLonghands.Of(property).Overlaps(longhands))
                {
                    return false;
                }
            }
            return true;
        }

        // A face's sentinel holds the edge only where the face took it from an inline value of the element's own.
        private static bool HoldsBorderEdge(VisualElement owner, DivideEdge edge, StyleColor inline)
        {
            var longhand = StyleLonghandOf(edge);
            return (SilhouetteFaceStash.TryReadFace(owner, edge, out _, out var faceInline)
                    ? faceInline
                    : inline.keyword == StyleKeyword.Undefined)
                && !HasRunningNativeTransition(owner, longhand) && OwnsInline(owner, StyleLonghandSet.Of(longhand));
        }

        // An inline value a container holds for the element (StyleArbitraryValueResolver.HoldsForContainer) is not
        // one of the element's own, so it holds no slot against the element's own classes, which it gives way to;
        // a dash mask over a color of the element's own still stands for that color.
        private static bool OwnsInline(VisualElement owner, StyleLonghandSet longhands)
        {
            foreach (var held in HeldSlotGroups.EverySlot)
            {
                if (longhands.Contains(HeldSlotGroups.LonghandOf(held))
                    && StyleArbitraryValueResolver.HoldsForContainer(owner, held)
                    && !DivideDashPainter.MasksOwnColor(owner, held))
                {
                    return false;
                }
            }
            return true;
        }

        // Read off the layers rather than the inline translate, which an axis utility and a both-axis one write alike.
        public bool HoldsRestingAxisTranslate(ArbitraryProperty slot)
        {
            if (slot != ArbitraryProperty.TranslateX && slot != ArbitraryProperty.TranslateY)
            {
                return false;
            }
            if (_drivenByRunningPlay(slot) || _inlineBySwap.Contains(slot))
            {
                return false;
            }
            return StyleArbitraryValueResolver.HasLayer(Owner(slot), slot);
        }

        public bool HasImportantInlineOutsideSwap(ArbitraryProperty slot)
            => StyleArbitraryValueResolver.HasImportantInlineOutside(_element, slot, _importantInlineBySwap);

        // The resting layer holding a translate axis, as the resolver's TryWinningAxis picks between the axis's own
        // and a both-axis one, the axis taking a tie; false where a running play drives the axis or none rests.
        public bool TryRestingTranslateHolder(ArbitraryProperty slot, out ArbitraryStyle holder, out long key,
            out bool axisLayer)
        {
            holder = default;
            key = 0;
            axisLayer = false;
            if (_drivenByRunningPlay(slot))
            {
                return false;
            }
            var owner = Owner(slot);
            var hasAxis = StyleArbitraryValueResolver.TryRestingLayer(owner, slot, _inlineBySwap, out var axis, out var axisKey);
            var hasBoth = StyleArbitraryValueResolver.TryRestingLayer(owner, ArbitraryProperty.Translate, _inlineBySwap,
                out var both, out var bothKey);
            axisLayer = hasAxis && (!hasBoth || axisKey >= bothKey);
            holder = axisLayer ? axis : both;
            key = axisLayer ? axisKey : bothKey;
            return hasAxis || hasBoth;
        }

        // The key of a resting scale-x-* or scale-y-* holding a value the browser rejects, which leaves the element no
        // scale unless a scale-[…] at or above it is drawn alone.
        public bool TryRestingRejectedScaleAxis(out long key)
        {
            key = 0;
            if (_drivenByRunningPlay(ArbitraryProperty.Scale))
            {
                return false;
            }
            var owner = Owner(ArbitraryProperty.Scale);
            foreach (var axis in s_scaleAxes)
            {
                if (StyleArbitraryValueResolver.TryRestingLayer(owner, axis, _inlineBySwap, out var style, out key)
                    && style.Rejected)
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly ArbitraryProperty[] s_scaleAxes = { ArbitraryProperty.ScaleX, ArbitraryProperty.ScaleY };

        // A swap's tokens carry no variant, so a holder in force under one outranks them: the element draws it
        // whatever the swap writes, as it does a resting hover:scale-x-* that composes the scale.
        public bool HoldsRestingVariant(ArbitraryProperty slot)
        {
            if (_drivenByRunningPlay(slot))
            {
                return false;
            }
            var owner = Owner(slot);
            if (StyleArbitraryValueResolver.HoldsVariantLayer(owner, slot))
            {
                return true;
            }
            var projection = StyleArbitraryValueResolver.TryGetProjection(owner);
            if (projection == null)
            {
                return false;
            }
            var longhands = StyleArbitraryLonghands.Of(slot);
            foreach (var cls in owner.GetClasses())
            {
                if (!_swapped.Contains(cls) && projection.HoldsUnderVariant(cls, longhands))
                {
                    return true;
                }
            }
            return false;
        }

        // Whether the plan read through this is an exit's, which a gesture channel's value does not keep out.
        public bool ForExit { get; set; }

        // A gesture channel holds slot: its value is protected from the swap, as Framer Motion keeps a value a gesture
        // state sets out of the element's animate and variants while the state is on.
        public bool HoldsGesture(ArbitraryProperty slot)
        {
            if (ForExit)
            {
                return false;
            }
            var owner = Owner(slot);
            return StyleArbitraryValueResolver.HoldsGestureLayer(owner, slot)
                || StyleArbitraryValueResolver.TryGetProjection(owner)?.HoldsGesture(StyleArbitraryLonghands.Of(slot)) == true;
        }

        // Unlike the running-play predicate, a held play does not count: it rests at its start and the next play
        // takes that from StartFromHeld, in the held play's own unit.
        public bool DrivenByLivePlay(ArbitraryProperty slot) => _drivenByLivePlay(slot);

        public bool NativeTransitionRunning(ArbitraryProperty slot) => HasRunningNativeTransition(slot);

        // The rate per second a running native transition is moving the slot at, in `unit` for a length; null where
        // none is, where the slot is a color (whose magnitude reads as none), or where the engine does not give it up.
        public float? NativeVelocity(ArbitraryProperty slot, LengthUnit unit)
        {
            return TryFindRunningNative(slot, out var longhand)
                ? NativeTransitionVelocity.Of(Owner(slot), s_nativePropertyIds[(int)longhand]!,
                    value => NativeMagnitude(value, slot, unit))
                : null;
        }

        internal static float? NativeMagnitude(object value, ArbitraryProperty slot, LengthUnit unit)
        {
            switch (value)
            {
                case float number:
                    return number;
                case Length length:
                    return length.unit == unit ? length.value : null;
                case Translate translate:
                {
                    var axis = slot == ArbitraryProperty.TranslateX ? translate.x : translate.y;
                    return axis.unit == unit ? axis.value : null;
                }
                case Scale scale:
                    return scale.value.x;
                case Rotate rotate:
                    return rotate.angle.ToDegrees();
                default:
                    return null;
            }
        }

        public bool TryReadCurrent(ArbitraryProperty slot, LengthUnit unit, out ArbitraryStyle value)
        {
            if (slot == ArbitraryProperty.BorderColor)
            {
                return TryReadBorderColor(out value);
            }
            // A projected face draws the colour its hidden slot does not hold.
            if (FaceSlotOf(slot) is { } faceSlot && ProjectedCornerFace.Drawn(Owner(slot), faceSlot) is { } drawn)
            {
                value = new ArbitraryStyle(slot, drawn);
                return true;
            }
            if (HasRunningNativeTransition(slot))
            {
                return TryReadResolved(slot, out value)
                    && (MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit));
            }
            if (slot == ArbitraryProperty.Scale && Owner(slot).style.scale.keyword == StyleKeyword.Undefined)
            {
                return TryReadInline(slot, out value);
            }
            if (TryReadInline(slot, out value))
            {
                return MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit);
            }
            if (!TryReadResolved(slot, out value))
            {
                return false;
            }
            return MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit);
        }

        // What the element showed on slot at its last style pass, in unit, a skewed parent's seat taken off a
        // translate (SeatedTranslate.Unseated); false where it cannot be read so.
        public bool TryReadShown(ArbitraryProperty slot, LengthUnit unit, out ArbitraryStyle value)
        {
            if (slot == ArbitraryProperty.TranslateX || slot == ArbitraryProperty.TranslateY)
            {
                var owner = Owner(slot);
                var own = SeatedTranslate.Unseated(owner, owner.resolvedStyle.translate);
                return Pixels(slot, slot == ArbitraryProperty.TranslateX ? own.x : own.y, out value)
                    && (value.Unit == unit || TryConvert(slot, ref value, unit));
            }
            return TryReadResolved(slot, out value)
                && (MotionPropertyClassParser.IsColor(slot) || value.Unit == unit || TryConvert(slot, ref value, unit));
        }

        private bool HasRunningNativeTransition(ArbitraryProperty slot) => TryFindRunningNative(slot, out _);

        private bool TryFindRunningNative(ArbitraryProperty slot, out StyleLonghand found)
        {
            found = default;
            if (_drivenByRunningPlay(slot)) return false;
            var owner = Owner(slot);
            var longhands = StyleArbitraryLonghands.Of(slot);
            // MUTANT_SURVIVES(equivalent, boundary): with 88 longhands, no mapped set claims the extra index, so the native query is never reached.
            for (var i = 0; i < s_nativePropertyIds.Length; i++)
            {
                var longhand = (StyleLonghand)i;
                if (longhands.Contains(longhand) && HasRunningNativeTransition(owner, longhand))
                {
                    found = longhand;
                    return true;
                }
            }
            return false;
        }

        private static bool HasRunningNativeTransition(VisualElement owner, StyleLonghand longhand)
            => owner.panel != null && s_nativePropertyIds[(int)longhand] is { } id
                && ReadNativeMemberOrNull(s_hasRunningNative, owner, new[] { id }) is true;

        private static object? ReadNativeMemberOrNull(MemberInfo? member, object owner, object[]? arguments = null)
        {
            try
            {
                return member switch
                {
                    MethodInfo method => method.Invoke(owner, arguments),
                    PropertyInfo property => property.GetValue(owner),
                    _ => null,
                };
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException)
            {
                return null;
            }
        }

        private static readonly MethodInfo? s_hasRunningNative = EngineMember.HasRunningStyleAnimation.ResolveMethod();
        private static readonly PropertyInfo? s_nativePropertyId = EngineMember.StylePropertyNameId.ResolveProperty();
        private static readonly object?[] s_nativePropertyIds = NativePropertyIds();

        private static object?[] NativePropertyIds()
        {
            var ids = new object?[StyleUtilityProperties.LonghandCount];
            for (var i = 0; i < ids.Length; i++)
            {
                ids[i] = ReadNativeMemberOrNull(s_nativePropertyId, new StylePropertyName(StyleUtilityProperties.UssName((StyleLonghand)i)));
            }
            return ids;
        }

        private VisualElement Owner(ArbitraryProperty slot)
            => ReferenceEquals(ClipPathLayoutBox.StyleFor(_element, slot), _element.style)
                ? _element
                : ClipPathLayoutBox.Of(_element);

        private bool TryReadInline(ArbitraryProperty slot, out ArbitraryStyle value)
        {
            var style = Owner(slot).style;
            value = default;
            switch (slot)
            {
                case ArbitraryProperty.Opacity:
                    return Float(slot, style.opacity, out value);
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY:
                    return Translate(slot, SeatedTranslate.Read(Owner(slot)), out value);
                case ArbitraryProperty.Scale:
                    return Scale(slot, style.scale, out value);
                case ArbitraryProperty.Rotate:
                    return Rotate(slot, style.rotate, out value);
                case ArbitraryProperty.TextColor:
                    return Color(slot, style.color, out value);
                case ArbitraryProperty.BackgroundColor:
                    return FaceColor(slot, null, ProjectedCornerFace.InlineOf(Owner(slot), 0, style.backgroundColor), out value);
                case ArbitraryProperty.BorderTopColor:
                    return EdgeColor(slot, DivideEdge.Top, ProjectedCornerFace.InlineOf(Owner(slot), 1, style.borderTopColor),
                        out value);
                case ArbitraryProperty.BorderRightColor:
                    return EdgeColor(slot, DivideEdge.Right,
                        ProjectedCornerFace.InlineOf(Owner(slot), 2, style.borderRightColor), out value);
                case ArbitraryProperty.BorderBottomColor:
                    return EdgeColor(slot, DivideEdge.Bottom,
                        ProjectedCornerFace.InlineOf(Owner(slot), 3, style.borderBottomColor), out value);
                case ArbitraryProperty.BorderLeftColor:
                    return EdgeColor(slot, DivideEdge.Left, ProjectedCornerFace.InlineOf(Owner(slot), 4, style.borderLeftColor),
                        out value);
                case ArbitraryProperty.BorderTopWidth:
                    return Float(slot, style.borderTopWidth, out value);
                case ArbitraryProperty.BorderRightWidth:
                    return Float(slot, style.borderRightWidth, out value);
                case ArbitraryProperty.BorderBottomWidth:
                    return Float(slot, style.borderBottomWidth, out value);
                case ArbitraryProperty.BorderLeftWidth:
                    return Float(slot, style.borderLeftWidth, out value);
                default:
                    return s_inlineLengths.TryGetValue(slot, out var read) && Length(slot, read(style), out value);
            }
        }

        private bool TryReadResolved(ArbitraryProperty slot, out ArbitraryStyle value)
        {
            var resolved = Owner(slot).resolvedStyle;
            value = default;
            switch (slot)
            {
                case ArbitraryProperty.Opacity:
                    return Pixels(slot, resolved.opacity, out value);
                case ArbitraryProperty.TranslateX:
                    return Pixels(slot, resolved.translate.x, out value);
                case ArbitraryProperty.TranslateY:
                    return Pixels(slot, resolved.translate.y, out value);
                case ArbitraryProperty.Scale:
                {
                    var scale = resolved.scale.value;
                    return scale.x.Equals(scale.y) && Pixels(slot, scale.x, out value);
                }
                case ArbitraryProperty.Rotate:
                    return Pixels(slot, resolved.rotate.angle.ToDegrees(), out value);
                case ArbitraryProperty.TextColor:
                    value = new ArbitraryStyle(slot, resolved.color);
                    return true;
                case ArbitraryProperty.BackgroundColor:
                    value = new ArbitraryStyle(slot, Shown(Owner(slot), null, resolved.backgroundColor));
                    return true;
                case ArbitraryProperty.BorderTopColor:
                    value = new ArbitraryStyle(slot, Shown(Owner(slot), DivideEdge.Top, resolved.borderTopColor));
                    return true;
                case ArbitraryProperty.BorderRightColor:
                    value = new ArbitraryStyle(slot, Shown(Owner(slot), DivideEdge.Right, resolved.borderRightColor));
                    return true;
                case ArbitraryProperty.BorderBottomColor:
                    value = new ArbitraryStyle(slot, Shown(Owner(slot), DivideEdge.Bottom, resolved.borderBottomColor));
                    return true;
                case ArbitraryProperty.BorderLeftColor:
                    value = new ArbitraryStyle(slot, Shown(Owner(slot), DivideEdge.Left, resolved.borderLeftColor));
                    return true;
                default:
                    return s_resolvedLengths.TryGetValue(slot, out var read) && Pixels(slot, read(resolved), out value);
            }
        }

        // The ProjectedCornerFace colour slot (StyleCascade.FaceColor's order) a slot is, or null.
        private static int? FaceSlotOf(ArbitraryProperty slot)
        {
            var index = System.Array.IndexOf(s_faceSlots, slot);
            return index >= 0 ? index : null;
        }

        private static readonly ArbitraryProperty[] s_faceSlots =
        {
            ArbitraryProperty.BackgroundColor, ArbitraryProperty.BorderTopColor, ArbitraryProperty.BorderRightColor,
            ArbitraryProperty.BorderBottomColor, ArbitraryProperty.BorderLeftColor,
        };

        private bool TryReadBorderColor(out ArbitraryStyle value)
        {
            var owner = Owner(ArbitraryProperty.BorderColor);
            var style = owner.style;
            var resolved = owner.resolvedStyle;
            var native = !_drivenByRunningPlay(ArbitraryProperty.BorderColor);
            var top = ProjectedCornerFace.Drawn(owner, 1)
                ?? ReadBorderEdge(owner, native, DivideEdge.Top, style.borderTopColor, resolved.borderTopColor);
            var right = ProjectedCornerFace.Drawn(owner, 2)
                ?? ReadBorderEdge(owner, native, DivideEdge.Right, style.borderRightColor, resolved.borderRightColor);
            var bottom = ProjectedCornerFace.Drawn(owner, 3)
                ?? ReadBorderEdge(owner, native, DivideEdge.Bottom, style.borderBottomColor, resolved.borderBottomColor);
            var left = ProjectedCornerFace.Drawn(owner, 4)
                ?? ReadBorderEdge(owner, native, DivideEdge.Left, style.borderLeftColor, resolved.borderLeftColor);
            value = new ArbitraryStyle(ArbitraryProperty.BorderColor, top);
            return top.Equals(right) && top.Equals(bottom) && top.Equals(left);
        }

        private Color ReadBorderEdge(VisualElement owner, bool native, DivideEdge edge, StyleColor inline, Color resolved)
        {
            var longhand = StyleLonghandOf(edge);
            if (DivideDashPainter.IsMask(inline) && DivideDashPainter.TryReadPainted(_element, longhand, out var painted))
            {
                return painted;
            }
            if (SilhouetteFaceStash.TryReadFace(owner, edge, out var shown, out _))
            {
                return shown;
            }
            return native && HasRunningNativeTransition(owner, longhand) || inline.keyword != StyleKeyword.Undefined
                ? resolved : inline.value;
        }

        private bool TryConvert(ArbitraryProperty slot, ref ArbitraryStyle value, LengthUnit unit)
        {
            if (slot is ArbitraryProperty.TranslateX or ArbitraryProperty.TranslateY)
            {
                // A translate's percentage is of the element's own box.
                var box = Owner(slot).layout;
                var extent = slot == ArbitraryProperty.TranslateX ? box.width : box.height;
                if (!(extent > 0f) || float.IsInfinity(extent))
                {
                    return false;
                }
                var shifted = value.Unit == LengthUnit.Percent ? value.Value / 100f * extent : value.Value;
                value = new ArbitraryStyle(slot, unit == LengthUnit.Percent ? shifted / extent * 100f : shifted, unit);
                return true;
            }
            var parent = Owner(slot).hierarchy.parent;
            var horizontal = slot is ArbitraryProperty.Width or ArbitraryProperty.MinWidth or ArbitraryProperty.MaxWidth;
            var vertical = slot is ArbitraryProperty.Height or ArbitraryProperty.MinHeight or ArbitraryProperty.MaxHeight;
            if (parent == null || (!horizontal && !vertical))
            {
                return false;
            }
            var basis = horizontal ? parent.contentRect.width : parent.contentRect.height;
            if (!(basis > 0f) || float.IsInfinity(basis))
            {
                return false;
            }
            var pixels = value.Unit == LengthUnit.Percent ? value.Value / 100f * basis : value.Value;
            value = new ArbitraryStyle(slot, unit == LengthUnit.Percent ? pixels / basis * 100f : pixels, unit);
            return true;
        }

        private static bool Float(ArbitraryProperty slot, StyleFloat style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value, LengthUnit.Pixel);
            return style.keyword == StyleKeyword.Undefined;
        }

        private static bool Translate(ArbitraryProperty slot, StyleTranslate style, out ArbitraryStyle value)
        {
            value = default;
            if (style.keyword != StyleKeyword.Undefined)
            {
                return false;
            }
            var axis = slot == ArbitraryProperty.TranslateX ? style.value.x : style.value.y;
            value = new ArbitraryStyle(slot, axis.value, axis.unit);
            return true;
        }

        private static bool Scale(ArbitraryProperty slot, StyleScale style, out ArbitraryStyle value)
        {
            value = default;
            if (style.keyword != StyleKeyword.Undefined || !style.value.value.x.Equals(style.value.value.y))
            {
                return false;
            }
            value = new ArbitraryStyle(slot, style.value.value.x, LengthUnit.Pixel);
            return true;
        }

        private static bool Rotate(ArbitraryProperty slot, StyleRotate style, out ArbitraryStyle value)
        {
            value = default;
            if (style.keyword != StyleKeyword.Undefined)
            {
                return false;
            }
            value = new ArbitraryStyle(slot, style.value.angle.ToDegrees(), LengthUnit.Pixel);
            return true;
        }

        // A masked edge reads as the color its dash takes.
        private bool EdgeColor(ArbitraryProperty slot, DivideEdge edge, StyleColor style, out ArbitraryStyle value)
        {
            if (DivideDashPainter.IsMask(style)
                && DivideDashPainter.TryReadPainted(_element, StyleLonghandOf(edge), out var painted))
            {
                value = new ArbitraryStyle(slot, painted);
                return true;
            }
            return FaceColor(slot, edge, style, out value);
        }

        // A slot a silhouette face suppresses reads as the colour the face shows, which is an inline value of the
        // element's own only where the face took it from one (SilhouetteFaceStash.TryReadFace).
        private bool FaceColor(ArbitraryProperty slot, DivideEdge? side, StyleColor style, out ArbitraryStyle value)
        {
            if (SilhouetteFaceStash.TryReadFace(Owner(slot), side, out var shown, out var inline))
            {
                value = new ArbitraryStyle(slot, shown);
                return inline;
            }
            return Color(slot, style, out value);
        }

        // read, or the colour a silhouette face shows in its place where one suppresses the slot.
        private static Color Shown(VisualElement owner, DivideEdge? side, Color read)
            => SilhouetteFaceStash.TryReadFace(owner, side, out var shown, out _) ? shown : read;

#pragma warning disable CS8524 // no discard arm: a new edge has to name its longhand
        private static StyleLonghand StyleLonghandOf(DivideEdge edge) => edge switch
        {
            DivideEdge.Top => StyleLonghand.BorderTopColor,
            DivideEdge.Right => StyleLonghand.BorderRightColor,
            DivideEdge.Bottom => StyleLonghand.BorderBottomColor,
            DivideEdge.Left => StyleLonghand.BorderLeftColor,
        };
#pragma warning restore CS8524

        private static bool Color(ArbitraryProperty slot, StyleColor style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value);
            return style.keyword == StyleKeyword.Undefined;
        }

        private static bool Length(ArbitraryProperty slot, StyleLength style, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, style.value.value, style.value.unit);
            // MUTANT_SURVIVES(equivalent, clause removed): the Auto/None exclusions are redundant on the current 25 mapped public getter paths;
            // Given_FlaggedLengths_When_ReadInline_Then_TheGettersNormalizeNonNumericKeywords pins the keyword boundary.
            return style.keyword == StyleKeyword.Undefined && !style.value.IsAuto() && !style.value.IsNone();
        }

        private static bool Pixels(ArbitraryProperty slot, float pixels, out ArbitraryStyle value)
        {
            value = new ArbitraryStyle(slot, pixels, LengthUnit.Pixel);
            return !float.IsNaN(pixels) && !float.IsInfinity(pixels);
        }

        private static float Keyworded(StyleFloat value) => value.keyword == StyleKeyword.Undefined ? value.value : float.NaN;

        private static readonly Dictionary<ArbitraryProperty, Func<IStyle, StyleLength>> s_inlineLengths = new()
        {
            [ArbitraryProperty.Width] = s => s.width,
            [ArbitraryProperty.Height] = s => s.height,
            [ArbitraryProperty.MinWidth] = s => s.minWidth,
            [ArbitraryProperty.MinHeight] = s => s.minHeight,
            [ArbitraryProperty.MaxWidth] = s => s.maxWidth,
            [ArbitraryProperty.MaxHeight] = s => s.maxHeight,
            [ArbitraryProperty.FlexBasis] = s => s.flexBasis,
            [ArbitraryProperty.Top] = s => s.top,
            [ArbitraryProperty.Right] = s => s.right,
            [ArbitraryProperty.Bottom] = s => s.bottom,
            [ArbitraryProperty.Left] = s => s.left,
            [ArbitraryProperty.PaddingTop] = s => s.paddingTop,
            [ArbitraryProperty.PaddingRight] = s => s.paddingRight,
            [ArbitraryProperty.PaddingBottom] = s => s.paddingBottom,
            [ArbitraryProperty.PaddingLeft] = s => s.paddingLeft,
            [ArbitraryProperty.MarginTop] = s => s.marginTop,
            [ArbitraryProperty.MarginRight] = s => s.marginRight,
            [ArbitraryProperty.MarginBottom] = s => s.marginBottom,
            [ArbitraryProperty.MarginLeft] = s => s.marginLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = s => s.borderTopLeftRadius,
            [ArbitraryProperty.BorderTopRightRadius] = s => s.borderTopRightRadius,
            [ArbitraryProperty.BorderBottomLeftRadius] = s => s.borderBottomLeftRadius,
            [ArbitraryProperty.BorderBottomRightRadius] = s => s.borderBottomRightRadius,
            [ArbitraryProperty.FontSize] = s => s.fontSize,
            [ArbitraryProperty.LetterSpacing] = s => s.letterSpacing,
        };

        private static readonly Dictionary<ArbitraryProperty, Func<IResolvedStyle, float>> s_resolvedLengths = new()
        {
            [ArbitraryProperty.Width] = s => s.width,
            [ArbitraryProperty.Height] = s => s.height,
            [ArbitraryProperty.MinWidth] = s => Keyworded(s.minWidth),
            [ArbitraryProperty.MinHeight] = s => Keyworded(s.minHeight),
            [ArbitraryProperty.MaxWidth] = s => Keyworded(s.maxWidth),
            [ArbitraryProperty.MaxHeight] = s => Keyworded(s.maxHeight),
            [ArbitraryProperty.FlexBasis] = s => Keyworded(s.flexBasis),
            [ArbitraryProperty.Top] = s => s.top,
            [ArbitraryProperty.Right] = s => s.right,
            [ArbitraryProperty.Bottom] = s => s.bottom,
            [ArbitraryProperty.Left] = s => s.left,
            [ArbitraryProperty.PaddingTop] = s => s.paddingTop,
            [ArbitraryProperty.PaddingRight] = s => s.paddingRight,
            [ArbitraryProperty.PaddingBottom] = s => s.paddingBottom,
            [ArbitraryProperty.PaddingLeft] = s => s.paddingLeft,
            [ArbitraryProperty.MarginTop] = s => s.marginTop,
            [ArbitraryProperty.MarginRight] = s => s.marginRight,
            [ArbitraryProperty.MarginBottom] = s => s.marginBottom,
            [ArbitraryProperty.MarginLeft] = s => s.marginLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = s => s.borderTopLeftRadius,
            [ArbitraryProperty.BorderTopRightRadius] = s => s.borderTopRightRadius,
            [ArbitraryProperty.BorderBottomLeftRadius] = s => s.borderBottomLeftRadius,
            [ArbitraryProperty.BorderBottomRightRadius] = s => s.borderBottomRightRadius,
            [ArbitraryProperty.BorderTopWidth] = s => s.borderTopWidth,
            [ArbitraryProperty.BorderRightWidth] = s => s.borderRightWidth,
            [ArbitraryProperty.BorderBottomWidth] = s => s.borderBottomWidth,
            [ArbitraryProperty.BorderLeftWidth] = s => s.borderLeftWidth,
            [ArbitraryProperty.FontSize] = s => s.fontSize,
            [ArbitraryProperty.LetterSpacing] = s => Keyworded(s.letterSpacing),
        };
    }
}
